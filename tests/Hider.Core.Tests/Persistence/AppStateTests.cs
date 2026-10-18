using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public class AppStateTests
    {
        [Fact]
        public void CT136_AppState_Defaults()
        {
            Assert.Equal(1, AppState.CurrentVersion);
            var state = new AppState();
            Assert.Equal(AppState.CurrentVersion, state.Version);
            Assert.Empty(state.Areas);
        }

        [Fact]
        public void CT136_AreaState_Defaults()
        {
            var a = new AreaState();
            Assert.Equal("", a.Id);
            Assert.Equal((0, 0), (a.X, a.Y));
            Assert.Equal((AreaDefaults.NewAreaWidthDip, AreaDefaults.NewAreaHeightDip), (a.Width, a.Height));
            Assert.Equal(1.0, a.Opacity);
            Assert.Equal(PatternKind.Mosaic, a.Pattern);
            Assert.Equal(AreaDefaults.FillColor, a.Fill.Color);
            Assert.Equal(AreaDefaults.MosaicMethod, a.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, a.Generated.Method);
        }

        [Fact]
        public void CT136_FromArea_PositionInPixels_SizeInDip_SettingsShared()
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(-300, 150, 600, 360), false);
            area.Opacity = 0.4;
            area.Pattern = PatternKind.Fill;
            var s = AreaState.FromArea(area, 1.5);
            Assert.Equal("0f3b8d2e", s.Id);
            Assert.Equal((-300, 150), (s.X, s.Y));
            Assert.Equal((400.0, 240.0), (s.Width, s.Height));
            Assert.Equal(0.4, s.Opacity);
            Assert.Equal(PatternKind.Fill, s.Pattern);
            Assert.Same(area.Fill, s.Fill);
            Assert.Same(area.Mosaic, s.Mosaic);
            Assert.Same(area.Generated, s.Generated);
        }

        [Fact]
        public void CT136_FromArea_SizeRoundedToTwoDecimals()
        {
            var area = new Area { Id = "00000001", Rect = new PhysicalRect(0, 0, 1001, 100) };
            var s = AreaState.FromArea(area, 1.5);
            Assert.Equal(667.33, s.Width); // 667.333…
            Assert.Equal(66.67, s.Height); // 66.666…
            var at175 = AreaState.FromArea(new Area { Id = "00000001", Rect = new PhysicalRect(0, 0, 61, 1) }, 1.75);
            Assert.Equal(34.86, at175.Width); // 34.857…
            Assert.Equal(0.57, at175.Height);
        }

        [Fact]
        public void CT136_FromArea_StoresTheRawOpacity_NotThePatternRange()
        {
            // Opacity 0.05 on a fill area draws at 0.10 but keeps 0.05, so switching to mosaic gets it back.
            var area = new Area { Id = "00000001", Pattern = PatternKind.Fill, Opacity = 0.05 };
            Assert.Equal(0.05, AreaState.FromArea(area, 1.0).Opacity);
        }

        [Fact]
        public void CT136_ToArea_SizeTimesScale_Rounded()
        {
            var s = new AreaState { Id = "0f3b8d2e", X = 12, Y = -34, Width = 667.33, Height = 66.67, Opacity = 0.25, Pattern = PatternKind.Generated };
            var a = s.ToArea(1.5);
            Assert.Equal("0f3b8d2e", a.Id);
            Assert.Equal(new PhysicalRect(12, -34, 1001, 100), a.Rect);
            Assert.Equal(0.25, a.Opacity);
            Assert.Equal(PatternKind.Generated, a.Pattern);
            Assert.Same(s.Fill, a.Fill);
            Assert.Same(s.Mosaic, a.Mosaic);
            Assert.Same(s.Generated, a.Generated);
        }

        [Fact]
        public void CT136_ToArea_HalfPixelRoundsToEven()
        {
            // Math.Round's default: 100.5 -> 100, 101.5 -> 102.
            var a = new AreaState { Id = "00000001", Width = 100.5, Height = 101.5 }.ToArea(1.0);
            Assert.Equal((100, 102), (a.Rect.Width, a.Rect.Height));
        }

        [Fact]
        public void CT136_ToArea_OpacityGoesThroughTheAreaClamp()
        {
            Assert.Equal(1.0, new AreaState { Id = "00000001", Opacity = double.NaN }.ToArea(1.0).Opacity);
            Assert.Equal(0.0, new AreaState { Id = "00000001", Opacity = -3 }.ToArea(1.0).Opacity);
            Assert.Equal(1.0, new AreaState { Id = "00000001", Opacity = 3 }.ToArea(1.0).Opacity);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.25)]
        [InlineData(1.5)]
        [InlineData(1.75)]
        [InlineData(2.0)]
        [InlineData(3.0)]
        public void CT136_FromAreaToArea_RoundTripsEveryPixelSize(double _scale)
        {
            for (int px = 1; px <= 4000; px += 7)
            {
                var area = new Area { Id = "00000001", Rect = new PhysicalRect(px, -px, px, px + 1) };
                var back = AreaState.FromArea(area, _scale).ToArea(_scale);
                Assert.Equal(area.Rect, back.Rect);
            }
        }
    }
}
