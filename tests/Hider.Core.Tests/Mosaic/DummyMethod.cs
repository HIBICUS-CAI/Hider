using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>A method defined only in tests, with its shader inline (extensibility checks CT-16 / CT-19).</summary>
    internal sealed class DummyMethod : IMosaicMethod
    {
        private const string Sksl = "uniform float amount; uniform float2 areaSize; uniform float2 areaOrigin; uniform shader src; half4 main(float2 p) { return src.eval(p); }";

        public DummyMethod(string _id, MethodKind _kind, PlatformSupport _support)
        {
            Id = _id;
            Kind = _kind;
            Support = _support;
        }

        public string Id { get; }
        public string LabelKey => "method.pixelate";
        public MethodKind Kind { get; }
        public PlatformSupport Support { get; }
        public IReadOnlyList<ParamSpec> Params { get; } = new[] { new ParamSpec("amount", "param.block", ParamUnit.Px, 1, 10, 1, 10, 5, LerpWithOpacity: true) };

        public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
        {
            using var effect = SKRuntimeEffect.CreateShader(Sksl, out _)!;
            var uniforms = new SKRuntimeEffectUniforms(effect) { ["amount"] = (float)_p.Effective("amount"), ["areaSize"] = new float[] { 16, 16 }, ["areaOrigin"] = new float[] { 0, 0 } };
            var children = new SKRuntimeEffectChildren(effect) { ["src"] = _inputs.Source!.ToShader() };
            return effect.ToShader(uniforms, children);
        }
    }
}
