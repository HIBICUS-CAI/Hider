using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class CpuRendererTests
    {
        /// <summary>The dummy method plus a CPU path that fills gray 100 (or declines).</summary>
        private sealed class CpuDummy : IMosaicMethod, ICpuRenderer
        {
            private readonly DummyMethod shader = new("cpudummy", MethodKind.Mosaic, PlatformSupport.All);
            public bool Accept { get; init; } = true;
            public string Id => shader.Id;
            public string LabelKey => shader.LabelKey;
            public MethodKind Kind => shader.Kind;
            public PlatformSupport Support => shader.Support;
            public IReadOnlyList<ParamSpec> Params => shader.Params;

            public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
            {
                return shader.CreateShader(_inputs, _p);
            }

            public bool TryRender(SKPixmap _dst, MosaicInputs _inputs, ParamValues _p)
            {
                return Accept && _dst.Erase(new SKColor(100, 100, 100));
            }
        }

        private static SKBitmap Draw(IMosaicMethod _method, int _levels)
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var p = ParamValues.ForMosaic(_method, new MethodSettings<MosaicCommon> { Method = _method.Id }, 1.0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(16, 16) };
            return Render.Draw(32, 32, _c => new MosaicPipeline().Draw(_c, SKRect.Create(32, 32), inputs, _method, p, _levels));
        }

        [Fact]
        public void CpuCanvas_UsesTheCpuPath_ThenTheLevels()
        {
            using var plain = Draw(new CpuDummy(), 256);
            Assert.Equal(new SKColor(100, 100, 100), plain.GetPixel(31, 31));
            using var levels = Draw(new CpuDummy(), 2);
            Assert.Equal(new SKColor(0, 0, 0), levels.GetPixel(0, 0));
        }

        [Fact]
        public void DeclinedCpuPath_FallsBackToTheShader()
        {
            using var shaderPath = Draw(new CpuDummy { Accept = false }, 256);
            using var source = SyntheticImage.CreateImage(16, 16);
            using var expected = Render.Draw(32, 32, _c => _c.DrawImage(source, SKRect.Create(32, 32), new SKSamplingOptions(SKFilterMode.Nearest)));
            Assert.Equal(0, Golden.MaxDiff(expected, shaderPath));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(2, true)]
        [InlineData(8, true)]
        public void RasterSampler_MatchesSkiaSampling(int _scale, bool _linear)
        {
            using var image = SyntheticImage.CreateImage(64, 48);
            var sampling = new SKSamplingOptions(_linear ? SKFilterMode.Linear : SKFilterMode.Nearest);
            using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, SKMatrix.CreateScale(_scale, _scale));
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            int w = 64 * _scale + 7, h = 48 * _scale + 5; // past the edges: clamping
            using var expected = Render.Draw(w, h, _c => _c.DrawRect(0, 0, w, h, paint));
            using var sampler = RasterSampler.Create(image, _scale, _linear)!;
            int worst = 0;
            for (int y = 0; y < h; y += 3)
            {
                for (int x = 0; x < w; x += 3)
                {
                    uint s = sampler.Sample(x + 0.5f, y + 0.5f);
                    var e = expected.GetPixel(x, y);
                    worst = Math.Max(worst, Math.Abs((int)((s >> 16) & 0xFF) - e.Red));
                    worst = Math.Max(worst, Math.Abs((int)((s >> 8) & 0xFF) - e.Green));
                    worst = Math.Max(worst, Math.Abs((int)(s & 0xFF) - e.Blue));
                }
            }
            Assert.True(worst <= 1, $"max difference {worst}");
        }
    }
}
