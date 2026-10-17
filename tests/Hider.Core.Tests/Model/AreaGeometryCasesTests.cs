using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    /// <summary>Each AreaGeometry function on its own: exact values, every handle, the minimum and the fallbacks.</summary>
    public class AreaGeometryCasesTests
    {
        private static readonly PhysicalRect start = new(100, 200, 400, 300);

        [Theory]
        [InlineData(ResizeHandle.Left, 86, 200, 24, 300)]    // the dragged left edge moves out to the minimum
        [InlineData(ResizeHandle.Right, 100, 200, 24, 300)]  // the dragged right edge moves out
        [InlineData(ResizeHandle.Top, 100, 200, 10, 300)]    // the narrow width is not touched by a vertical handle
        [InlineData(ResizeHandle.TopLeft, 86, 200, 24, 300)]
        public void CT02_Resize_FromBelowTheMinimum_ZeroDragBringsTheDraggedSideToTheMinimum(ResizeHandle _handle, int _x, int _y, int _w, int _h)
        {
            // An area narrower than the minimum (a window scale grew since it was made) grows to the minimum as soon as one
            // of its width handles is grabbed: the result always respects the minimum on the dragged sides, and the opposite
            // edge stays where it is, as for any resize.
            var narrow = new PhysicalRect(100, 200, 10, 300);
            Assert.Equal(new PhysicalRect(_x, _y, _w, _h), AreaGeometry.Resize(narrow, _handle, 0, 0, 1.0));
        }

        [Fact]
        public void CT02_Resize_FromBelowTheMinimum_BothSides()
        {
            var tiny = new PhysicalRect(100, 200, 10, 5);
            Assert.Equal(new PhysicalRect(100, 200, 24, 24), AreaGeometry.Resize(tiny, ResizeHandle.BottomRight, 0, 0, 1.0));
            Assert.Equal(new PhysicalRect(86, 181, 24, 24), AreaGeometry.Resize(tiny, ResizeHandle.TopLeft, 3, 3, 1.0));
            Assert.Equal(new PhysicalRect(100, 200, 30, 24), AreaGeometry.Resize(tiny, ResizeHandle.BottomRight, 20, -100, 1.0));
        }

        [Theory]
        [InlineData(0, 1.0, 0.25)]
        [InlineData(60, 1.0, 60.25)]
        [InlineData(60, 2.0, 30.125)]
        [InlineData(61, 1.75, 61.25 / 1.75)]
        public void CT110_DipLength_AddsAQuarterPixelOfSlack(int _px, double _scale, double _expected)
        {
            Assert.Equal(_expected, AreaGeometry.DipLength(_px, _scale), 12);
        }

        [Theory]
        [InlineData(1.0)]
        [InlineData(2.0)]
        public void CT110_StableLength_IntegerScale_IsTheLengthItself(double _scale)
        {
            foreach (int px in new[] { 0, 1, 24, 1000, 16383 })
            {
                Assert.Equal(px, AreaGeometry.StableLength(px, _scale));
            }
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(0.0)]
        public void CT110_StableLength_MalformedScale_StopsAfterTheSearchLimit(double _scale)
        {
            // No length survives (int)(p / scale × scale) == p: the search ends after 4096 candidates instead of looping.
            Assert.Equal(1 + 4096, AreaGeometry.StableLength(1, _scale));
            Assert.Equal(500 + 4096, AreaGeometry.StableLength(500, _scale));
        }

        [Theory]
        [InlineData(1.0, 24)]
        [InlineData(1.1, 27)] // 26.4 rounded up
        [InlineData(1.25, 30)]
        [InlineData(1.5, 36)]
        [InlineData(1.75, 42)]
        [InlineData(2.0, 48)]
        [InlineData(0.5, 12)]
        public void CT02_MinSizePx_24DipRoundedUp(double _scale, int _expected)
        {
            Assert.Equal(_expected, AreaGeometry.MinSizePx(_scale));
        }

        [Fact]
        public void CT109_Resize_None_LeavesTheRectangle()
        {
            Assert.Equal(start, AreaGeometry.Resize(start, ResizeHandle.None, 50, -70, 1.0));
        }

        [Theory]
        [InlineData(ResizeHandle.Left, 10, 20, 110, 200, 390, 300)]
        [InlineData(ResizeHandle.Left, -10, 20, 90, 200, 410, 300)]
        [InlineData(ResizeHandle.Right, 10, 20, 100, 200, 410, 300)]
        [InlineData(ResizeHandle.Right, -10, 20, 100, 200, 390, 300)]
        [InlineData(ResizeHandle.Top, 10, 20, 100, 220, 400, 280)]
        [InlineData(ResizeHandle.Top, 10, -20, 100, 180, 400, 320)]
        [InlineData(ResizeHandle.Bottom, 10, 20, 100, 200, 400, 320)]
        [InlineData(ResizeHandle.Bottom, 10, -20, 100, 200, 400, 280)]
        [InlineData(ResizeHandle.TopLeft, 10, 20, 110, 220, 390, 280)]
        [InlineData(ResizeHandle.TopRight, 10, 20, 100, 220, 410, 280)]
        [InlineData(ResizeHandle.BottomLeft, 10, 20, 110, 200, 390, 320)]
        [InlineData(ResizeHandle.BottomRight, 10, 20, 100, 200, 410, 320)]
        public void CT109_Resize_OnlyTheDraggedEdgesMove(ResizeHandle _handle, int _dx, int _dy, int _x, int _y, int _w, int _h)
        {
            Assert.Equal(new PhysicalRect(_x, _y, _w, _h), AreaGeometry.Resize(start, _handle, _dx, _dy, 1.0));
        }

        [Theory]
        [InlineData(ResizeHandle.Left, 1000, 0, 476, 200, 24, 300)]
        [InlineData(ResizeHandle.Right, -1000, 0, 100, 200, 24, 300)]
        [InlineData(ResizeHandle.Top, 0, 1000, 100, 476, 400, 24)]
        [InlineData(ResizeHandle.Bottom, 0, -1000, 100, 200, 400, 24)]
        [InlineData(ResizeHandle.TopLeft, 1000, 1000, 476, 476, 24, 24)]
        [InlineData(ResizeHandle.BottomRight, -1000, -1000, 100, 200, 24, 24)]
        public void CT02_Resize_StopsAtTheMinimum_OppositeEdgeFixed(ResizeHandle _handle, int _dx, int _dy, int _x, int _y, int _w, int _h)
        {
            Assert.Equal(new PhysicalRect(_x, _y, _w, _h), AreaGeometry.Resize(start, _handle, _dx, _dy, 1.0));
        }

        [Fact]
        public void CT02_Resize_MinimumFollowsTheScale()
        {
            // 24 DIP at 150% = 36 px.
            var r = AreaGeometry.Resize(start, ResizeHandle.Right, -1000, 0, 1.5);
            Assert.Equal(new PhysicalRect(100, 200, 36, 300), r);
            var exact = AreaGeometry.Resize(start, ResizeHandle.Left, 400 - 36, 0, 1.5);
            Assert.Equal(new PhysicalRect(464, 200, 36, 300), exact);
        }

        [Fact]
        public void CT109_Resize_UnrelatedDeltaIgnored()
        {
            // A horizontal handle ignores dy, a vertical one dx.
            Assert.Equal(new PhysicalRect(100, 200, 450, 300), AreaGeometry.Resize(start, ResizeHandle.Right, 50, 999, 1.0));
            Assert.Equal(new PhysicalRect(100, 200, 400, 350), AreaGeometry.Resize(start, ResizeHandle.Bottom, 999, 50, 1.0));
        }

        [Theory]
        [InlineData(0, 0, false, 10, 10)]
        [InlineData(0, 0, true, 10, 10)]
        [InlineData(1, 1, false, 11, 11)]
        [InlineData(-1, -1, false, 9, 9)]
        [InlineData(1, -1, true, 20, 0)]
        [InlineData(-1, 1, true, 0, 20)]
        [InlineData(5, -7, false, 11, 9)] // only the sign of the direction counts
        [InlineData(-100, 100, true, 0, 20)]
        public void CT95_Nudge_SignOfTheDirection_TimesTheStep(int _dirX, int _dirY, bool _shift, int _x, int _y)
        {
            var r = new PhysicalRect(10, 10, 100, 50);
            Assert.Equal(new PhysicalRect(_x, _y, 100, 50), AreaGeometry.Nudge(r, _dirX, _dirY, _shift));
        }

        [Fact]
        public void CT02_EnsureMinSize_GrowsOnlyWhatIsTooSmall_OriginKept()
        {
            Assert.Equal(new PhysicalRect(5, 6, 24, 24), AreaGeometry.EnsureMinSize(new PhysicalRect(5, 6, 1, 0), 1.0));
            Assert.Equal(new PhysicalRect(5, 6, 300, 36), AreaGeometry.EnsureMinSize(new PhysicalRect(5, 6, 300, 10), 1.5));
            Assert.Equal(new PhysicalRect(5, 6, 48, 200), AreaGeometry.EnsureMinSize(new PhysicalRect(5, 6, 20, 200), 2.0));
            var big = new PhysicalRect(-5, -6, 300, 200);
            Assert.Equal(big, AreaGeometry.EnsureMinSize(big, 1.0));
            Assert.Equal(new PhysicalRect(0, 0, 24, 24), AreaGeometry.EnsureMinSize(new PhysicalRect(0, 0, 24, 24), 1.0));
        }

        [Fact]
        public void CT02_EnsureMinSize_NegativeSize_NormalizedFirst()
        {
            // (100, 100) with -50 x -10 covers 50..100 x 90..100: normalized, then the height grows to the minimum.
            Assert.Equal(new PhysicalRect(50, 90, 50, 24), AreaGeometry.EnsureMinSize(new PhysicalRect(100, 100, -50, -10), 1.0));
        }
    }
}
