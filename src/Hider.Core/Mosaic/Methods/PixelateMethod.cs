using SkiaSharp;

namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Pixelation (spec §4.5): square cells of "block" px aligned to the area origin, each showing (approximately) the
    /// average of its pixels. The average is one linear sample of the pyramid level whose texels are the largest power of two
    /// not above the block (design §4.2). The effective block lerp(a, 1, block) is rounded up (CT-44).
    /// </summary>
    public sealed class PixelateMethod : SkslMethod, ICellMethod
    {
        public override string Id => "pixelate";
        public override string LabelKey => "method.pixelate";
        public override MethodKind Kind => MethodKind.Mosaic;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("block", "param.block", "px", 2, 256, 2, 64, 12, LerpWithOpacity: true, LerpFloor: 1),
        };

        public static int EffectiveBlock(ParamValues _p)
        {
            return Math.Max(1, (int)Math.Ceiling(_p.Effective("block") - 1e-9));
        }

        public int CellSize(ParamValues _p)
        {
            return EffectiveBlock(_p);
        }

        /// <summary>1 px cells sample each pixel at its own centre (opacity 0, CT-43).</summary>
        protected internal override bool IsPassThrough(ParamValues _p)
        {
            return EffectiveBlock(_p) <= 1;
        }

        protected override double UniformValue(string _key, ParamValues _p)
        {
            return _key == "block" ? EffectiveBlock(_p) : base.UniformValue(_key, _p);
        }

        protected override SKShader SourceShader(MosaicInputs _inputs, ParamValues _p)
        {
            int level = (int)Math.Floor(Math.Log2(EffectiveBlock(_p)));
            if (level > 0)
            {
                level = Math.Min(_inputs.Pyramid.Count - 1, level); // only read the pyramid when it is sampled
            }
            if (level <= 0)
            {
                return base.SourceShader(_inputs, _p);
            }
            // Level k texel j covers source pixels [j * 2^k, (j + 1) * 2^k): scale its coordinates back to area pixels.
            float scale = 1 << level;
            return _inputs.Pyramid[level].ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale(scale, scale));
        }
    }
}
