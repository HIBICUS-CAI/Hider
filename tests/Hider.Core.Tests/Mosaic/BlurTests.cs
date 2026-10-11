using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;

namespace Hider.Core.Tests.Mosaic
{
    public class BlurTests
    {
        [Fact]
        public void CT75_BlurMethod_IsThePreBlurOnly()
        {
            // Radius 8 with no common blur equals the pre-blur stage alone (CT-48's golden): no cells, no other processing.
            using var actual = Render.Mosaic("blur");
            Golden.AssertMatches("CT48_blur_r8", actual, 2);
        }

        [Fact]
        public void CT75_BlurOpacityZero_IsIdentity()
        {
            using var source = SyntheticImage.CreateImage();
            using var expected = Render.ToBitmap(source);
            using var actual = Render.Mosaic("blur", _opacity: 0.0, _source: source);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void CT75_BlurCombinesWithCommonBlur()
        {
            // Gaussian radii combine as sqrt(a² + b²): common 4.8 with method 6.4 is one blur of 8.
            using var combined = Render.Mosaic("blur", _blur: 4.8, _params: new() { ["radius"] = 6.4 });
            Golden.AssertMatches("CT48_blur_r8", combined, 2);
        }
    }
}
