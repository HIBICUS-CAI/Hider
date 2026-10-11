using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class PlacementTests
    {
        private static readonly PhysicalRect primaryScreen = new(0, 0, 1920, 1080);
        private static readonly PhysicalRect leftScreen = new(-2560, -200, 2560, 1440);

        [Theory]
        [InlineData(1.0, true, 400, 240, 100)]
        [InlineData(1.5, false, 600, 360, 300000)]
        [InlineData(2.0, true, 800, 480, 100)]
        public void CT10_NewArea_Defaults(double _scale, bool _isWindows, int _w, int _h, int _interval)
        {
            var screen = new PhysicalRect(0, 0, 3840, 2160);
            var rect = Placement.NewArea(screen, _scale);
            var area = Area.CreateNew("0f3b8d2e", rect, _isWindows);

            Assert.Equal(_w, rect.Width);
            Assert.Equal(_h, rect.Height);
            Assert.Equal((3840 - _w) / 2, rect.X);
            Assert.Equal((2160 - _h) / 2, rect.Y);
            Assert.Equal(PatternKind.Mosaic, area.Pattern);
            Assert.Equal("pixelate", area.Mosaic.Method);
            Assert.Equal(_interval, area.Mosaic.Common.IntervalMs);
            Assert.Equal(1.0, area.Opacity);
        }

        [Fact]
        public void CT32_Fit_IntersectingRect_Unchanged()
        {
            var rect = new PhysicalRect(1800, 1000, 400, 300); // partly off the primary screen
            Assert.Equal(rect, Placement.Fit(rect, new[] { primaryScreen }, 0));
        }

        [Fact]
        public void CT33_Fit_NoIntersection_MovesToPrimaryCentre()
        {
            var rect = new PhysicalRect(5000, 5000, 400, 240);
            Assert.Equal(new PhysicalRect(760, 420, 400, 240), Placement.Fit(rect, new[] { leftScreen, primaryScreen }, 1));
        }

        [Fact]
        public void CT34_Fit_TooLarge_ShrinksToPrimary()
        {
            var rect = new PhysicalRect(9000, 0, 4000, 3000);
            Assert.Equal(primaryScreen, Placement.Fit(rect, new[] { primaryScreen }, 0));
        }

        [Fact]
        public void CT35_Fit_NegativeCoordinatesOnLeftMonitor()
        {
            var onLeft = new PhysicalRect(-1000, 100, 400, 240);
            Assert.Equal(onLeft, Placement.Fit(onLeft, new[] { leftScreen, primaryScreen }, 1));
            var beyondLeft = new PhysicalRect(-3000, 100, 400, 240);
            Assert.Equal(new PhysicalRect(760, 420, 400, 240), Placement.Fit(beyondLeft, new[] { leftScreen, primaryScreen }, 1));
        }

        [Fact]
        public void Fit_LargerThanAllScreens_CutToTheirBoundingBox()
        {
            // A hand-edited 40000 px area touches a screen but would need multi-gigabyte pixel buffers.
            var huge = new PhysicalRect(-5000, 100, 40000, 40000);
            Assert.Equal(new PhysicalRect(-2560, 100, 4480, 1140), Placement.Fit(huge, new[] { leftScreen, primaryScreen }, 1));
            var wide = new PhysicalRect(100, 100, 5000, 200);
            Assert.Equal(new PhysicalRect(100, 100, 1820, 200), Placement.Fit(wide, new[] { primaryScreen }, 0));
        }
    }
}
