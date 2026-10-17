using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Hider.Core;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Areas
{
    /// <summary>
    /// Draws an area's pattern (spec §4.5). Fill is a plain rectangle. Mosaic and generated patterns are drawn with Skia
    /// through <see cref="ISkiaSharpApiLeaseFeature"/>, on the GPU when Avalonia renders there and on the CPU raster otherwise.
    /// Results are kept in offscreen images and only redrawn when the frame, values or size change (FR-42, proposal P-1);
    /// a repaint just copies the cached image. Until the first frame arrives a mosaic area shows its generated pattern so the
    /// content stays covered (NFR-05). When a pattern cannot be drawn (an exception, no Skia, the window closing) the area is
    /// filled with its fill color instead, never left see-through.
    /// </summary>
    public sealed class MosaicPresenter : Control, IDisposable
    {
        /// <summary>GPU images of closed areas, freed by the next draw on the render thread (where the GrContext lives).</summary>
        private static readonly List<SKImage> retired = new();
        private static int backendLogged;

        private readonly object cacheGate = new();
        private bool disposed;
        private readonly GeneratedCache generated = new();
        private readonly MosaicPipeline pipeline = new();
        private SKImage? mosaicImage;
        private string? mosaicKey;
        private GRRecordingContext? mosaicContext;

        public Area? Model { get; set; }

        /// <summary>The captured underlay of this area, shared with the capture side.</summary>
        public FrameExchange? Frames { get; set; }

        /// <summary>Pattern drawings actually made (not repaints), for tests (CT-57, GT-23, GT-25).</summary>
        public int GeneratedRenderCount
        {
            get
            {
                lock (cacheGate)
                {
                    return generated.RenderCount;
                }
            }
        }

        public int MosaicRenderCount { get; private set; }

        /// <summary>How a draw gets Skia from Avalonia's context (tests replace it: no lease, a lease that fails).</summary>
        internal Func<ImmediateDrawingContext, ISkiaSharpApiLease?> LeaseSkia { get; set; } = _c => _c.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();

        private ImmutableSolidColorBrush? fillBrush;

        public override void Render(DrawingContext _context)
        {
            var model = Model;
            if (model is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }
            var bounds = new Rect(Bounds.Size);
            // The fill pattern, and the cover of the other patterns when they cannot be drawn.
            var (r, g, b) = ColorText.Parse(model.Fill.Color);
            var color = Color.FromRgb(r, g, b);
            if (fillBrush?.Color != color)
            {
                fillBrush = new ImmutableSolidColorBrush(color);
            }
            if (model.Pattern == PatternKind.Fill)
            {
                _context.FillRectangle(fillBrush, bounds);
                return;
            }
            // Everything the render thread needs, captured now on the UI thread.
            var size = new SKSizeI(Math.Max(1, model.Rect.Width), Math.Max(1, model.Rect.Height));
            var origin = new SKPointI(model.Rect.X, model.Rect.Y);
            var generatedMethod = MethodRegistry.Find(model.Generated.Method, MethodKind.Generated) ?? MethodRegistry.Find(AreaDefaults.GeneratedMethod)!;
            var generatedValues = ParamValues.ForGenerated(generatedMethod, model.Generated);
            DrawOp op;
            if (model.Pattern == PatternKind.Mosaic)
            {
                var method = MethodRegistry.Find(model.Mosaic.Method, MethodKind.Mosaic) ?? MethodRegistry.Find(AreaDefaults.MosaicMethod)!;
                var values = ParamValues.ForMosaic(method, model.Mosaic, model.Opacity);
                op = new DrawOp(this, bounds, size, origin, generatedMethod, generatedValues, method, values, model.Mosaic.Common.Levels, fillBrush);
            }
            else
            {
                op = new DrawOp(this, bounds, size, origin, generatedMethod, generatedValues, null, null, 256, fillBrush);
            }
            _context.Custom(op);
        }

        /// <summary>Draws into <paramref name="_canvas"/> (render thread); throws when the pattern cannot be drawn.</summary>
        private void Draw(SKCanvas _canvas, DrawOp _op)
        {
            FreeRetired();
            var context = _canvas.Context;
            if (Interlocked.Exchange(ref backendLogged, 1) == 0)
            {
                Log.Verbose($"pattern drawing: {(context is null ? "CPU raster" : $"GPU ({context.Backend})")}, render thread {Environment.CurrentManagedThreadId}");
            }
            var dst = SKRect.Create((float)_op.Bounds.Width, (float)_op.Bounds.Height);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
            lock (cacheGate)
            {
                if (disposed)
                {
                    // The window is closing: making new images here would leak them (GPU) past the retire list. Its last frames
                    // still cover the content.
                    Cover(_canvas, _op);
                    return;
                }
                if (_op.Method is not null && Frames is { } frames)
                {
                    var image = frames.Read((_frame, _version) => _frame is null ? null : MosaicImage(_frame, _version, _op, context));
                    if (image is not null)
                    {
                        _canvas.DrawImage(image, dst, sampling, paint);
                        return;
                    }
                }
                var pattern = generated.Get(_op.GeneratedMethod, _op.GeneratedValues, _op.Size, context);
                _canvas.DrawImage(pattern, dst, sampling, paint);
            }
        }

        /// <summary>The mosaic of the front frame, redrawn only when the frame, values, size or GPU context change.</summary>
        private SKImage MosaicImage(FrameBuffer _frame, long _version, DrawOp _op, GRRecordingContext? _context)
        {
            string key = $"{_version}|{_frame.Width}x{_frame.Height}|{_op.Method!.Id}|{_op.Values!.Fingerprint()}|{_op.Levels}|{_op.Origin}";
            if (mosaicImage is not null && key == mosaicKey && ReferenceEquals(_context, mosaicContext))
            {
                return mosaicImage;
            }
            var info = new SKImageInfo(_frame.Width, _frame.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pin = _frame.Bgra.Pin();
            SKImage source;
            unsafe
            {
                // CPU: no copy, the raster canvas reads the front buffer right here while the exchange is held. GPU: Skia defers
                // texture uploads of raster images until the flush at the end of the frame, after the exchange (and the pin)
                // are released and the buffer may be swapped, resized or collected, so the GPU gets its own copy (crashes in
                // gr_direct_context_flush_and_submit seen on Windows).
                source = _context is null
                    ? SKImage.FromPixels(info, (nint)pin.Pointer, _frame.Stride)
                    : SKImage.FromPixelCopy(info, (nint)pin.Pointer, _frame.Stride);
            }
            using (source)
            {
                var inputs = MosaicPipeline.Prepare(source, _op.Origin, _op.Values, _context, _op.Method);
                try
                {
                    using var surface = MosaicSurface(info, _context);
                    pipeline.Draw(surface.Canvas, SKRect.Create(info.Width, info.Height), inputs, _op.Method, _op.Values, _op.Levels);
                    mosaicImage?.Dispose();
                    // The snapshot no longer refers to the frame memory (a raster snapshot copies, a GPU one is a texture).
                    mosaicImage = surface.Snapshot();
                }
                finally
                {
                    MosaicPipeline.Release(inputs, source);
                }
            }
            mosaicKey = key;
            mosaicContext = _context;
            MosaicRenderCount++;
            return mosaicImage;
        }

        /// <summary>Fills the area with its fill color, opaque (the pattern could not be drawn).</summary>
        private static void Cover(SKCanvas _canvas, DrawOp _op)
        {
            var c = _op.CoverBrush.Color;
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src, Color = new SKColor(c.R, c.G, c.B) };
            _canvas.DrawRect(SKRect.Create((float)_op.Bounds.Width, (float)_op.Bounds.Height), paint);
        }

        private static SKSurface MosaicSurface(SKImageInfo _info, GRRecordingContext? _context)
        {
            return (_context is not null ? SKSurface.Create(_context, false, _info) : null) ?? SKSurface.Create(_info)
                ?? throw new InvalidOperationException("Cannot create the mosaic surface");
        }

        internal sealed class DrawOp : ICustomDrawOperation
        {
            private readonly MosaicPresenter owner;

            public DrawOp(MosaicPresenter _owner, Rect _bounds, SKSizeI _size, SKPointI _origin, IMosaicMethod _generatedMethod,
                ParamValues _generatedValues, IMosaicMethod? _method, ParamValues? _values, int _levels, ImmutableSolidColorBrush _cover)
            {
                owner = _owner;
                Bounds = _bounds;
                Size = _size;
                Origin = _origin;
                GeneratedMethod = _generatedMethod;
                GeneratedValues = _generatedValues;
                Method = _method;
                Values = _values;
                Levels = _levels;
                CoverBrush = _cover;
            }

            public Rect Bounds { get; }
            public SKSizeI Size { get; }
            public SKPointI Origin { get; }
            public IMosaicMethod GeneratedMethod { get; }
            public ParamValues GeneratedValues { get; }
            public IMosaicMethod? Method { get; }
            public ParamValues? Values { get; }
            public int Levels { get; }

            /// <summary>The area's fill color, for when the pattern cannot be drawn (immutable: safe on the render thread).</summary>
            public ImmutableSolidColorBrush CoverBrush { get; }

            public bool HitTest(Point _p) => Bounds.Contains(_p);

            public bool Equals(ICustomDrawOperation? _other) => false;

            public void Render(ImmediateDrawingContext _context)
            {
                if (!TryDraw(owner.LeaseSkia(_context)))
                {
                    // No Skia, or even the cover fill through it failed: Avalonia's own drawing covers the area (NFR-05).
                    _context.FillRectangle(CoverBrush, Bounds);
                }
            }

            /// <summary>
            /// Draws the pattern through <paramref name="_lease"/> (disposed here), or the cover when the pattern fails. False:
            /// nothing could be drawn through it (no lease, or the cover failed too).
            /// </summary>
            internal bool TryDraw(ISkiaSharpApiLease? _lease)
            {
                if (_lease is null)
                {
                    return false;
                }
                using (_lease)
                {
                    try
                    {
                        owner.Draw(_lease.SkCanvas, this);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Log.Verbose($"pattern draw failed: {e.Message}; the area is covered with its fill color");
                    }
                    try
                    {
                        Cover(_lease.SkCanvas, this);
                        return true;
                    }
                    catch (Exception e)
                    {
                        Log.Verbose($"cover through Skia failed: {e.Message}");
                        return false;
                    }
                }
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Drops the cached images (window closed; UI thread). Raster images are freed now; GPU textures must be freed on the
        /// render thread with its GrContext, so they wait for the next draw of any area (crashes seen on Windows otherwise).
        /// </summary>
        public void ReleaseCaches()
        {
            lock (cacheGate)
            {
                Retire(generated.Detach());
                Retire(mosaicImage);
                mosaicImage = null;
                mosaicKey = null;
            }
        }

        /// <summary>The area's window closed: its cached images go (<see cref="ReleaseCaches"/>).</summary>
        public void Dispose()
        {
            lock (cacheGate)
            {
                // Under the gate the render thread sees the flag; the images go through the retire list only, so a GPU
                // texture is still freed on the render thread (never here on the UI thread).
                disposed = true;
                Retire(generated.Detach());
                Retire(mosaicImage);
                mosaicImage = null;
                mosaicKey = null;
            }
        }

        private static void Retire(SKImage? _image)
        {
            if (_image is null)
            {
                return;
            }
            if (!_image.IsTextureBacked)
            {
                _image.Dispose();
                return;
            }
            lock (retired)
            {
                retired.Add(_image);
            }
        }

        /// <summary>Render thread, inside the Skia lease.</summary>
        private static void FreeRetired()
        {
            lock (retired)
            {
                foreach (var image in retired)
                {
                    image.Dispose();
                }
                retired.Clear();
            }
        }
    }
}
