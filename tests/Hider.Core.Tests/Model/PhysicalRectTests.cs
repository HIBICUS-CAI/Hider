using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class PhysicalRectTests
    {
        [Fact]
        public void RightAndBottom()
        {
            var r = new PhysicalRect(-10, 20, 30, 40);
            Assert.Equal(20, r.Right);
            Assert.Equal(60, r.Bottom);
            Assert.Equal((-10, 20, 30, 40), (r.X, r.Y, r.Width, r.Height));
        }

        [Theory]
        [InlineData(1, 1, false)]
        [InlineData(0, 1, true)]
        [InlineData(1, 0, true)]
        [InlineData(-1, 5, true)]
        [InlineData(5, -1, true)]
        [InlineData(0, 0, true)]
        public void IsEmpty_WhenWidthOrHeightIsNotPositive(int _w, int _h, bool _empty)
        {
            Assert.Equal(_empty, new PhysicalRect(3, 4, _w, _h).IsEmpty);
        }

        [Fact]
        public void Default_IsEmptyAtTheOrigin()
        {
            PhysicalRect r = default;
            Assert.True(r.IsEmpty);
            Assert.Equal(new PhysicalRect(0, 0, 0, 0), r);
        }

        [Theory]
        [InlineData(10, 20, 30, 50)]
        [InlineData(30, 20, 10, 50)]
        [InlineData(10, 50, 30, 20)]
        [InlineData(30, 50, 10, 20)]
        public void FromEdges_AnyCornerOrder(int _l, int _t, int _r, int _b)
        {
            Assert.Equal(new PhysicalRect(10, 20, 20, 30), PhysicalRect.FromEdges(_l, _t, _r, _b));
        }

        [Fact]
        public void FromEdges_SameEdges_IsEmpty()
        {
            Assert.Equal(new PhysicalRect(5, 7, 0, 0), PhysicalRect.FromEdges(5, 7, 5, 7));
        }

        [Fact]
        public void Normalized_PositiveIsUnchanged_NegativeFlipsTheOrigin()
        {
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), new PhysicalRect(1, 2, 3, 4).Normalized());
            Assert.Equal(new PhysicalRect(-2, 2, 3, 4), new PhysicalRect(1, 2, -3, 4).Normalized());
            Assert.Equal(new PhysicalRect(1, -2, 3, 4), new PhysicalRect(1, 2, 3, -4).Normalized());
        }

        [Fact]
        public void Intersects_Overlap()
        {
            var a = new PhysicalRect(0, 0, 100, 100);
            Assert.True(a.Intersects(new PhysicalRect(50, 50, 100, 100)));
            Assert.True(a.Intersects(new PhysicalRect(99, 99, 1, 1)));
            Assert.True(a.Intersects(new PhysicalRect(-10, -10, 11, 11)));
            Assert.True(a.Intersects(new PhysicalRect(10, 10, 10, 10))); // inside
            Assert.True(new PhysicalRect(10, 10, 10, 10).Intersects(a)); // around
        }

        [Fact]
        public void Intersects_TouchingEdgesOrApart_IsFalse()
        {
            var a = new PhysicalRect(0, 0, 100, 100);
            Assert.False(a.Intersects(new PhysicalRect(100, 0, 10, 10))); // right edge
            Assert.False(a.Intersects(new PhysicalRect(-10, 0, 10, 10))); // left edge
            Assert.False(a.Intersects(new PhysicalRect(0, 100, 10, 10))); // bottom edge
            Assert.False(a.Intersects(new PhysicalRect(0, -10, 10, 10))); // top edge
            Assert.False(a.Intersects(new PhysicalRect(500, 500, 10, 10)));
            Assert.False(a.Intersects(new PhysicalRect(50, 500, 10, 10))); // overlapping in x only
        }

        [Fact]
        public void Intersects_EmptyNeverIntersects()
        {
            var a = new PhysicalRect(0, 0, 100, 100);
            Assert.False(a.Intersects(new PhysicalRect(50, 50, 0, 10)));
            Assert.False(new PhysicalRect(50, 50, 10, 0).Intersects(a));
            Assert.False(a.Intersects(new PhysicalRect(50, 50, -10, -10)));
        }

        [Fact]
        public void Intersect_Overlap_IsTheCommonPart()
        {
            var a = new PhysicalRect(0, 0, 100, 100);
            Assert.Equal(new PhysicalRect(50, 60, 50, 40), a.Intersect(new PhysicalRect(50, 60, 100, 100)));
            Assert.Equal(new PhysicalRect(10, 10, 5, 5), a.Intersect(new PhysicalRect(10, 10, 5, 5)));
            Assert.Equal(a, a.Intersect(new PhysicalRect(-50, -50, 500, 500)));
        }

        [Fact]
        public void Intersect_NoOverlap_IsDefault()
        {
            var a = new PhysicalRect(0, 0, 100, 100);
            Assert.Equal(default, a.Intersect(new PhysicalRect(100, 0, 10, 10))); // touching in x
            Assert.Equal(default, a.Intersect(new PhysicalRect(0, 100, 10, 10))); // touching in y
            Assert.Equal(default, a.Intersect(new PhysicalRect(200, 50, 10, 10))); // apart in x only
            Assert.Equal(default, a.Intersect(new PhysicalRect(50, 200, 10, 10))); // apart in y only
        }

        [Fact]
        public void Union_SmallestRectangleContainingBoth()
        {
            var a = new PhysicalRect(0, 0, 10, 10);
            Assert.Equal(new PhysicalRect(-5, 0, 35, 40), a.Union(new PhysicalRect(-5, 20, 35, 20)));
            Assert.Equal(a, a.Union(new PhysicalRect(2, 2, 3, 3)));
            Assert.Equal(new PhysicalRect(-2560, -200, 4480, 1440), new PhysicalRect(0, 0, 1920, 1080).Union(new PhysicalRect(-2560, -200, 2560, 1440)));
        }

        [Fact]
        public void Offset_MovesWithoutResizing()
        {
            var r = new PhysicalRect(1, 2, 3, 4);
            Assert.Equal(new PhysicalRect(11, -18, 3, 4), r.Offset(10, -20));
            Assert.Equal(r, r.Offset(0, 0));
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), r); // a value: the original is unchanged
        }

        [Fact]
        public void Equality_ByValue()
        {
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), new PhysicalRect(1, 2, 3, 4));
            Assert.NotEqual(new PhysicalRect(1, 2, 3, 4), new PhysicalRect(1, 2, 3, 5));
            Assert.True(new PhysicalRect(1, 2, 3, 4) == new PhysicalRect(1, 2, 3, 4));
            Assert.Equal(new PhysicalRect(1, 2, 3, 4).GetHashCode(), new PhysicalRect(1, 2, 3, 4).GetHashCode());
        }
    }
}
