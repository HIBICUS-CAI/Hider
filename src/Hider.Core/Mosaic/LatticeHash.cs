namespace Hider.Core.Mosaic
{
    /// <summary>
    /// The block / noise cell hash (FR-40), the same in C# and in block.sksl / noise.sksl. It chains the permutation
    /// x³ mod 251 (251 is prime and 3 does not divide 250) with additions, on whole numbers that stay below 2^16, so every
    /// step is exact in float32 and every backend (CPU, any GPU, with or without FMA) gives the same value. The mod uses a
    /// multiplication by 1/251, never a division (GLSL allows 2.5 ULP for division), and floors x + 0.5 so a rounding error
    /// cannot cross an integer. Unlike the mod-289 polynomial of simplex noise, a prime modulus leaves no residue class
    /// through which a difference passes unchanged, so different inputs do not line up into equal patterns.
    /// Cells repeat after 251² in each direction. The seed enters as three digits base 251; each step is a bijection of the
    /// running value whose differences depend on that value, so every one of the 65536 seeds gives its own pattern.
    /// </summary>
    public static class LatticeHash
    {
        public const float Modulus = 251f;
        private const float InverseModulus = 1f / 251f;

        /// <summary>x mod 251 for a whole number 0 ≤ x &lt; 2^16.</summary>
        public static float Mod(float _x)
        {
            return _x - MathF.Floor((_x + 0.5f) * InverseModulus) * Modulus;
        }

        /// <summary>x³ mod 251 for a whole number 0 ≤ x &lt; 251: a permutation of 0..250.</summary>
        public static float Permute(float _x)
        {
            return Mod(Mod(_x * _x) * _x);
        }

        /// <summary>The "seed" uniform (float3): the seed 0..65535 as three digits base 251, lowest first.</summary>
        public static (float Low, float Middle, float High) SeedDigits(double _seed)
        {
            long seed = ((long)_seed % 65536 + 65536) % 65536;
            return (seed % 251, seed / 251 % 251, seed / (251 * 251));
        }

        /// <summary>Hash of a cell (whole numbers 0 ≤ x, y &lt; 251²) in (0, 1), 251 steps.</summary>
        public static float Hash(float _x, float _y, (float Low, float Middle, float High) _seed)
        {
            float qx = MathF.Floor((_x + 0.5f) * InverseModulus), qy = MathF.Floor((_y + 0.5f) * InverseModulus);
            float rx = _x - qx * Modulus, ry = _y - qy * Modulus;
            // Each coordinate enters twice with a seed digit in between: one pass (x, then y) leaves visible diagonal streaks.
            float h = Permute(Mod(_seed.Low + rx));
            h = Permute(Mod(h + ry));
            h = Permute(Mod(h + _seed.Middle));
            h = Permute(Mod(h + rx));
            h = Permute(Mod(h + _seed.High));
            h = Permute(Mod(h + ry));
            h = Permute(Mod(h + qx));
            h = Permute(Mod(h + qy));
            return (h + 0.5f) * InverseModulus;
        }
    }
}
