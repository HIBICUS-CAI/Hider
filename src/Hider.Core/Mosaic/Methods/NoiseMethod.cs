namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Generated noise (spec §4.5, FR-40): smooth value noise around the base color, posterised to "levels" steps. The lattice
    /// step is "scale" × 4 px and follows the area origin, so resizing only extends the pattern at the edges.
    /// </summary>
    public sealed class NoiseMethod : SkslMethod
    {
        public override string Id => "noise";
        public override string LabelKey => "method.noise";
        public override MethodKind Kind => MethodKind.Generated;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("scale", "param.noiseScale", "", 1, 64, 1, 16, 4),
            new ParamSpec("levels", "param.noiseLevels", "段", 2, 64, 2, 16, 6),
        };

        protected override double UniformValue(string _key, ParamValues _p)
        {
            return _key == "levels" ? Math.Max(2, Math.Round(_p.Raw(_key))) : base.UniformValue(_key, _p);
        }
    }
}
