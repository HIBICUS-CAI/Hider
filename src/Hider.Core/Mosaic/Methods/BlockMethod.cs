namespace Hider.Core.Mosaic.Methods
{
    /// <summary>Generated blocks (spec §4.5, FR-40): cells colored by hash(cellX, cellY, seed) around the base color.</summary>
    public sealed class BlockMethod : SkslMethod
    {
        public override string Id => "block";
        public override string LabelKey => "method.block";
        public override MethodKind Kind => MethodKind.Generated;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("cellSize", "param.cellSize", "px", 2, 256, 2, 64, 12),
            new ParamSpec("variance", "param.variance", "%", 0, 1, 0, 1, 0.2),
            new ParamSpec("gap", "param.gap", "px", 0, 64, 0, 8, 0),
        };
    }
}
