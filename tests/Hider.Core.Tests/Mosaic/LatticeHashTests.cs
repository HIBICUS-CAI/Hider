using Hider.Core.Mosaic;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class LatticeHashTests
    {
        [Fact]
        public void Permute_IsAPermutationOf0To250_ComputedExactly()
        {
            var seen = new HashSet<float>();
            for (int x = 0; x < 251; x++)
            {
                float p = LatticeHash.Permute(x);
                Assert.Equal((long)x * x * x % 251, (long)p);
                seen.Add(p);
            }
            Assert.Equal(251, seen.Count);
            // Every value the hash feeds to the mod: sums up to 2 · 250 and products up to 250².
            for (int x = 0; x <= 250 * 250; x++)
            {
                Assert.Equal(x % 251, (int)LatticeHash.Mod(x));
            }
        }

        [Fact]
        public void EverySeed_GivesItsOwnPattern()
        {
            var patterns = new HashSet<string>();
            for (int seed = 0; seed < 65536; seed++)
            {
                var digits = LatticeHash.SeedDigits(seed);
                var cells = new char[12];
                for (int i = 0; i < cells.Length; i++)
                {
                    cells[i] = (char)(LatticeHash.Hash(i % 4, i / 4, digits) * 251);
                }
                patterns.Add(new string(cells));
            }
            Assert.Equal(65536, patterns.Count);
            Assert.Equal(LatticeHash.SeedDigits(5), LatticeHash.SeedDigits(65536 + 5));
        }

        [Theory]
        [InlineData(0, 0, 1234)]
        [InlineData(62800, 260, 65535)] // beyond one period of 251 and the largest seed (third digit 1)
        public void Shader_GivesExactlyTheCSharpValues(int _offsetX, int _offsetY, int _seed)
        {
            // The block shader's own hash() with a main that writes the step index (0..250) as an exact byte.
            string source = ShaderSource.Load("block");
            source = source[..source.IndexOf("half4 main", StringComparison.Ordinal)] + """
                uniform float2 offset;
                half4 main(float2 p) {
                    float n = floor(hash(floor(p) + offset) * 251.0);
                    return half4(half(mod(n, 256.0) / 255.0), half(floor(n / 256.0) / 255.0), 0, 1);
                }
                """;
            using var effect = SKRuntimeEffect.CreateShader(source, out string? errors);
            Assert.True(effect is not null, errors);
            var digits = LatticeHash.SeedDigits(_seed);
            using var uniforms = new SKRuntimeEffectUniforms(effect)
            {
                ["cellSize"] = 1f, ["variance"] = 0f, ["gap"] = 0f, ["areaSize"] = new[] { 300f, 300f }, ["areaOrigin"] = new[] { 0f, 0f },
                ["baseColor"] = new[] { 0f, 0f, 0f }, ["seed"] = new[] { digits.Low, digits.Middle, digits.High }, ["offset"] = new[] { (float)_offsetX, _offsetY },
            };
            using var shader = effect.ToShader(uniforms);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            using var image = Render.Draw(300, 300, _c => _c.DrawRect(0, 0, 300, 300, paint));
            for (int y = 0; y < 300; y++)
            {
                for (int x = 0; x < 300; x++)
                {
                    var c = image.GetPixel(x, y);
                    int expected = (int)(LatticeHash.Hash(x + _offsetX, y + _offsetY, digits) * 251);
                    Assert.True(c.Red + c.Green * 256 == expected, $"cell ({x + _offsetX}, {y + _offsetY}): shader {c.Red + c.Green * 256}, C# {expected}");
                }
            }
        }
    }
}
