using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// FR-42 as read in design §4.2: a generated pattern is drawn once into an offscreen image when its size or values change,
    /// and that image is reused for every repaint. Not thread-safe: one instance per area, used from the render thread.
    /// </summary>
    public sealed class GeneratedCache : IDisposable
    {
        private readonly MosaicPipeline pipeline = new();
        private SKImage? image;
        private string? key;
        private GRRecordingContext? context;

        /// <summary>How many times the pattern was actually drawn (CT-57).</summary>
        public int RenderCount { get; private set; }

        public SKImage Get(IMosaicMethod _method, ParamValues _p, SKSizeI _size, GRRecordingContext? _context = null)
        {
            string newKey = $"{_method.Id}|{_size.Width}x{_size.Height}|{_p.Fingerprint()}";
            if (image is not null && key == newKey && ReferenceEquals(context, _context))
            {
                return image;
            }
            image?.Dispose();
            var info = new SKImageInfo(Math.Max(1, _size.Width), Math.Max(1, _size.Height), SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = MosaicPipeline.CreateSurface(_context, info);
            var inputs = new MosaicInputs { AreaSize = new SKSizeI(info.Width, info.Height) };
            pipeline.Draw(surface.Canvas, SKRect.Create(info.Width, info.Height), inputs, _method, _p, 256);
            image = surface.Snapshot();
            key = newKey;
            context = _context;
            RenderCount++;
            return image;
        }

        /// <summary>Hands the cached image over (the caller frees it, e.g. on the render thread when it is a GPU texture).</summary>
        public SKImage? Detach()
        {
            var detached = image;
            image = null;
            key = null;
            return detached;
        }

        public void Dispose()
        {
            image?.Dispose();
            image = null;
        }
    }
}
