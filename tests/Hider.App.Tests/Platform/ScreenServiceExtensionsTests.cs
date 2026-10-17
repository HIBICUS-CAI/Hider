using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Platform
{
    public class ScreenServiceExtensionsTests
    {
        private static readonly ScreenInfo a = new(new PhysicalRect(0, 0, 1920, 1080), new PhysicalRect(0, 0, 1920, 1040), 1.0, false);
        private static readonly ScreenInfo b = new(new PhysicalRect(1920, 0, 2560, 1440), new PhysicalRect(1920, 0, 2560, 1400), 1.25, true);
        private static readonly ScreenInfo c = new(new PhysicalRect(-1280, 0, 1280, 1024), new PhysicalRect(-1280, 0, 1280, 1024), 1.0, false);

        private static FakeScreenService Service(params ScreenInfo[] _screens)
        {
            return new FakeScreenService { Screens = _screens };
        }

        [Fact]
        public void Primary_IsTheScreenMarkedPrimary_WhereverItIsInTheList()
        {
            Assert.Same(b, Service(a, b, c).Primary());
        }

        [Fact]
        public void Primary_FirstMarkedScreenWins()
        {
            var alsoPrimary = c with { IsPrimary = true };

            Assert.Same(b, Service(a, b, alsoPrimary).Primary());
        }

        [Fact]
        public void Primary_NoneMarked_IsTheFirstScreen()
        {
            Assert.Same(a, Service(a, c).Primary());
        }

        [Fact]
        public void Primary_NoScreens_IsNull()
        {
            Assert.Null(Service().Primary());
        }

        [Theory]
        [InlineData(0, 0, 0)]          // top-left pixel of the first screen
        [InlineData(1919, 1079, 0)]    // its last pixel
        [InlineData(1920, 0, 1)]       // first pixel of the second screen (right edges are exclusive)
        [InlineData(4479, 1439, 1)]
        [InlineData(-1, 500, 2)]       // left of the origin
        public void ScreenAt_IsTheScreenHoldingThePoint(int _x, int _y, int _index)
        {
            var screens = new[] { a, b, c };

            Assert.Same(screens[_index], Service(screens).ScreenAt(_x, _y));
        }

        [Theory]
        [InlineData(0, 1080)]          // below the first screen
        [InlineData(5000, 0)]          // right of every screen
        [InlineData(-1281, 0)]         // left of every screen
        public void ScreenAt_OutsideEveryScreen_IsThePrimary(int _x, int _y)
        {
            Assert.Same(b, Service(a, b, c).ScreenAt(_x, _y));
        }

        [Fact]
        public void ScreenAt_NoScreens_IsNull()
        {
            Assert.Null(Service().ScreenAt(0, 0));
        }

        [Fact]
        public void PrimaryIndex_IsTheIndexOfThePrimary()
        {
            Assert.Equal(1, Service(a, b, c).PrimaryIndex());
            Assert.Equal(0, Service(b, a).PrimaryIndex());
        }

        [Fact]
        public void PrimaryIndex_NoneMarkedOrNoScreens_IsZero()
        {
            Assert.Equal(0, Service(a, c).PrimaryIndex());
            Assert.Equal(0, Service().PrimaryIndex());
        }

        [Fact]
        public void ScreenInfo_IsAValueRecord()
        {
            var copy = new ScreenInfo(a.Bounds, a.WorkingArea, a.Scale, a.IsPrimary);

            Assert.Equal(a, copy);
            Assert.NotEqual(a, a with { Scale = 2.0 });
            Assert.NotEqual(a, a with { IsPrimary = true });
            Assert.Equal(new PhysicalRect(0, 0, 1920, 1040), a.WorkingArea);
        }

        [AvaloniaTheory]
        [InlineData(1.0, 300, 300.0)]
        [InlineData(2.0, 300, 150.0)]
        [InlineData(1.5, 100, 66.666666666666671)]
        [InlineData(1.25, 0, 0.0)]
        public void PxToDip_DividesByTheHostScale(double _scale, int _px, double _dip)
        {
            var window = new Window();
            var host = new FakeAreaWindowHost(window, _scale);

            Assert.Equal(_dip, host.PxToDip(_px), 12);
            window.Close();
        }
    }
}
