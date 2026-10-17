using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>The base class's own behaviour, through subclasses that override nothing optional.</summary>
    public class SkslMethodTests
    {
        /// <summary>pixelate.sksl with the base class defaults: no pass-through, effective uniforms, nearest source.</summary>
        private sealed class PlainMosaic : SkslMethod
        {
            public override string Id => "pixelate";
            public override string LabelKey => "method.pixelate";
            public override MethodKind Kind => MethodKind.Mosaic;
            public override IReadOnlyList<ParamSpec> Params { get; } = new[]
            {
                new ParamSpec("block", "param.block", ParamUnit.Px, 2, 256, 2, 64, 12, LerpWithOpacity: true, LerpFloor: 1),
            };

            public double Uniform(string _key, ParamValues _p) => UniformValue(_key, _p);

            public SKShader Source(MosaicInputs _inputs, ParamValues _p) => SourceShader(_inputs, _p);
        }

        /// <summary>block.sksl with the base class defaults (no rounding of the uniforms).</summary>
        private sealed class PlainGenerated : SkslMethod
        {
            public override string Id => "block";
            public override string LabelKey => "method.block";
            public override MethodKind Kind => MethodKind.Generated;
            public override IReadOnlyList<ParamSpec> Params { get; } = MethodRegistry.Find("block")!.Params;

            public static (float Low, float Middle, float High) Seed(ParamValues _p) => SeedUniformValue(_p);
        }

        private static ParamValues MosaicValues(SkslMethod _method, double _block, double _opacity = 1.0)
        {
            var settings = new MethodSettings<MosaicCommon>();
            settings.ParamsFor(_method.Id)["block"] = _block;
            return ParamValues.ForMosaic(_method, settings, _opacity);
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
        public void Support_DefaultsToAll()
        {
            Assert.Equal(PlatformSupport.All, new PlainMosaic().Support);
            Assert.Equal(PlatformSupport.All, new PlainGenerated().Support);
        }

        [Fact]
        public void Effect_CompiledOnceFromTheShaderNamedAfterTheId()
        {
            var method = new PlainMosaic();
            var effect = method.Effect;
            Assert.Same(effect, method.Effect);
            Assert.Contains("block", effect.Uniforms);
            Assert.NotSame(effect, new PlainMosaic().Effect); // per instance
        }

        [Fact]
        public void IsPassThrough_DefaultIsFalse()
        {
            var method = new PlainMosaic();
            Assert.False(method.IsPassThrough(MosaicValues(method, 12, 0.0)));
            Assert.False(method.IsPassThrough(MosaicValues(method, 2, 1.0)));
        }

        [Fact]
        public void UniformValue_DefaultIsTheEffectiveValue()
        {
            var method = new PlainMosaic();
            Assert.Equal(6.5, method.Uniform("block", MosaicValues(method, 12, 0.5)), 9);
            Assert.Equal(12, method.Uniform("block", MosaicValues(method, 12, 1.0)));
            Assert.Equal(0, method.Uniform("unknown", MosaicValues(method, 12, 1.0)));
        }

        [Fact]
        public void CreateShader_MosaicWithoutCapture_Throws()
        {
            var method = new PlainMosaic();
            var e = Assert.Throws<ArgumentException>(() => method.CreateShader(new MosaicInputs { AreaSize = new SKSizeI(4, 4) }, MosaicValues(method, 12)));
            Assert.Equal("_inputs", e.ParamName);
            Assert.StartsWith("pixelate needs a captured image", e.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void CreateShader_GeneratedWithoutCapture_Works()
        {
            var method = new PlainGenerated();
            var p = ParamValues.ForGenerated(method, new MethodSettings<GeneratedCommon>());
            using var shader = method.CreateShader(new MosaicInputs { AreaSize = new SKSizeI(8, 8) }, p);
            Assert.NotNull(shader);
            using var effectShader = method.CreateEffectShader(new MosaicInputs { AreaSize = new SKSizeI(8, 8) }, p);
            Assert.NotNull(effectShader);
        }

        [Fact]
        public void CreateShader_WithoutPassThrough_RunsTheEffect()
        {
            // Not a pass-through: the shader's cells appear, and the effect shader gives the same pixels. An odd block keeps
            // the sample points off texel edges (where nearest sampling may pick either neighbour).
            using var source = SyntheticImage.CreateImage(48, 24);
            var method = new PlainMosaic();
            var p = MosaicValues(method, 7);
            var inputs = new MosaicInputs { Source = source, AreaSize = new SKSizeI(48, 24) };
            using var viaCreate = DrawShader(method.CreateShader(inputs, p), 48, 24);
            using var viaEffect = DrawShader(method.CreateEffectShader(inputs, p), 48, 24);
            Assert.Equal(0, Golden.MaxDiff(viaCreate, viaEffect));
            using var plain = Render.ToBitmap(source);
            // Cell (0, 0) shows the nearest source pixel at its centre (3.5, 3.5).
            Assert.Equal(plain.GetPixel(3, 3), viaCreate.GetPixel(0, 0));
            Assert.Equal(plain.GetPixel(3, 3), viaCreate.GetPixel(6, 6));
            Assert.Equal(plain.GetPixel(10, 3), viaCreate.GetPixel(7, 0));
            Assert.Equal(plain.GetPixel(10, 17), viaCreate.GetPixel(13, 14));
        }

        [Fact]
        public void CreateEffectShader_PassesTheAreaSize()
        {
            // pixelate.sksl samples the centre of the visible part of a cell: with a 19 px wide area the last 7 px cell
            // [14, 19) is sampled at x = 16.5, with a 21 px wide one at 17.5, so the areaSize uniform reached the shader.
            using var source = SyntheticImage.CreateImage(21, 7);
            var method = new PlainMosaic();
            var p = MosaicValues(method, 7);
            using var plain = Render.ToBitmap(source);
            Assert.NotEqual(plain.GetPixel(16, 3), plain.GetPixel(17, 3));
            using var drawn = DrawShader(method.CreateEffectShader(new MosaicInputs { Source = source, AreaSize = new SKSizeI(19, 7) }, p), 19, 7);
            Assert.Equal(plain.GetPixel(16, 3), drawn.GetPixel(18, 0));
            using var wider = DrawShader(method.CreateEffectShader(new MosaicInputs { Source = source, AreaSize = new SKSizeI(21, 7) }, p), 19, 7);
            Assert.Equal(plain.GetPixel(17, 3), wider.GetPixel(18, 0));
        }

        [Fact]
        public void CreateEffectShader_UnroundedEffectiveValue()
        {
            // The base class passes 6.5 at opacity 0.5 (PixelateMethod rounds it up to 7): cells start at 0, 6.5, 13, ...
            using var source = SyntheticImage.CreateImage(40, 8);
            var method = new PlainMosaic();
            var p = MosaicValues(method, 12, 0.5);
            using var plain = Render.ToBitmap(source);
            using var drawn = DrawShader(method.CreateEffectShader(new MosaicInputs { Source = source, AreaSize = new SKSizeI(40, 8) }, p), 40, 8);
            Assert.Equal(plain.GetPixel(3, 3), drawn.GetPixel(5, 0));   // pixel centre 5.5: cell [0, 6.5), centre 3.25
            Assert.Equal(plain.GetPixel(9, 3), drawn.GetPixel(6, 0));   // pixel centre 6.5: cell [6.5, 13), centre 9.75 (a 7 px cell would give 3)
            Assert.NotEqual(plain.GetPixel(3, 3), plain.GetPixel(9, 3));
        }

        [Fact]
        public void CreateEffectShader_AreaOriginDoesNotMove_TheCells()
        {
            using var source = SyntheticImage.CreateImage(30, 20);
            var method = new PlainMosaic();
            var p = MosaicValues(method, 7);
            using var a = DrawShader(method.CreateEffectShader(new MosaicInputs { Source = source, AreaSize = new SKSizeI(30, 20) }, p), 30, 20);
            using var b = DrawShader(method.CreateEffectShader(new MosaicInputs { Source = source, AreaSize = new SKSizeI(30, 20), AreaOrigin = new SKPointI(13, -4) }, p), 30, 20);
            Assert.Equal(0, Golden.MaxDiff(a, b));
        }

        [Fact]
        public void SourceShader_DefaultIsTheCaptureNearestAndClamped()
        {
            using var source = SyntheticImage.CreateImage(10, 6);
            var method = new PlainMosaic();
            var inputs = new MosaicInputs { Source = source, AreaSize = new SKSizeI(10, 6) };
            using var drawn = DrawShader(method.Source(inputs, MosaicValues(method, 12)), 14, 9);
            using var plain = Render.ToBitmap(source);
            for (int y = 0; y < 9; y++)
            {
                for (int x = 0; x < 14; x++)
                {
                    Assert.Equal(plain.GetPixel(Math.Min(x, 9), Math.Min(y, 5)), drawn.GetPixel(x, y));
                }
            }
        }

        [Theory]
        [InlineData(1234, 230f, 4f, 0f)]
        [InlineData(65535, 24f, 10f, 1f)]
        [InlineData(0, 0f, 0f, 0f)]
        public void SeedUniformValue_IsTheSeedDigits(int _seed, float _low, float _middle, float _high)
        {
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.Seed = _seed;
            var p = ParamValues.ForGenerated(new PlainGenerated(), settings);
            Assert.Equal((_low, _middle, _high), PlainGenerated.Seed(p));
            Assert.Equal(LatticeHash.SeedDigits(_seed), PlainGenerated.Seed(p));
        }

        [Fact]
        public void Generated_BaseColorAndSeedReachTheShader()
        {
            // Variance 0: every pixel is the base color; another seed with variance changes the cells.
            var method = new PlainGenerated();
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.BaseColor = "#204060";
            settings.ParamsFor("block")["variance"] = 0;
            var inputs = new MosaicInputs { AreaSize = new SKSizeI(24, 24) };
            using var flat = DrawShader(method.CreateShader(inputs, ParamValues.ForGenerated(method, settings)), 24, 24);
            Assert.Equal(new SKColor(0x20, 0x40, 0x60), flat.GetPixel(5, 17));
            settings.ParamsFor("block")["variance"] = 1;
            using var a = DrawShader(method.CreateShader(inputs, ParamValues.ForGenerated(method, settings)), 24, 24);
            settings.Common.Seed = 99;
            using var b = DrawShader(method.CreateShader(inputs, ParamValues.ForGenerated(method, settings)), 24, 24);
            Assert.True(Golden.MaxDiff(a, b) > 0);
        }
    }
}
