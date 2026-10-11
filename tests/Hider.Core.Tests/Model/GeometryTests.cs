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
    }
}
