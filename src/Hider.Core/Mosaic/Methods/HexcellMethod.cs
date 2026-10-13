using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Hexagonal cell average (spec §4.5, D-5): pointy-top hexagons of "radius" px (centre to corner) on a lattice anchored at
    /// the area origin. Each cell shows one linear sample of the pyramid level that approximates its average (design §4.2).
    /// The effective radius lerp(a, 1, radius); at 1 px the method is the identity (decision 13).
    /// </summary>
    public sealed class HexcellMethod : SkslMethod, ICpuRenderer
    {
        public override string Id => "hexcell";
        public override string LabelKey => "method.hexcell";
        public override MethodKind Kind => MethodKind.Mosaic;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("radius", "param.radius", ParamUnit.Px, 2, 256, 2, 64, 10, LerpWithOpacity: true, LerpFloor: 1),
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

        /// <summary>A hexagon of radius r is about 1.73 r wide: the level whose texels are the largest power of two below it.</summary>
        private static int Level(MosaicInputs _inputs, double _radius)
        {
            double width = Math.Sqrt(3) * _radius;
            int level = width <= 1.0 ? 0 : (int)Math.Floor(Math.Log2(width));
            return level > 0 ? Math.Min(_inputs.Pyramid.Count - 1, level) : 0; // only read the pyramid when it is sampled
        }

        /// <summary>The shader's rule with one sample per hexagon (a run of pixels in a row shares it).</summary>
        [MethodImpl(MethodImplOptions.AggressiveOptimization)] // a per-pixel loop called a few times: skip tier-0 code
        public bool TryRender(SKPixmap _dst, MosaicInputs _inputs, ParamValues _p)
        {
            float radius = (float)_p.Effective("radius");
            if (radius <= 1f)
            {
                return false; // pass-through
            }
            int level = Level(_inputs, radius);
            var image = level > 0 ? _inputs.Pyramid[level] : _inputs.Source!;
            using var sampler = RasterSampler.Create(image, 1 << level, level > 0);
            if (sampler is null)
            {
                return false;
            }
            int w = _dst.Width, h = _dst.Height;
            float maxX = _inputs.AreaSize.Width - 0.5f, maxY = _inputs.AreaSize.Height - 0.5f;
            var pixels = _dst.GetPixelSpan();
            for (int y = 0; y < h; y++)
            {
                var row = MemoryMarshal.Cast<byte, uint>(pixels.Slice(y * _dst.RowBytes, w * 4));
                float py = y + 0.5f;
                float lastX = float.NaN, lastZ = float.NaN;
                uint color = 0;
                for (int x = 0; x < w; x++)
                {
                    // Same float arithmetic as hexcell.sksl.
                    float px = x + 0.5f;
                    float cx = (0.57735027f * px - 0.33333333f * py) / radius;
                    float cz = (0.66666667f * py) / radius;
                    float cy = -cx - cz;
                    float rx = MathF.Floor(cx + 0.5f), ry = MathF.Floor(cy + 0.5f), rz = MathF.Floor(cz + 0.5f);
                    float dx = MathF.Abs(rx - cx), dy = MathF.Abs(ry - cy), dz = MathF.Abs(rz - cz);
                    if (dx > dy && dx > dz)
                    {
                        rx = -ry - rz;
                    }
                    else if (dy <= dz)
                    {
                        rz = -rx - ry;
                    }
                    if (rx != lastX || rz != lastZ)
                    {
                        lastX = rx;
                        lastZ = rz;
                        float sx = radius * (1.7320508f * (rx + rz * 0.5f)), sy = radius * (1.5f * rz);
                        color = sampler.Sample(Math.Clamp(sx, 0.5f, maxX), Math.Clamp(sy, 0.5f, maxY));
                    }
                    row[x] = color;
                }
            }
            return true;
        }

        protected override SKShader SourceShader(MosaicInputs _inputs, ParamValues _p)
        {
            int level = Level(_inputs, _p.Effective("radius"));
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
