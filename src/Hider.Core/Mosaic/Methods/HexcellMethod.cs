using SkiaSharp;

namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Hexagonal cell average (spec §4.5, D-5): pointy-top hexagons of "radius" px (centre to corner) on a lattice anchored at
    /// the area origin. Each cell shows one linear sample of the pyramid level that approximates its average (design §4.2).
    /// The effective radius lerp(a, 1, radius); at 1 px the method is the identity (decision 13).
    /// </summary>
    public sealed class HexcellMethod : SkslMethod
    {
        public override string Id => "hexcell";
        public override string LabelKey => "method.hexcell";
        public override MethodKind Kind => MethodKind.Mosaic;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("radius", "param.radius", "px", 2, 256, 2, 64, 10, LerpWithOpacity: true, LerpFloor: 1),
        };

        /// <summary>Centre of the hexagon containing (x, y), the same arithmetic as hexcell.sksl (for tests).</summary>
        public static (double X, double Y) CellCentre(double _x, double _y, double _radius)
        {
            double q = (Math.Sqrt(3) / 3 * _x - _y / 3) / _radius;
            double r = 2.0 / 3 * _y / _radius;
            double x = q, z = r, y = -x - z;
            double rx = Math.Floor(x + 0.5), ry = Math.Floor(y + 0.5), rz = Math.Floor(z + 0.5);
            double dx = Math.Abs(rx - x), dy = Math.Abs(ry - y), dz = Math.Abs(rz - z);
            if (dx > dy && dx > dz)
            {
                rx = -ry - rz;
            }
            else if (dy <= dz)
            {
                rz = -rx - ry;
            }
            return (_radius * Math.Sqrt(3) * (rx + rz / 2), _radius * 1.5 * rz);
        }

        protected internal override bool IsPassThrough(ParamValues _p)
        {
            return _p.Effective("radius") <= 1.0;
        }

        protected override SKShader SourceShader(MosaicInputs _inputs, ParamValues _p)
        {
            // A hexagon of radius r is about 1.73 r wide: sample the level whose texels are the largest power of two below it.
            double width = Math.Sqrt(3) * _p.Effective("radius");
            int level = width <= 1.0 ? 0 : Math.Min(_inputs.Pyramid.Count - 1, (int)Math.Floor(Math.Log2(width)));
            if (level <= 0)
            {
                return base.SourceShader(_inputs, _p);
            }
            float scale = 1 << level;
            return _inputs.Pyramid[level].ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp,
                new SKSamplingOptions(SKFilterMode.Linear), SKMatrix.CreateScale(scale, scale));
        }
    }
}
