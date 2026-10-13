using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using SkiaSharp;

namespace Hider.Core.Tests.Imaging
{
    /// <summary>Runs the pipeline on the CPU raster, the way goldens are made.</summary>
    public static class Render
    {
        public static SKBitmap Mosaic(string _method, double _opacity = 1.0, int _levels = 256, double _blur = 0,
            Dictionary<string, double>? _params = null, SKImage? _source = null, SKPointI _origin = default)
        {
            var method = MethodRegistry.Find(_method)!;
            var settings = new MethodSettings<MosaicCommon> { Method = _method };
            settings.Common.Blur = _blur;
            settings.Common.Levels = _levels;
            if (_params is not null)
            {
                settings.Params[_method] = _params;
            }
            var p = ParamValues.ForMosaic(method, settings, _opacity);
            using var owned = _source is null ? SyntheticImage.CreateImage() : null;
            var source = _source ?? owned!;
            var inputs = MosaicPipeline.Prepare(source, _origin, p, null, method);
            try
            {
                return Draw(source.Width, source.Height, _canvas => new MosaicPipeline().Draw(_canvas, SKRect.Create(source.Width, source.Height), inputs, method, p, _levels));
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        public static SKBitmap Generated(string _method, int _width, int _height, Dictionary<string, double>? _params = null,
            int _seed = 1234, string _baseColor = "#68768A", SKPointI _origin = default)
        {
            var method = MethodRegistry.Find(_method)!;
            var settings = new MethodSettings<GeneratedCommon> { Method = _method };
            settings.Common.Seed = _seed;
            settings.Common.BaseColor = _baseColor;
            if (_params is not null)
            {
                settings.Params[_method] = _params;
            }
            var p = ParamValues.ForGenerated(method, settings);
            var inputs = new MosaicInputs { AreaSize = new SKSizeI(_width, _height), AreaOrigin = _origin };
            return Draw(_width, _height, _canvas => new MosaicPipeline().Draw(_canvas, SKRect.Create(_width, _height), inputs, method, p, 256));
        }

        public static SKBitmap Draw(int _width, int _height, Action<SKCanvas> _draw)
        {
            var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(info);
            surface.Canvas.Clear(SKColors.Transparent);
            _draw(surface.Canvas);
            var bitmap = new SKBitmap(info);
            surface.ReadPixels(info, bitmap.GetPixels(), info.RowBytes, 0, 0);
            return bitmap;
        }

        public static SKBitmap ToBitmap(SKImage _image)
        {
            return Draw(_image.Width, _image.Height, _c => _c.DrawImage(_image, 0, 0));
        }
    }
}
