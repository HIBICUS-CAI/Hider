namespace Hider.Core.Mosaic.Methods
{
    /// <summary>Pixelation (spec §4.5): square cells of "block" px aligned to the area origin.</summary>
    public sealed class PixelateMethod : SkslMethod
    {
        public override string Id => "pixelate";
        public override string LabelKey => "method.pixelate";
        public override MethodKind Kind => MethodKind.Mosaic;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("block", "param.block", "px", 2, 256, 2, 64, 12, LerpWithOpacity: true, LerpFloor: 1),
        };
    }
}
