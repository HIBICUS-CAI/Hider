using Hider.Core.Tests.Imaging;

namespace Hider.Core.Tests.Mosaic
{
    public class NoiseTests
    {
        [Fact]
        public void CT76_Noise_DeterministicPerSeedScaleLevels()
        {
            using var a = Render.Generated("noise", 200, 120);
            using var b = Render.Generated("noise", 200, 120);
            Assert.Equal(0, Golden.MaxDiff(a, b));
            using var seed = Render.Generated("noise", 200, 120, _seed: 4321);
            using var scale = Render.Generated("noise", 200, 120, new() { ["scale"] = 8 });
            using var levels = Render.Generated("noise", 200, 120, new() { ["levels"] = 3 });
            Assert.True(Golden.MaxDiff(a, seed) > 0, "seed");
            Assert.True(Golden.MaxDiff(a, scale) > 0, "scale");
            Assert.True(Golden.MaxDiff(a, levels) > 0, "levels");
        }

        [Fact]
        public void CT76_Noise_ResizeKeepsThePattern()
        {
            using var small = Render.Generated("noise", 200, 120);
            using var large = Render.Generated("noise", 320, 200);
            for (int y = 0; y < 120; y++)
            {
                for (int x = 0; x < 200; x++)
                {
                    Assert.Equal(small.GetPixel(x, y), large.GetPixel(x, y));
                }
            }
        }

        [Fact]
        public void CT77_NoiseLevels6_SixLuminanceSteps()
        {
            using var image = Render.Generated("noise", 256, 256);
            var luminances = new HashSet<int>();
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var c = image.GetPixel(x, y);
                    luminances.Add(c.Red * 299 + c.Green * 587 + c.Blue * 114);
                }
            }
            Assert.Equal(6, luminances.Count);
        }
    }
}
