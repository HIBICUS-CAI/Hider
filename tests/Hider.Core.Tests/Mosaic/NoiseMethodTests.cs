using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class NoiseMethodTests
    {
        private static readonly NoiseMethod method = (NoiseMethod)MethodRegistry.Find("noise")!;

        private static ParamValues Values(double? _scale = null, double? _levels = null, string _color = "#68768A", int _seed = 1234)
        {
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.BaseColor = _color;
            settings.Common.Seed = _seed;
            if (_scale is { } scale)
            {
                settings.ParamsFor("noise")["scale"] = scale;
            }
            if (_levels is { } levels)
            {
                settings.ParamsFor("noise")["levels"] = levels;
            }
            return ParamValues.ForGenerated(method, settings);
        }

        /// <summary>The CPU path alone, into a fresh w x h bitmap.</summary>
        private static SKBitmap Cpu(ParamValues _p, int _w, int _h)
        {
            var bitmap = new SKBitmap(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var pixmap = bitmap.PeekPixels();
            Assert.True(method.TryRender(pixmap, new MosaicInputs { AreaSize = new SKSizeI(_w, _h) }, _p));
            return bitmap;
        }

        private static HashSet<SKColor> Colors(SKBitmap _bitmap)
        {
            var colors = new HashSet<SKColor>();
            for (int y = 0; y < _bitmap.Height; y++)
            {
                for (int x = 0; x < _bitmap.Width; x++)
                {
                    colors.Add(_bitmap.GetPixel(x, y));
                }
            }
            return colors;
        }

        /// <summary>The k-th of L colors: base + 0.3 · (k / (L - 1) - 0.5) on each channel, clamped.</summary>
        private static SKColor Step(string _color, int _k, int _levels)
        {
            var (r, g, b) = ColorText.Parse(_color);
            float shift = 0.3f * (_k / (_levels - 1f) - 0.5f);
            byte Channel(byte _c) => (byte)MathF.Round(Math.Clamp((float)(_c / 255.0) + shift, 0f, 1f) * 255f);
            return new SKColor(Channel(r), Channel(g), Channel(b));
        }

        [Fact]
        public void Identity()
        {
            Assert.Equal("noise", method.Id);
            Assert.Equal("method.noise", method.LabelKey);
            Assert.Equal(MethodKind.Generated, method.Kind);
            Assert.Equal(PlatformSupport.All, method.Support);
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            Assert.False(((IMosaicMethod)method) is ICellMethod);
            Assert.False(((IMosaicMethod)method) is IPreBlurMethod);
            Assert.False(method.IsPassThrough(Values()));
        }

        [Fact]
        public void Params_ScaleAndLevels()
        {
            Assert.Equal(new[]
            {
                new ParamSpec("scale", "param.noiseScale", ParamUnit.None, 1, 64, 1, 16, 4),
                new ParamSpec("levels", "param.noiseLevels", ParamUnit.Steps, 2, 64, 2, 16, 6),
            }, method.Params);
        }

        [Theory]
        [InlineData(2.0, 2)]
        [InlineData(2.4, 2)]
        [InlineData(2.5, 2)]   // Math.Round: halves to even
        [InlineData(3.5, 4)]
        [InlineData(2.6, 3)]
        [InlineData(1.0, 2)]   // clamped to Min
        [InlineData(7.0, 7)]
        public void Levels_RoundedToWholeSteps(double _levels, int _steps)
        {
            var p = Values(_levels: _levels, _scale: 1);
            using var bitmap = Cpu(p, 160, 160);
            var colors = Colors(bitmap);
            var palette = Enumerable.Range(0, _steps).Select(_k => Step("#68768A", _k, _steps)).ToHashSet();
            Assert.Subset(palette, colors);
            Assert.Equal(_steps, colors.Count); // a 40 x 40 lattice reaches every step
        }

        [Theory]
        [InlineData(64.0)]
        [InlineData(500.0)]  // clamped to 64
        public void Levels_Maximum(double _levels)
        {
            var p = Values(_levels: _levels, _scale: 1);
            using var bitmap = Cpu(p, 200, 200);
            var palette = Enumerable.Range(0, 64).Select(_k => Step("#68768A", _k, 64)).ToHashSet();
            Assert.Subset(palette, Colors(bitmap));
        }

        [Theory]
        [InlineData("#FFFFFF")]
        [InlineData("#000000")]
        [InlineData("#FF0000")]
        public void Colors_ClampedAtTheEndsOfTheRange(string _color)
        {
            var p = Values(_levels: 6, _color: _color);
            using var bitmap = Cpu(p, 128, 128);
            var palette = Enumerable.Range(0, 6).Select(_k => Step(_color, _k, 6)).ToHashSet();
            var colors = Colors(bitmap);
            Assert.Subset(palette, colors);
            Assert.All(colors, _c => Assert.Equal(255, _c.Alpha));
            if (_color == "#FFFFFF")
            {
                Assert.Contains(new SKColor(255, 255, 255), colors);
            }
            if (_color == "#000000")
            {
                Assert.Contains(new SKColor(0, 0, 0), colors);
            }
        }

        [Fact]
        public void TryRender_LatticeFollowsTheArea_LargerAreaExtendsIt()
        {
            var p = Values(_scale: 3);
            using var small = Cpu(p, 37, 23);
            using var large = Cpu(p, 101, 64);
            for (int y = 0; y < 23; y++)
            {
                for (int x = 0; x < 37; x++)
                {
                    Assert.Equal(small.GetPixel(x, y), large.GetPixel(x, y));
                }
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(1, 300)]
        [InlineData(300, 1)]
        [InlineData(5, 3)]
        public void TryRender_TinyAndThinAreas(int _w, int _h)
        {
            // Smaller than one lattice step (scale 64 = 256 px) or one pixel: two lattice nodes per axis are enough.
            using var big = Cpu(Values(_scale: 64), _w, _h);
            using var small = Cpu(Values(_scale: 1), _w, _h);
            Assert.All(Colors(big), _c => Assert.Equal(255, _c.Alpha));
            Assert.All(Colors(small), _c => Assert.Equal(255, _c.Alpha));
        }

        [Fact]
        public void TryRender_OverwritesEveryPixelOfTheTarget()
        {
            // The pipeline hands over a fresh area-sized bitmap; every pixel is written, opaque, whatever was there.
            using var bitmap = new SKBitmap(new SKImageInfo(10, 7, SKColorType.Bgra8888, SKAlphaType.Premul));
            bitmap.Erase(new SKColor(1, 2, 3, 4));
            using (var pixmap = bitmap.PeekPixels())
            {
                Assert.True(method.TryRender(pixmap, new MosaicInputs { AreaSize = new SKSizeI(10, 7) }, Values()));
            }
            Assert.All(Colors(bitmap), _c => Assert.Equal(255, _c.Alpha));
        }

        [Theory]
        [InlineData(1, 2, 0, "#68768A")]
        [InlineData(64, 2, 65535, "#FFFFFF")]
        [InlineData(1, 64, 1, "#000000")]
        [InlineData(64, 64, 4321, "#123456")]
        [InlineData(0.5, 1, 7, "#68768A")]       // below the minimum: clamped
        [InlineData(100, 100, 9, "#68768A")]     // above the maximum: clamped
        [InlineData(2.5, 6.5, 11, "#808080")]    // fractional scale, levels rounded to even
        public void CpuPath_MatchesTheShader_AtTheBoundaries(double _scale, double _levels, int _seed, string _color)
        {
            var p = Values(_scale, _levels, _color, _seed);
            MethodConsistencyTests.AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(211, 143) }, p, 0.002);
        }

        [Fact]
        public void BelowMinimumAndAboveMaximum_SameAsTheEnds()
        {
            using var minScale = Render.Generated("noise", 120, 80, new() { ["scale"] = 1 });
            using var belowScale = Render.Generated("noise", 120, 80, new() { ["scale"] = -3 });
            Assert.Equal(0, Golden.MaxDiff(minScale, belowScale));
            using var maxLevels = Render.Generated("noise", 120, 80, new() { ["levels"] = 64 });
            using var aboveLevels = Render.Generated("noise", 120, 80, new() { ["levels"] = 1000 });
            Assert.Equal(0, Golden.MaxDiff(maxLevels, aboveLevels));
            using var defaults = Render.Generated("noise", 120, 80);
            using var nan = Render.Generated("noise", 120, 80, new() { ["scale"] = double.NaN, ["levels"] = double.NaN });
            Assert.Equal(0, Golden.MaxDiff(defaults, nan));
        }

        [Fact]
        public void IndependentOfTheAreaOrigin()
        {
            using var a = Render.Generated("noise", 90, 60);
            using var b = Render.Generated("noise", 90, 60, _origin: new SKPointI(333, -21));
            Assert.Equal(0, Golden.MaxDiff(a, b));
        }
    }
}
