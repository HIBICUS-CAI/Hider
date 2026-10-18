using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class PixelateMethodTests
    {
        private static readonly PixelateMethod method = (PixelateMethod)MethodRegistry.Find("pixelate")!;

        private static ParamValues Values(double? _block, double _opacity = 1.0)
        {
            var settings = new MethodSettings<MosaicCommon>();
            if (_block is { } block)
            {
                settings.ParamsFor("pixelate")["block"] = block;
            }
            return ParamValues.ForMosaic(method, settings, _opacity);
        }

        private static SKBitmap DrawShader(SKShader _shader, int _w, int _h)
        {
            using (_shader)
            {
                using var paint = new SKPaint { Shader = _shader, BlendMode = SKBlendMode.Src };
                return Render.Draw(_w, _h, _c => _c.DrawRect(0, 0, _w, _h, paint));
            }
        }

        [Fact]
        public void CT114_Identity()
        {
            Assert.Equal("pixelate", method.Id);
            Assert.Equal("method.pixelate", method.LabelKey);
            Assert.Equal(MethodKind.Mosaic, method.Kind);
            Assert.Equal(PlatformSupport.All, method.Support);
            Assert.IsAssignableFrom<ICellMethod>(method);
            Assert.False(((IMosaicMethod)method) is ICpuRenderer);
            Assert.False(((IMosaicMethod)method) is IPreBlurMethod);
        }

        [Fact]
        public void CT04_Params_BlockFadingToOne()
        {
            var spec = Assert.Single(method.Params);
            Assert.Equal(new ParamSpec("block", "param.block", ParamUnit.Px, 2, 256, 2, 64, 12, LerpWithOpacity: true, LerpFloor: 1), spec);
        }

        [Theory]
        [InlineData(null, 1.0, 12)]
        [InlineData(12.0, 1.0, 12)]
        [InlineData(12.0, 0.5, 7)]           // 6.5 rounded up (CT-44)
        [InlineData(12.0, 0.0, 1)]
        [InlineData(2.0, 1.0, 2)]
        [InlineData(1.0, 1.0, 2)]            // clamped to Min
        [InlineData(256.0, 1.0, 256)]
        [InlineData(1000.0, 1.0, 256)]
        [InlineData(6.0000000001, 1.0, 6)]   // a rounding error above a whole number is not a larger block
        [InlineData(6.01, 1.0, 7)]
        [InlineData(double.NaN, 1.0, 12)]
        public void CT44_EffectiveBlock_RoundedUp(double? _block, double _opacity, int _expected)
        {
            var p = Values(_block, _opacity);
            Assert.Equal(_expected, PixelateMethod.EffectiveBlock(p));
            Assert.Equal(_expected, method.CellSize(p));
            Assert.Equal(_expected <= 1, method.IsPassThrough(p));
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-7.0)]
        [InlineData(0.4)]
        public void CT44_EffectiveBlock_AtLeastOne_WithoutTheSpec(double _block)
        {
            var p = new ParamValues(Array.Empty<ParamSpec>(), new Dictionary<string, double> { ["block"] = _block }, 1.0);
            Assert.Equal(1, PixelateMethod.EffectiveBlock(p));
        }

        [Theory]
        [InlineData(2, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(7, 2)]
        [InlineData(8, 3)]
        [InlineData(12, 3)]
        [InlineData(31, 4)]
        [InlineData(64, 6)]
        [InlineData(256, 8)]
        public void CT130_Cells_OneLinearSampleOfTheLargestLevelNotAboveTheBlock(int _block, int _level)
        {
            // Each cell shows pyramid level floor(log2 block) sampled linearly at the centre of the cell's visible part.
            const int w = 211, h = 143;
            using var source = SyntheticImage.CreateImage(w, h);
            var p = Values(_block);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                using var drawn = Render.Draw(w, h, _c => new MosaicPipeline().Draw(_c, SKRect.Create(w, h), inputs, method, p, 256));
                int level = Math.Min(inputs.Pyramid.Count - 1, _level);
                using var sampler = RasterSampler.Create(inputs.Pyramid[level], 1 << level, true)!;
                for (int cy = 0; cy < h; cy += _block)
                {
                    for (int cx = 0; cx < w; cx += _block)
                    {
                        float mx = (cx + Math.Min(cx + _block, w)) / 2f, my = (cy + Math.Min(cy + _block, h)) / 2f;
                        uint s = sampler.Sample(mx, my);
                        var c = drawn.GetPixel(cx, cy);
                        Assert.InRange(c.Red, (int)((s >> 16) & 0xFF) - 1, (int)((s >> 16) & 0xFF) + 1);
                        Assert.InRange(c.Green, (int)((s >> 8) & 0xFF) - 1, (int)((s >> 8) & 0xFF) + 1);
                        Assert.InRange(c.Blue, (int)(s & 0xFF) - 1, (int)(s & 0xFF) + 1);
                        Assert.Equal(c, drawn.GetPixel(Math.Min(w, cx + _block) - 1, Math.Min(h, cy + _block) - 1));
                    }
                }
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT130_ShortPyramid_NearestOnTheSource()
        {
            // Block 7 wants level 2; with only the source the cell centres are sampled nearest.
            using var source = SyntheticImage.CreateImage(45, 24);
            var p = Values(7);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(45, 24) };
            using var drawn = DrawShader(method.CreateShader(inputs, p), 45, 24);
            using var plain = Render.ToBitmap(source);
            for (int y = 0; y < 24; y++)
            {
                for (int x = 0; x < 45; x++)
                {
                    int cx = x / 7 * 7, cy = y / 7 * 7;
                    int mx = (int)Math.Floor((cx + Math.Min(cx + 7, 45)) / 2.0), my = (int)Math.Floor((cy + Math.Min(cy + 7, 24)) / 2.0);
                    Assert.Equal(plain.GetPixel(mx, my), drawn.GetPixel(x, y));
                }
            }
        }

        [Theory]
        [InlineData(2.0, 1.0)]
        [InlineData(3.0, 1.0)]
        [InlineData(12.0, 1.0)]
        [InlineData(12.0, 0.5)]
        [InlineData(256.0, 1.0)]
        [InlineData(37.0, 0.3)]
        public void CT119_CellPath_SameAsTheShaderOnEveryPixel(double _block, double _opacity)
        {
            // The pipeline evaluates the shader once per cell (P-1); the GPU evaluates it per pixel: the same pixels.
            var p = Values(_block, _opacity);
            using var source = SyntheticImage.CreateImage(211, 143);
            var inputs = MosaicPipeline.Prepare(source, new SKPointI(17, -9), p, null, method);
            try
            {
                MethodConsistencyTests.AssertClose(method, inputs, p, 0);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT43_PassThrough_SourceShaderIsTheCapture()
        {
            using var source = SyntheticImage.CreateImage(30, 20);
            var p = Values(12, 0.0);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                using var drawn = DrawShader(method.CreateShader(inputs, p), 30, 20);
                using var plain = Render.ToBitmap(source);
                Assert.Equal(0, Golden.MaxDiff(plain, drawn));
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT120_BelowMinimumAndAboveMaximum_SameAsTheEnds()
        {
            using var min = Render.Mosaic("pixelate", _params: new() { ["block"] = 2 });
            using var below = Render.Mosaic("pixelate", _params: new() { ["block"] = -10 });
            using var max = Render.Mosaic("pixelate", _params: new() { ["block"] = 256 });
            using var above = Render.Mosaic("pixelate", _params: new() { ["block"] = 9999 });
            Assert.Equal(0, Golden.MaxDiff(min, below));
            Assert.Equal(0, Golden.MaxDiff(max, above));
        }
    }
}
