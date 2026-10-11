namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Gaussian blur (spec §4.5, D-5): only the fixed pre-blur stage with this method's "radius", no cells. The effective
    /// radius lerp(a, 0, radius), so opacity 0 shows the capture unchanged.
    /// </summary>
    public sealed class BlurMethod : SkslMethod, IPreBlurMethod
    {
        public override string Id => "blur";
        public override string LabelKey => "method.blur";
        public override MethodKind Kind => MethodKind.Mosaic;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("radius", "param.radius", "px", 0, 128, 0, 32, 8, LerpWithOpacity: true, LerpFloor: 0),
        };

        /// <summary>The blur is done by the pre-blur stage; the shader never changes the picture.</summary>
        protected internal override bool IsPassThrough(ParamValues _p)
        {
            return true;
        }

        public double PreBlurRadius(ParamValues _p)
        {
            return _p.Effective("radius");
        }
    }
}
