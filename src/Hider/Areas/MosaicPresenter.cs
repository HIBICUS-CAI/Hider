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
    /// content stays covered (NFR-05).
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

        private ImmutableSolidColorBrush? fillBrush;

        public override void Render(DrawingContext _context)
        {
            var model = Model;
            if (model is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                return;
            }
            var bounds = new Rect(Bounds.Size);
            if (model.Pattern == PatternKind.Fill)
            {
                var (r, g, b) = ColorText.Parse(model.Fill.Color);
                var color = Color.FromRgb(r, g, b);
                if (fillBrush?.Color != color)
                {
                    fillBrush = new ImmutableSolidColorBrush(color);
                }
                _context.FillRectangle(fillBrush, bounds);
                return;
            }
            // Everything the render thread needs, captured now on the UI thread.
            var size = new SKSizeI(Math.Max(1, model.Rect.Width), Math.Max(1, model.Rect.Height));
            var origin = new SKPointI(model.Rect.X, model.Rect.Y);
            var generatedMethod = MethodRegistry.Find(model.Generated.Method) ?? MethodRegistry.Find(AreaDefaults.GeneratedMethod)!;
            var generatedValues = ParamValues.ForGenerated(generatedMethod, model.Generated);
            DrawOp op;
            if (model.Pattern == PatternKind.Mosaic)
            {
                var method = MethodRegistry.Find(model.Mosaic.Method) ?? MethodRegistry.Find(AreaDefaults.MosaicMethod)!;
                var values = ParamValues.ForMosaic(method, model.Mosaic, model.Opacity);
                op = new DrawOp(this, bounds, size, origin, generatedMethod, generatedValues, method, values, model.Mosaic.Common.Levels);
            }
            else
            {
                op = new DrawOp(this, bounds, size, origin, generatedMethod, generatedValues, null, null, 256);
            }
            _context.Custom(op);
        }

        /// <summary>Draws into <paramref name="_canvas"/> (render thread). Returns false when nothing could be drawn.</summary>
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
                    return; // the window is closing: making new images here would leak them (GPU) past the retire list
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

        private static SKSurface MosaicSurface(SKImageInfo _info, GRRecordingContext? _context)
        {
            return (_context is not null ? SKSurface.Create(_context, false, _info) : null) ?? SKSurface.Create(_info)
                ?? throw new InvalidOperationException("Cannot create the mosaic surface");
        }

        private sealed class DrawOp : ICustomDrawOperation
        {
            private readonly MosaicPresenter owner;

            public DrawOp(MosaicPresenter _owner, Rect _bounds, SKSizeI _size, SKPointI _origin, IMosaicMethod _generatedMethod,
                ParamValues _generatedValues, IMosaicMethod? _method, ParamValues? _values, int _levels)
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
            }

            public Rect Bounds { get; }
            public SKSizeI Size { get; }
            public SKPointI Origin { get; }
            public IMosaicMethod GeneratedMethod { get; }
            public ParamValues GeneratedValues { get; }
            public IMosaicMethod? Method { get; }
            public ParamValues? Values { get; }
            public int Levels { get; }

            public bool HitTest(Point _p) => Bounds.Contains(_p);

            public bool Equals(ICustomDrawOperation? _other) => false;

            public void Render(ImmediateDrawingContext _context)
            {
                var lease = _context.TryGetFeature<ISkiaSharpApiLeaseFeature>()?.Lease();
                if (lease is null)
                {
                    return;
                }
                using (lease)
                {
                    try
                    {
                        owner.Draw(lease.SkCanvas, this);
                    }
                    catch (Exception e)
                    {
                        Log.Verbose($"mosaic draw failed: {e.Message}");
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
