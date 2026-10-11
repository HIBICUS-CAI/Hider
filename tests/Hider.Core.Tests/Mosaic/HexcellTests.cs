using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;

namespace Hider.Core.Tests.Mosaic
{
    public class HexcellTests
    {
        [Fact]
        public void CT71_Hexcell10_MatchesGolden()
        {
            using var actual = Render.Mosaic("hexcell");
            Golden.AssertMatches("CT71_hexcell_radius10", actual, 1);
        }

        [Theory]
        [InlineData(0, 0, 0, 0)]
        [InlineData(3, -2, 0, 0)]
        [InlineData(17.0, 1, 17.320508, 0)]         // one hexagon to the right (width r·√3)
        [InlineData(9.0, 15.5, 8.660254, 15)]       // next row: shifted by half a width, 1.5 r down
        [InlineData(-8.0, 14.0, -8.660254, 15)]
        [InlineData(26.5, 30.5, 34.641016, 30)]      // nearest centre: (34.64, 30) at 8.15 vs (17.32, 30) at 9.19
        public void CT72_HexLattice_CellCentres(double _x, double _y, double _cx, double _cy)
        {
            var (cx, cy) = HexcellMethod.CellCentre(_x, _y, 10);
            Assert.Equal(_cx, cx, 4);
            Assert.Equal(_cy, cy, 4);
        }

        [Fact]
        public void CT73_HexcellOpacityZero_IsIdentity()
        {
            using var source = SyntheticImage.CreateImage();
            using var expected = Render.ToBitmap(source);
            using var actual = Render.Mosaic("hexcell", _opacity: 0.0, _source: source);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void CT74_HexcellWithLevels_OnlyQuantisedValues()
        {
            using var actual = Render.Mosaic("hexcell", _levels: 3);
            var allowed = new HashSet<byte> { 0, 128, 255 };
            for (int y = 0; y < actual.Height; y += 2)
            {
                for (int x = 0; x < actual.Width; x += 2)
                {
                    var c = actual.GetPixel(x, y);
                    Assert.True(allowed.Contains(c.Red) && allowed.Contains(c.Green) && allowed.Contains(c.Blue), $"({x},{y}) = {c}");
                }
            }
        }
    }
}
