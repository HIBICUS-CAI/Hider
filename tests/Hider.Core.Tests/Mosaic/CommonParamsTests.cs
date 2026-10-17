using Hider.Core.Model;
using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    public class CommonParamsTests
    {
        private static readonly string[] mosaicKeys = { "blur", "levels", "intervalMs", "stripPx" };
        private static readonly string[] mosaicUniforms = { "areaSize", "areaOrigin" };
        private static readonly string[] generatedUniforms = { "areaSize", "areaOrigin", "baseColor", "seed" };

        [Fact]
        public void CT15_Keys_AreTheJsonAndUniformNames()
        {
            // Stored in areas.json and declared in the shaders: renaming one breaks files or shaders.
            Assert.Equal("blur", CommonParams.Blur);
            Assert.Equal("levels", CommonParams.Levels);
            Assert.Equal("intervalMs", CommonParams.IntervalMs);
            Assert.Equal("stripPx", CommonParams.StripPx);
            Assert.Equal("seed", CommonParams.Seed);
            Assert.Equal("baseColorR", CommonParams.BaseColorR);
            Assert.Equal("baseColorG", CommonParams.BaseColorG);
            Assert.Equal("baseColorB", CommonParams.BaseColorB);
            Assert.Equal("areaSize", CommonParams.AreaSizeUniform);
            Assert.Equal("areaOrigin", CommonParams.AreaOriginUniform);
            Assert.Equal("seed", CommonParams.SeedUniform);
            Assert.Equal("baseColor", CommonParams.BaseColorUniform);
        }

        [Fact]
        public void CT117_Mosaic_RowsInTheOrderOfTheSettings()
        {
            Assert.Equal(mosaicKeys, CommonParams.Mosaic.Select(_s => _s.Key));
        }

        [Fact]
        public void CT117_Mosaic_Blur()
        {
            var s = CommonParams.Mosaic[0];
            Assert.Equal(new ParamSpec("blur", "param.blur", ParamUnit.Px, 0, 128, 0, 32, AreaDefaults.MosaicBlur, LerpWithOpacity: true, LerpFloor: 0), s);
            Assert.Equal(0, s.Default);
            Assert.Equal(0, s.Effective(8, 0.0)); // fades out with the opacity
        }

        [Fact]
        public void CT117_Mosaic_Levels()
        {
            var s = CommonParams.Mosaic[1];
            Assert.Equal(new ParamSpec("levels", "param.levels", ParamUnit.Colors, 2, 256, 2, 256, 256), s);
            Assert.False(s.LerpWithOpacity);
            Assert.False(s.LogScale);
        }

        [Fact]
        public void CT07_Mosaic_Interval_LogSliderReachingZero()
        {
            var s = CommonParams.Mosaic[2];
            Assert.Equal(new ParamSpec("intervalMs", "param.intervalMs", ParamUnit.Ms, 0, 3600000, 10, 3600000, AreaDefaults.IntervalMsForCurrentOs, LogScale: true), s);
            Assert.Equal(OperatingSystem.IsWindows() ? 100 : 300000, s.Default);
        }

        [Fact]
        public void CT117_Mosaic_StripHeight()
        {
            var s = CommonParams.Mosaic[3];
            Assert.Equal(new ParamSpec("stripPx", "param.stripPx", ParamUnit.Px, 0, 4096, 0, 128, 8), s);
        }

        [Fact]
        public void CT15_FixedUniforms_PerKind()
        {
            Assert.Equal(mosaicUniforms, CommonParams.FixedUniforms(MethodKind.Mosaic));
            Assert.Equal(generatedUniforms, CommonParams.FixedUniforms(MethodKind.Generated));
            Assert.Same(CommonParams.FixedUniforms(MethodKind.Generated), CommonParams.FixedUniforms(MethodKind.Generated));
        }

        [Fact]
        public void CT15_FixedUniforms_EveryShaderDeclaresThem()
        {
            foreach (var method in MethodRegistry.All)
            {
                using var effect = ShaderSource.Compile(method.Id);
                Assert.All(CommonParams.FixedUniforms(method.Kind), _u => Assert.Contains(_u, effect.Uniforms));
            }
        }
    }
}
