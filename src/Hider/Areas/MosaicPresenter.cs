using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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
    public sealed class MosaicPresenter : Control
    {
        private readonly object cacheGate = new();
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
                _context.FillRectangle(new SolidColorBrush(Color.FromRgb(r, g, b)), bounds);
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
            var context = _canvas.Context;
            var dst = SKRect.Create((float)_op.Bounds.Width, (float)_op.Bounds.Height);
            using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
            var sampling = new SKSamplingOptions(SKFilterMode.Nearest);
            lock (cacheGate)
            {
                if (_op.Method is not null && Frames is { } frames)
                {
                    var image = frames.Read((frame, version) => frame is null ? null : MosaicImage(frame, version, _op, context));
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
                // No copy: the image reads the front buffer, which is not written while this read holds the exchange.
                source = SKImage.FromPixels(info, (nint)pin.Pointer, _frame.Stride);
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

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs _e)
        {
            base.OnDetachedFromVisualTree(_e);
        }

        /// <summary>Frees the cached images (window closed).</summary>
        public void ReleaseCaches()
        {
            lock (cacheGate)
            {
                generated.Dispose();
                mosaicImage?.Dispose();
                mosaicImage = null;
            }
        }
    }
}
