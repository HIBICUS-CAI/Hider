using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class BlurMethodTests
    {
        private static readonly BlurMethod method = (BlurMethod)MethodRegistry.Find("blur")!;

        private static ParamValues Values(double? _radius, double _opacity = 1.0, double _common = 0)
        {
            var settings = new MethodSettings<MosaicCommon>();
            settings.Common.Blur = _common;
            if (_radius is { } radius)
            {
                settings.ParamsFor("blur")["radius"] = radius;
            }
            return ParamValues.ForMosaic(method, settings, _opacity);
        }

        [Fact]
        public void CT114_Identity()
        {
            Assert.Equal("blur", method.Id);
            Assert.Equal("method.blur", method.LabelKey);
            Assert.Equal(MethodKind.Mosaic, method.Kind);
            Assert.Equal(PlatformSupport.All, method.Support);
            Assert.IsAssignableFrom<IPreBlurMethod>(method);
            Assert.False(((IMosaicMethod)method) is ICellMethod);
            Assert.False(((IMosaicMethod)method) is ICpuRenderer);
        }

        [Fact]
        public void CT05_Params_RadiusFadingToZero()
        {
            var spec = Assert.Single(method.Params);
            Assert.Equal(new ParamSpec("radius", "param.radius", ParamUnit.Px, 0, 128, 0, 32, 8, LerpWithOpacity: true, LerpFloor: 0), spec);
        }

        [Theory]
        [InlineData(null, 1.0, 8)]
        [InlineData(8.0, 0.5, 4)]
        [InlineData(8.0, 0.0, 0)]
        [InlineData(0.0, 1.0, 0)]
        [InlineData(128.0, 1.0, 128)]
        [InlineData(500.0, 1.0, 128)]
        [InlineData(-3.0, 1.0, 0)]
        [InlineData(double.NaN, 0.25, 2)]
        public void CT75_PreBlurRadius_IsTheEffectiveRadius(double? _radius, double _opacity, double _expected)
        {
            Assert.Equal(_expected, method.PreBlurRadius(Values(_radius, _opacity)), 9);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(0.5)]
        [InlineData(1.0)]
        public void CT75_IsPassThrough_Always(double _opacity)
        {
            Assert.True(method.IsPassThrough(Values(8, _opacity)));
            Assert.True(method.IsPassThrough(Values(0, _opacity)));
            Assert.True(method.IsPassThrough(Values(128, _opacity)));
        }

        [Fact]
        public void CT75_Shader_PassesTheBlurredInputThrough()
        {
            // The shader never changes the picture: the pipeline's output is the pre-blurred image.
            using var source = SyntheticImage.CreateImage(64, 40);
            var p = Values(6);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                Assert.NotSame(source, inputs.Source);
                using var expected = Render.ToBitmap(inputs.Source!);
                using var actual = Render.Draw(64, 40, _c => new MosaicPipeline().Draw(_c, SKRect.Create(64, 40), inputs, method, p, 256));
                Assert.Equal(0, Golden.MaxDiff(expected, actual));
                using var effect = method.CreateEffectShader(inputs, p);
                using var paint = new SKPaint { Shader = effect, BlendMode = SKBlendMode.Src };
                using var viaEffect = Render.Draw(64, 40, _c => _c.DrawRect(0, 0, 64, 40, paint));
                Assert.Equal(0, Golden.MaxDiff(expected, viaEffect));
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT75_RadiusZero_IsIdentity()
        {
            using var source = SyntheticImage.CreateImage(64, 40);
            using var expected = Render.ToBitmap(source);
            using var actual = Render.Mosaic("blur", _params: new() { ["radius"] = 0 }, _source: source);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void CT75_LargestRadius_BlursEverythingAlmostFlat_AboveMaxClamped()
        {
            using var source = SyntheticImage.CreateImage(64, 40);
            using var max = Render.Mosaic("blur", _params: new() { ["radius"] = 128 }, _source: source);
            using var above = Render.Mosaic("blur", _params: new() { ["radius"] = 1000 }, _source: source);
            Assert.Equal(0, Golden.MaxDiff(max, above));
            using var small = Render.Mosaic("blur", _params: new() { ["radius"] = 2 }, _source: source);
            Assert.True(Spread(max) < Spread(small));
        }

        [Fact]
        public void CT05_HalfOpacity_HalfTheRadius()
        {
            using var source = SyntheticImage.CreateImage(64, 40);
            using var half = Render.Mosaic("blur", _opacity: 0.5, _params: new() { ["radius"] = 12 }, _source: source);
            using var six = Render.Mosaic("blur", _params: new() { ["radius"] = 6 }, _source: source);
            Assert.Equal(0, Golden.MaxDiff(half, six));
        }

        /// <summary>Range of the red channel over the image.</summary>
        private static int Spread(SKBitmap _bitmap)
        {
            int min = 255, max = 0;
            for (int y = 0; y < _bitmap.Height; y++)
            {
                for (int x = 0; x < _bitmap.Width; x++)
                {
                    int r = _bitmap.GetPixel(x, y).Red;
                    min = Math.Min(min, r);
                    max = Math.Max(max, r);
                }
            }
            return max - min;
        }
    }
}
