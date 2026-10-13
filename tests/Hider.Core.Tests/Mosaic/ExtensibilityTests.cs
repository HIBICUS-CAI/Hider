using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class ExtensibilityTests
    {
        [Fact]
        public void CT19_DummyMethod_WorksWithoutExtraCode()
        {
            var dummy = new DummyMethod("dummy", MethodKind.Mosaic, PlatformSupport.All);

            // Parameters resolve through the generic path (defaults, clamping, opacity interpolation).
            var settings = new MethodSettings<MosaicCommon> { Method = dummy.Id };
            settings.ParamsFor(dummy.Id)["amount"] = 3;
            var p = ParamValues.ForMosaic(dummy, settings, 0.5);
            Assert.Equal(3, p.Raw("amount"));
            Assert.Equal(2, p.Effective("amount"));

            // JSON keeps its values even though the registry does not know it.
            var area = new Area { Id = "0f3b8d2e", Rect = new PhysicalRect(0, 0, 100, 100), Mosaic = settings };
            var json = AreasFile.ToJson(new AppState { Areas = { AreaState.FromArea(area, 1.0) } });
            var back = AreasFile.FromJson(json)!;
            AreasFile.Normalize(back);
            Assert.Equal(dummy.Id, back.Areas[0].Mosaic.Method);
            Assert.Equal(3, back.Areas[0].Mosaic.Params[dummy.Id]["amount"]);

            // The pipeline draws it.
            using var source = SolidImage(16, 16, SKColors.Red);
            using var surface = SKSurface.Create(new SKImageInfo(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul));
            new MosaicPipeline().Draw(surface.Canvas, SKRect.Create(16, 16), new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(16, 16) }, dummy, p, 256);
            using var bitmap = new SKBitmap(16, 16);
            surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
            Assert.Equal(SKColors.Red, bitmap.GetPixel(8, 8));
        }

        private static SKImage SolidImage(int _w, int _h, SKColor _color)
        {
            using var surface = SKSurface.Create(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(_color);
            return surface.Snapshot();
        }
    }
}
