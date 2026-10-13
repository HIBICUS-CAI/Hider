using Hider.Core.Model;

namespace Hider.Core.Mosaic
{
    /// <summary>Values shared by every method of a kind (spec §4.5) and the uniforms every shader of a kind declares.</summary>
    public static class CommonParams
    {
        public const string Blur = "blur";
        public const string Levels = "levels";
        public const string IntervalMs = "intervalMs";
        public const string StripPx = "stripPx";
        public const string Seed = "seed";
        public const string BaseColorR = "baseColorR";
        public const string BaseColorG = "baseColorG";
        public const string BaseColorB = "baseColorB";

        public const string AreaSizeUniform = "areaSize";
        public const string AreaOriginUniform = "areaOrigin";
        public const string SeedUniform = "seed";
        public const string BaseColorUniform = "baseColor";

        /// <summary>Rows of the mosaic settings below the method's own rows (design canvas 4b).</summary>
        public static readonly IReadOnlyList<ParamSpec> Mosaic = new[]
        {
            new ParamSpec(Blur, "param.blur", ParamUnit.Px, 0, 128, 0, 32, AreaDefaults.MosaicBlur, LerpWithOpacity: true, LerpFloor: 0),
            new ParamSpec(Levels, "param.levels", ParamUnit.Colors, 2, 256, 2, 256, AreaDefaults.MosaicLevels),
            new ParamSpec(IntervalMs, "param.intervalMs", ParamUnit.Ms, 0, 3600000, 10, 3600000, AreaDefaults.IntervalMsForCurrentOs, LogScale: true),
            new ParamSpec(StripPx, "param.stripPx", ParamUnit.Px, 0, 4096, 0, 128, AreaDefaults.StripPx),
        };

        /// <summary>Uniforms a shader of the kind declares besides its <see cref="IMosaicMethod.Params"/> keys.</summary>
        public static IReadOnlyList<string> FixedUniforms(MethodKind _kind)
        {
            return _kind == MethodKind.Mosaic ? mosaicUniforms : generatedUniforms;
        }

        private static readonly string[] mosaicUniforms = { AreaSizeUniform, AreaOriginUniform };
        private static readonly string[] generatedUniforms = { AreaSizeUniform, AreaOriginUniform, BaseColorUniform, SeedUniform };
    }
}
