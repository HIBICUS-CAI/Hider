using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Skia;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.App.Tests.Areas
{
    /// <summary>
    /// Shared parts of the tests of the cover (MosaicPresenter fills an area with its fill color when the pattern cannot be
    /// drawn): areas, frames that draw and frames that throw, drawing a presenter at a scale, pixel checks and a fake Skia
    /// lease whose canvas fails.
    /// </summary>
    internal static class CoverKit
    {
        public const int W = 64, H = 48;
        public static readonly SKColor DefaultFill = new(0x68, 0x76, 0x8A);

        public static Area NewArea(PatternKind _pattern, string? _fill = AreaDefaults.FillColor, int _w = W, int _h = H)
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(10, 20, _w, _h), true);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Mosaic.Params, Hider.Core.Mosaic.MethodKind.Mosaic);
            Hider.Core.Persistence.AreasFile.FillDefaults(area.Generated.Params, Hider.Core.Mosaic.MethodKind.Generated);
            area.Pattern = _pattern;
            area.Fill.Color = _fill!;
            return area;
        }

        public static void Publish(FrameExchange _frames, int _w = W, int _h = H)
        {
            _frames.Back.Resize(_w, _h);
            SyntheticImage.CreatePixels(_w, _h).AsSpan().CopyTo(_frames.Back.Bgra.Span);
            _frames.Publish();
        }

        public static FrameExchange GoodFrame(int _w = W, int _h = H)
        {
            var frames = new FrameExchange();
            Publish(frames, _w, _h);
            return frames;
        }

        /// <summary>A front frame without pixels: no capture makes one, and drawing a mosaic of it throws.</summary>
        public static FrameExchange BrokenFrame()
        {
            var frames = new FrameExchange();
            Break(frames);
            return frames;
        }

        public static void Break(FrameExchange _frames)
        {
            _frames.Back.Resize(0, 0);
            _frames.Publish();
        }

        /// <summary>Lays the presenter out at <paramref name="_w"/> x <paramref name="_h"/> DIP and draws it into a bitmap.</summary>
        public static SKBitmap Draw(MosaicPresenter _presenter, double _w = W, double _h = H, int _pw = W, int _ph = H, double _scale = 1.0)
        {
            _presenter.Measure(new Size(_w, _h));
            _presenter.Arrange(new Rect(0, 0, _w, _h));
            using var target = new RenderTargetBitmap(new PixelSize(_pw, _ph), new Vector(96 * _scale, 96 * _scale));
            using (var context = target.CreateDrawingContext())
            {
                _presenter.Render(context);
            }
            var colorType = target.Format == Avalonia.Platform.PixelFormat.Rgba8888 ? SKColorType.Rgba8888 : SKColorType.Bgra8888;
            using var bitmap = new SKBitmap(new SKImageInfo(_pw, _ph, colorType, SKAlphaType.Premul));
            target.CopyPixels(new PixelRect(0, 0, _pw, _ph), bitmap.GetPixels(), bitmap.ByteCount, bitmap.RowBytes);
            using var image = SKImage.FromBitmap(bitmap);
            return Render.ToBitmap(image);
        }

        /// <summary>
        /// Draws the presenter the way an area window of <paramref name="_pw"/> x <paramref name="_ph"/> physical pixels lays
        /// it out at <paramref name="_scale"/> (the DIP size that truncates back to the same pixels, AreaGeometry.DipLength).
        /// </summary>
        public static SKBitmap DrawPhysical(MosaicPresenter _presenter, int _pw, int _ph, double _scale)
        {
            return Draw(_presenter, AreaGeometry.DipLength(_pw, _scale), AreaGeometry.DipLength(_ph, _scale), _pw, _ph, _scale);
        }

        /// <summary>The pixels in [0, w) x [0, h) that are not exactly <paramref name="_color"/> (opaque).</summary>
        public static int CountOther(SKBitmap _b, SKColor _color, int _w, int _h)
        {
            int n = 0;
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    if (_b.GetPixel(x, y) != _color)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        public static bool AllCovered(SKBitmap _b, SKColor _color) => CountOther(_b, _color, _b.Width, _b.Height) == 0;

        /// <summary>The pixels that are not fully opaque (where something under the area would show).</summary>
        public static int CountSeeThrough(SKBitmap _b)
        {
            int n = 0;
            for (int y = 0; y < _b.Height; y++)
            {
                for (int x = 0; x < _b.Width; x++)
                {
                    if (_b.GetPixel(x, y).Alpha != 255)
                    {
                        n++;
                    }
                }
            }
            return n;
        }

        public static SKColor Parse(string _hex) => new(Convert.ToByte(_hex.Substring(1, 2), 16), Convert.ToByte(_hex.Substring(3, 2), 16), Convert.ToByte(_hex.Substring(5, 2), 16));
    }

    /// <summary>
    /// A Skia lease on a raster surface of its own, scaled like Avalonia's canvas at that display scale (draws are in DIP).
    /// Its canvas throws (the exception of <c>fault</c>) on the first <c>failures</c> reads (negative: on every read),
    /// standing in for a canvas that fails mid-draw.
    /// </summary>
    internal sealed class FakeLease : ISkiaSharpApiLease
    {
        private readonly SKSurface surface;
        private readonly Func<Exception> fault;
        private int failures;

        public FakeLease(int _failures, SKColor _background, Func<Exception>? _fault = null, int _w = CoverKit.W, int _h = CoverKit.H, double _scale = 1.0)
        {
            failures = _failures;
            fault = _fault ?? (() => new InvalidOperationException("canvas lost"));
            surface = SKSurface.Create(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul))!;
            surface.Canvas.Clear(_background);
            surface.Canvas.Scale((float)_scale);
        }

        public int Disposals { get; private set; }

        public int CanvasReads { get; private set; }

        public SKCanvas SkCanvas
        {
            get
            {
                CanvasReads++;
                if (failures != 0)
                {
                    failures--;
                    throw fault();
                }
                return surface.Canvas;
            }
        }

        public GRContext GrContext => null!;

        public SKSurface SkSurface => surface;

        public double CurrentOpacity => 1;

        public ISkiaSharpPlatformGraphicsApiLease TryLeasePlatformGraphicsApi() => null!;

        public SKBitmap Pixels()
        {
            var info = surface.Canvas.DeviceClipBounds;
            var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
            return bitmap;
        }

        public void Dispose()
        {
            Disposals++;
        }

        public void Free()
        {
            surface.Dispose();
        }
    }
}
