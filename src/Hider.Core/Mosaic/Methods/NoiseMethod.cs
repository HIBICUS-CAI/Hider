using System.Runtime.InteropServices;
using SkiaSharp;

namespace Hider.Core.Mosaic.Methods
{
    /// <summary>
    /// Generated noise (spec §4.5, FR-40): smooth value noise around the base color, posterised to "levels" steps. The lattice
    /// step is "scale" × 4 px and follows the area origin, so resizing only extends the pattern at the edges.
    /// </summary>
    public sealed class NoiseMethod : SkslMethod, ICpuRenderer
    {
        public override string Id => "noise";
        public override string LabelKey => "method.noise";
        public override MethodKind Kind => MethodKind.Generated;

        public override IReadOnlyList<ParamSpec> Params { get; } = new[]
        {
            new ParamSpec("scale", "param.noiseScale", "", 1, 64, 1, 16, 4),
            new ParamSpec("levels", "param.noiseLevels", "段", 2, 64, 2, 16, 6),
        };

        /// <summary>noise.sksl in C#: the lattice hashes once per node, then the same interpolation and steps per pixel.</summary>
        public bool TryRender(SKPixmap _dst, MosaicInputs _inputs, ParamValues _p)
        {
            float step = (float)UniformValue("scale", _p) * 4f;
            float levels = (float)UniformValue("levels", _p);
            float seed = SeedUniformValue(_p);
            int w = _dst.Width, h = _dst.Height;
            int cols = (int)MathF.Floor(w / step) + 2, rows = (int)MathF.Floor(h / step) + 2;
            var lattice = new float[cols * rows];
            for (int j = 0; j < rows; j++)
            {
                for (int i = 0; i < cols; i++)
                {
                    lattice[j * cols + i] = Hash(i, j, seed);
                }
            }
            var colors = new uint[(int)levels];
            for (int k = 0; k < colors.Length; k++)
            {
                float shift = 0.3f * (k / (levels - 1f) - 0.5f);
                colors[k] = 0xFF000000u | (Unorm((float)_p.Raw(CommonParams.BaseColorR) + shift) << 16)
                    | (Unorm((float)_p.Raw(CommonParams.BaseColorG) + shift) << 8) | Unorm((float)_p.Raw(CommonParams.BaseColorB) + shift);
            }
            var pixels = _dst.GetPixelSpan();
            for (int y = 0; y < h; y++)
            {
                var row = MemoryMarshal.Cast<byte, uint>(pixels.Slice(y * _dst.RowBytes, w * 4));
                float qy = (y + 0.5f) / step;
                float iy = MathF.Floor(qy), fy = qy - iy;
                float uy = fy * fy * (3f - 2f * fy);
                int j = Math.Min((int)iy, rows - 2);
                for (int x = 0; x < w; x++)
                {
                    float qx = (x + 0.5f) / step;
                    float ix = MathF.Floor(qx), fx = qx - ix;
                    float ux = fx * fx * (3f - 2f * fx);
                    int i = Math.Min((int)ix, cols - 2), n = j * cols + i;
                    float top = Mix(lattice[n], lattice[n + 1], ux), bottom = Mix(lattice[n + cols], lattice[n + cols + 1], ux);
                    float v = Mix(top, bottom, uy);
                    row[x] = colors[(int)MathF.Min(MathF.Floor(v * levels), levels - 1f)];
                }
            }
            return true;
        }

        private static float Hash(float _x, float _y, float _seed)
        {
            float px = Fract((_x + _seed * 0.731f) * 0.1031f), py = Fract((_y + _seed * 0.137f) * 0.1031f), pz = Fract((_x + _seed * 0.419f) * 0.1031f);
            float d = px * (py + 33.33f) + py * (pz + 33.33f) + pz * (px + 33.33f);
            px += d;
            py += d;
            pz += d;
            return Fract((px + py) * pz);
        }

        private static float Fract(float _v)
        {
            return _v - MathF.Floor(_v);
        }

        private static float Mix(float _a, float _b, float _t)
        {
            return _a + (_b - _a) * _t;
        }

        private static uint Unorm(float _v)
        {
            return (uint)MathF.Round(Math.Clamp(_v, 0f, 1f) * 255f);
        }

        protected override double UniformValue(string _key, ParamValues _p)
        {
            return _key == "levels" ? Math.Max(2, Math.Round(_p.Raw(_key))) : base.UniformValue(_key, _p);
        }
    }
}
