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
            new ParamSpec(Blur, "param.blur", "px", 0, 128, 0, 32, 0, LerpWithOpacity: true, LerpFloor: 0),
            new ParamSpec(Levels, "param.levels", "色", 2, 256, 2, 256, 256),
            new ParamSpec(IntervalMs, "param.intervalMs", "ms", 0, 3600000, 10, 3600000, 100, LogScale: true),
            new ParamSpec(StripPx, "param.stripPx", "px", 0, 4096, 0, 128, 8),
        };

        /// <summary>Uniforms a shader of the kind declares besides its <see cref="IMosaicMethod.Params"/> keys.</summary>
        public static IReadOnlyList<string> FixedUniforms(MethodKind _kind)
        {
            return _kind == MethodKind.Mosaic
                ? new[] { AreaSizeUniform, AreaOriginUniform }
                : new[] { AreaSizeUniform, AreaOriginUniform, BaseColorUniform, SeedUniform };
        }
    }
}
