using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>
    /// ICpuRenderer paths against the shader they stand in for (the shader is what the GPU draws). Float rounding may move a
    /// boundary by a pixel and linear sampling may differ by 1, so a small share of differing pixels is allowed; a logic
    /// difference changes far more.
    /// </summary>
    public class MethodConsistencyTests
    {
        private const int Width = 211, Height = 143;

        [Theory]
        [InlineData("hexcell", "radius", 10)]
        [InlineData("hexcell", "radius", 3)]
        [InlineData("hexcell", "radius", 1.1)]
        [InlineData("hexcell", "radius", 37)]
        public void Mosaic_CpuPathMatchesShader(string _id, string _key, double _value)
        {
            var method = MethodRegistry.Find(_id)!;
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            var settings = new MethodSettings<MosaicCommon> { Method = _id };
            settings.ParamsFor(_id)[_key] = _value;
            var p = ParamValues.ForMosaic(method, settings, 1.0);
            using var source = SyntheticImage.CreateImage(Width, Height);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                AssertClose(method, inputs, p, 0.005);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(4, 6, 1234)]
        [InlineData(1, 16, 7)]
        [InlineData(13.5, 2, 65535)]
        [InlineData(64, 64, 42)]
        public void Generated_NoiseCpuPathMatchesShader(double _scale, double _levels, int _seed)
        {
            var method = MethodRegistry.Find("noise")!;
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            var settings = new MethodSettings<GeneratedCommon> { Method = "noise" };
            settings.Common.Seed = _seed;
            settings.ParamsFor("noise")["scale"] = _scale;
            settings.ParamsFor("noise")["levels"] = _levels;
            var p = ParamValues.ForGenerated(method, settings);
            // The block / noise hash ends in fract(x · y) with x · y in the thousands, so FMA or evaluation order moves its value by
            // up to ~1e-3 between implementations (also between GPUs): pixels next to a step boundary may change step.
            AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(Width, Height) }, p, 0.02);
        }

        internal static void AssertClose(IMosaicMethod _method, MosaicInputs _inputs, ParamValues _p, double _maxShare)
        {
            int w = _inputs.AreaSize.Width, h = _inputs.AreaSize.Height;
            using var cpu = Render.Draw(w, h, c => new MosaicPipeline().Draw(c, SKRect.Create(w, h), _inputs, _method, _p, 256));
            using var shader = _method.CreateShader(_inputs, _p);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            using var reference = Render.Draw(w, h, c => c.DrawRect(0, 0, w, h, paint));
            int far = 0;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var a = cpu.GetPixel(x, y);
                    var b = reference.GetPixel(x, y);
                    int d = Math.Max(Math.Max(Math.Abs(a.Red - b.Red), Math.Abs(a.Green - b.Green)), Math.Max(Math.Abs(a.Blue - b.Blue), Math.Abs(a.Alpha - b.Alpha)));
                    if (d > 1)
                    {
                        far++;
                    }
                }
            }
            Assert.True(far <= w * h * _maxShare, $"{_method.Id}: {far} of {w * h} pixels differ by more than 1");
        }
    }
}
