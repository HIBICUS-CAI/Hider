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
        public void StableLength_SurvivesTheWin32RoundTrip_AtEveryWindowsScale()
        {
            // Windows scales are dpi / 96 (96..480 dpi, custom scales included). Avalonia on Win32: WM_SIZE stores the client
            // width as px / scale, and Resize sends (int)(width × scale) back.
            int worst = 0;
            for (int dpi = 96; dpi <= 480; dpi++)
            {
                double scale = dpi / 96.0;
                for (int px = 1; px < 16384; px++)
                {
                    int p = AreaGeometry.StableLength(px, scale);
                    Assert.True(p >= px, $"{dpi} dpi: {px} -> {p}");
                    double stored = p / scale;
                    Assert.True((int)(stored * scale) == p, $"{dpi} dpi: {p} is not stable");
                    worst = Math.Max(worst, p - px);
                }
            }
            Assert.InRange(worst, 0, 37);
            Assert.Equal(62, AreaGeometry.StableLength(61, 168 / 96.0)); // 175%: never 60
            Assert.Equal(1516, AreaGeometry.StableLength(1479, 263 / 96.0)); // the longest run of unstable lengths
        }
    }
}
