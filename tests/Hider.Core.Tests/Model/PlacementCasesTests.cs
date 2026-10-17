using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class PlacementCasesTests
    {
        private static readonly PhysicalRect primary = new(0, 0, 1920, 1080);
        private static readonly PhysicalRect right = new(1920, 0, 1280, 1024);

        [Fact]
        public void Fit_NoScreens_OnlyNormalizes()
        {
            Assert.Equal(new PhysicalRect(5000, 5000, 400, 240), Placement.Fit(new PhysicalRect(5000, 5000, 400, 240), Array.Empty<PhysicalRect>(), 0));
            Assert.Equal(new PhysicalRect(90, 80, 10, 20), Placement.Fit(new PhysicalRect(100, 100, -10, -20), Array.Empty<PhysicalRect>(), 3));
        }

        [Fact]
        public void Fit_NegativeSize_NormalizedBeforeTheChecks()
        {
            // (2000, 500) with -200 x 100 covers 1800..2000: it touches the primary screen and stays (normalized).
            Assert.Equal(new PhysicalRect(1800, 500, 200, 100), Placement.Fit(new PhysicalRect(2000, 500, -200, 100), new[] { primary }, 0));
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Fit_PrimaryIndexBelowRange_UsesTheFirstScreen(int _index)
        {
            var off = new PhysicalRect(9000, 9000, 400, 240);
            Assert.Equal(new PhysicalRect(760, 420, 400, 240), Placement.Fit(off, new[] { primary, right }, _index));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(99)]
        public void Fit_PrimaryIndexAboveRange_UsesTheLastScreen(int _index)
        {
            var off = new PhysicalRect(9000, 9000, 400, 240);
            Assert.Equal(new PhysicalRect(1920 + 440, 392, 400, 240), Placement.Fit(off, new[] { primary, right }, _index));
        }

        [Fact]
        public void Fit_Offscreen_OnlyTheTooLargeSideShrinks()
        {
            var wide = new PhysicalRect(9000, 9000, 3000, 100);
            Assert.Equal(new PhysicalRect(0, 490, 1920, 100), Placement.Fit(wide, new[] { primary }, 0));
            var tall = new PhysicalRect(9000, 9000, 100, 3000);
            Assert.Equal(new PhysicalRect(910, 0, 100, 1080), Placement.Fit(tall, new[] { primary }, 0));
        }

        [Fact]
        public void Fit_Offscreen_OddRemainderRoundsTowardsTheOrigin()
        {
            var off = new PhysicalRect(9000, 9000, 401, 241);
            Assert.Equal(new PhysicalRect(759, 419, 401, 241), Placement.Fit(off, new[] { primary }, 0));
        }

        [Fact]
        public void Fit_TouchingAnyScreen_StaysAlsoOnTheSecond()
        {
            var onRight = new PhysicalRect(3000, 900, 400, 300); // partly below the right screen
            Assert.Equal(onRight, Placement.Fit(onRight, new[] { primary, right }, 0));
        }

        [Fact]
        public void Fit_JustTouchingAnEdge_IsNotOnTheScreen()
        {
            // Sharing only the right edge of the primary screen is no overlap: the area is moved to the centre.
            var edge = new PhysicalRect(1920, 0, 400, 240);
            Assert.Equal(new PhysicalRect(760, 420, 400, 240), Placement.Fit(edge, new[] { primary }, 0));
        }

        [Fact]
        public void Fit_ExactlyTheBoundingBox_IsKept()
        {
            var all = new PhysicalRect(0, 0, 3200, 1080);
            Assert.Equal(all, Placement.Fit(all, new[] { primary, right }, 0));
        }

        [Theory]
        [InlineData(1.0, 400, 240)]
        [InlineData(1.25, 500, 300)]
        [InlineData(1.75, 700, 420)]
        [InlineData(1.1, 440, 264)]
        [InlineData(1.003, 401, 241)] // 401.2 and 240.72 round to the nearest pixel
        public void NewArea_400x240DipAtTheScale_Centred(double _scale, int _w, int _h)
        {
            var screen = new PhysicalRect(-1920, 100, 1920, 1080);
            var r = Placement.NewArea(screen, _scale);
            Assert.Equal(new PhysicalRect(-1920 + (1920 - _w) / 2, 100 + (1080 - _h) / 2, _w, _h), r);
        }

        [Fact]
        public void NewArea_SmallScreen_ClampedToTheScreen()
        {
            var screen = new PhysicalRect(10, 20, 300, 200);
            Assert.Equal(screen, Placement.NewArea(screen, 1.0));
            var narrow = new PhysicalRect(0, 0, 300, 1000);
            Assert.Equal(new PhysicalRect(0, 380, 300, 240), Placement.NewArea(narrow, 1.0));
        }
    }
}
