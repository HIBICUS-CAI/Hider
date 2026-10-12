using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class GeometryTests
    {
        [Theory]
        [InlineData(1.0)]
        [InlineData(1.5)]
        [InlineData(2.0)]
        public void CT02_Resize_NeverBelowMinimumDip(double _scale)
        {
            var start = new PhysicalRect(100, 100, 400, 300);
            foreach (var handle in new[] { ResizeHandle.Left, ResizeHandle.Top, ResizeHandle.Right, ResizeHandle.Bottom,
                         ResizeHandle.TopLeft, ResizeHandle.TopRight, ResizeHandle.BottomLeft, ResizeHandle.BottomRight })
            {
                foreach (int d in new[] { -5000, 5000 })
                {
                    var r = AreaGeometry.Resize(start, handle, d, d, _scale);
                    Assert.True(r.Width / _scale >= 24 - 1e-9, $"{handle} {d}: width {r.Width} at {_scale}");
                    Assert.True(r.Height / _scale >= 24 - 1e-9, $"{handle} {d}: height {r.Height} at {_scale}");
                }
            }
        }

        [Fact]
        public void CT95_Nudge_OneOrTenPixels()
        {
            var r = new PhysicalRect(10, 10, 100, 100);
            Assert.Equal(new PhysicalRect(11, 10, 100, 100), AreaGeometry.Nudge(r, 1, 0, false));
            Assert.Equal(new PhysicalRect(10, 0, 100, 100), AreaGeometry.Nudge(r, 0, -1, true));
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(1.1)]
        [InlineData(1.25)]
        [InlineData(1.3)]
        [InlineData(1.5)]
        [InlineData(1.75)]
        [InlineData(2.25)]
        [InlineData(3.5)]
        public void DipLength_TruncatedBackToTheSamePixels(double _scale)
        {
            // The platform sizes the window as (int)(DIP × scale): every pixel count must come back exactly (175%: 61 and
            // 115 used to lose a column).
            for (int px = 1; px < 8000; px++)
            {
                Assert.Equal(px, (int)(AreaGeometry.DipLength(px, _scale) * _scale));
            }
        }

        [Fact]
        public void MostOverlapping_PicksTheMonitorHoldingMostOfTheRect()
        {
            var left = new PhysicalRect(0, 0, 1920, 1080);
            var right = new PhysicalRect(1920, 0, 2560, 1440);
            var screens = new[] { left, right };
            Assert.Equal(1, AreaGeometry.MostOverlapping(new PhysicalRect(1900, 100, 400, 200), screens)); // top-left on the left one
            Assert.Equal(0, AreaGeometry.MostOverlapping(new PhysicalRect(1700, 100, 400, 200), screens));
            Assert.Equal(1, AreaGeometry.MostOverlapping(new PhysicalRect(2000, 1200, 10, 10), screens)); // below the left one's bottom
            Assert.Equal(0, AreaGeometry.MostOverlapping(new PhysicalRect(9000, 9000, 10, 10), screens)); // nowhere: the first
            Assert.Equal(-1, AreaGeometry.MostOverlapping(new PhysicalRect(0, 0, 10, 10), Array.Empty<PhysicalRect>()));
        }
    }
}
