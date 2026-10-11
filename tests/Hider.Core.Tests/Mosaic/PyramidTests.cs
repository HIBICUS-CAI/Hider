using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class PyramidTests
    {
        [Fact]
        public void CT50_BuildPyramid_HalvesAndAverages()
        {
            // 4x4 with distinct 2x2 blocks: each level-1 pixel must be the average of its block.
            var info = new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            var values = new byte[,] { { 0, 40, 100, 100 }, { 80, 160, 100, 100 }, { 10, 20, 255, 255 }, { 30, 40, 255, 255 } };
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 4; x++)
                {
                    bitmap.SetPixel(x, y, new SKColor(values[y, x], values[y, x], values[y, x], 255));
                }
            }
            using var source = SKImage.FromBitmap(bitmap);
            var pyramid = MosaicPipeline.BuildPyramid(source, 10);
            try
            {
                Assert.Equal(3, pyramid.Count); // 4x4, 2x2, 1x1
                Assert.Equal(new SKSizeI(2, 2), new SKSizeI(pyramid[1].Width, pyramid[1].Height));
                Assert.Equal(new SKSizeI(1, 1), new SKSizeI(pyramid[2].Width, pyramid[2].Height));
                using var level1 = SKBitmap.FromImage(pyramid[1]);
                Assert.InRange(level1.GetPixel(0, 0).Red, 69, 71);   // (0+40+80+160)/4 = 70
                Assert.InRange(level1.GetPixel(1, 0).Red, 99, 101);  // 100
                Assert.InRange(level1.GetPixel(0, 1).Red, 24, 26);   // 25
                Assert.InRange(level1.GetPixel(1, 1).Red, 254, 255); // 255
                Assert.Equal(2, MosaicPipeline.BuildPyramid(source, 2).Count);
            }
            finally
            {
                foreach (var image in pyramid.Skip(1))
                {
                    image.Dispose();
                }
            }
        }
    }
}
