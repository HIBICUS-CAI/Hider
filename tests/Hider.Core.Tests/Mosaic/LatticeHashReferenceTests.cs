using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary><see cref="LatticeHash"/> against an integer reference of the same steps, and its edge values.</summary>
    public class LatticeHashReferenceTests
    {
        private static long IntPermute(long _x)
        {
            return _x * _x % 251 * _x % 251;
        }

        /// <summary>The documented hash with exact integer arithmetic: the result index 0..250.</summary>
        private static long IntHash(long _x, long _y, (long Low, long Middle, long High) _seed)
        {
            long qx = _x / 251, qy = _y / 251, rx = _x % 251, ry = _y % 251;
            long h = IntPermute((_seed.Low + rx) % 251);
            h = IntPermute((h + ry) % 251);
            h = IntPermute((h + _seed.Middle) % 251);
            h = IntPermute((h + rx) % 251);
            h = IntPermute((h + _seed.High) % 251);
            h = IntPermute((h + ry) % 251);
            h = IntPermute((h + qx) % 251);
            h = IntPermute((h + qy) % 251);
            return h;
        }

        [Fact]
        public void CT121_Modulus_Is251()
        {
            Assert.Equal(251f, LatticeHash.Modulus);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(250, 250)]
        [InlineData(251, 0)]
        [InlineData(252, 1)]
        [InlineData(501, 250)]
        [InlineData(502, 0)]
        [InlineData(63000, 250)]
        [InlineData(63001, 0)]
        [InlineData(65535, 24)]
        public void CT121_Mod_ExactAtTheBoundaries(float _x, float _expected)
        {
            Assert.Equal(_expected, LatticeHash.Mod(_x));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(2, 8)]
        [InlineData(7, 92)]      // 343 - 251
        [InlineData(250, 250)]   // (-1)³ = -1
        public void CT121_Permute_Cubes(float _x, float _expected)
        {
            Assert.Equal(_expected, LatticeHash.Permute(_x));
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(250, 250, 0, 0)]
        [InlineData(251, 0, 1, 0)]
        [InlineData(63000, 250, 250, 0)]
        [InlineData(63001, 0, 0, 1)]
        [InlineData(65535, 24, 10, 1)]
        [InlineData(65536, 0, 0, 0)]       // wraps at 2^16
        [InlineData(-1, 24, 10, 1)]        // negative: wraps to 65535
        [InlineData(-65537, 24, 10, 1)]
        [InlineData(1234.9, 230, 4, 0)]    // fractions are cut off (1234 = 4 · 251 + 230)
        [InlineData(-0.5, 0, 0, 0)]
        public void CT121_SeedDigits_ThreeDigitsBase251(double _seed, float _low, float _middle, float _high)
        {
            Assert.Equal((_low, _middle, _high), LatticeHash.SeedDigits(_seed));
        }

        [Fact]
        public void CT121_SeedDigits_AlwaysWholeDigitsBelow251()
        {
            foreach (double seed in new[] { -1e9, -70000.0, -1, 0, 1, 12345.678, 65535, 65536, 1e9 })
            {
                var (low, middle, high) = LatticeHash.SeedDigits(seed);
                foreach (float d in new[] { low, middle, high })
                {
                    Assert.InRange(d, 0, 250);
                    Assert.Equal(MathF.Floor(d), d);
                }
                Assert.InRange(low + 251 * middle + 251 * 251 * high, 0, 65535);
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1234)]
        [InlineData(65535)]
        public void CT121_Hash_EqualsTheIntegerReference(int _seed)
        {
            var digits = LatticeHash.SeedDigits(_seed);
            var intDigits = ((long)digits.Low, (long)digits.Middle, (long)digits.High);
            // Small cells, cells around the 251 period and the largest cells (251² - 1).
            var coordinates = Enumerable.Range(0, 40).Concat(Enumerable.Range(245, 12)).Concat(Enumerable.Range(62990, 11)).ToArray();
            foreach (int y in coordinates)
            {
                foreach (int x in coordinates)
                {
                    float h = LatticeHash.Hash(x, y, digits);
                    long index = IntHash(x, y, intDigits);
                    Assert.Equal((index + 0.5f) * (1f / 251f), h); // bit for bit
                    Assert.Equal(index, (long)(h * 251));
                }
            }
        }

        [Fact]
        public void CT121_Hash_StrictlyBetween0And1_EveryValueReached()
        {
            var seed = LatticeHash.SeedDigits(1234);
            var seen = new HashSet<int>();
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float h = LatticeHash.Hash(x, y, seed);
                    Assert.True(h > 0f && h < 1f, $"({x}, {y}) = {h}");
                    seen.Add((int)(h * 251));
                }
            }
            Assert.Equal(251, seen.Count);
            Assert.Equal(0.5f / 251f, Enumerable.Range(0, 64 * 64).Min(_i => LatticeHash.Hash(_i % 64, _i / 64, seed)), 6);
        }

        [Fact]
        public void CT121_Hash_TheQuotientMatters_NoRepeatEvery251Cells()
        {
            var seed = LatticeHash.SeedDigits(7);
            int same = 0;
            for (int x = 0; x < 251; x++)
            {
                if (LatticeHash.Hash(x, 3, seed) == LatticeHash.Hash(x + 251, 3, seed))
                {
                    same++;
                }
            }
            Assert.True(same < 10, $"{same} of 251 cells repeat one period later");
        }
    }
}
