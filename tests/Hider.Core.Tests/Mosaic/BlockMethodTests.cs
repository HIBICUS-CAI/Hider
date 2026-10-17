using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class BlockMethodTests
    {
        private static readonly BlockMethod method = (BlockMethod)MethodRegistry.Find("block")!;

        private static ParamValues Values(double? _cell = null, double? _variance = null, double? _gap = null, string _color = "#68768A", int _seed = 1234)
        {
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.BaseColor = _color;
            settings.Common.Seed = _seed;
            var stored = settings.ParamsFor("block");
            if (_cell is { } cell)
            {
                stored["cellSize"] = cell;
            }
            if (_variance is { } variance)
            {
                stored["variance"] = variance;
            }
            if (_gap is { } gap)
            {
                stored["gap"] = gap;
            }
            return ParamValues.ForGenerated(method, settings);
        }

        [Fact]
        public void Identity()
        {
            Assert.Equal("block", method.Id);
            Assert.Equal("method.block", method.LabelKey);
            Assert.Equal(MethodKind.Generated, method.Kind);
            Assert.Equal(PlatformSupport.All, method.Support);
            Assert.IsAssignableFrom<ICellMethod>(method);
            Assert.False(((IMosaicMethod)method) is ICpuRenderer);
            Assert.False(((IMosaicMethod)method) is IPreBlurMethod);
        }

        [Fact]
        public void Params_CellSizeVarianceGap()
        {
            Assert.Equal(new[]
            {
                new ParamSpec("cellSize", "param.cellSize", ParamUnit.Px, 2, 256, 2, 64, 12),
                new ParamSpec("variance", "param.variance", ParamUnit.Percent, 0, 1, 0, 1, 0.2),
                new ParamSpec("gap", "param.gap", ParamUnit.Px, 0, 64, 0, 8, 0),
            }, method.Params);
            Assert.True(method.Params[1].IsFraction);
        }

        [Fact]
        public void IsPassThrough_NeverForAGeneratedMethod()
        {
            Assert.False(method.IsPassThrough(Values()));
        }

        [Theory]
        [InlineData(null, 12)]
        [InlineData(12.0, 12)]
        [InlineData(7.4, 7)]
        [InlineData(7.6, 8)]
        [InlineData(8.5, 8)]     // Math.Round: halves to even
        [InlineData(9.5, 10)]
        [InlineData(1.0, 2)]     // clamped to Min
        [InlineData(-4.0, 2)]
        [InlineData(300.0, 256)] // clamped to Max
        [InlineData(double.NaN, 12)]
        public void CellSize_WholePixelsInTheRange(double? _cell, int _expected)
        {
            Assert.Equal(_expected, method.CellSize(Values(_cell)));
        }

        [Theory]
        [InlineData(7.6, 8)]
        [InlineData(8.5, 8)]
        [InlineData(1.0, 2)]
        public void Shader_UsesTheRoundedCellSize(double _cell, double _rounded)
        {
            using var fractional = Render.Generated("block", 50, 30, new() { ["cellSize"] = _cell, ["variance"] = 1.0 });
            using var whole = Render.Generated("block", 50, 30, new() { ["cellSize"] = _rounded, ["variance"] = 1.0 });
            Assert.Equal(0, Golden.MaxDiff(fractional, whole));
        }

        [Theory]
        [InlineData(2.4, 2)]
        [InlineData(2.5, 2)]
        [InlineData(3.5, 4)]
        [InlineData(100.0, 64)]
        [InlineData(-1.0, 0)]
        public void Shader_UsesTheRoundedGap(double _gap, double _rounded)
        {
            using var fractional = Render.Generated("block", 50, 30, new() { ["gap"] = _gap, ["variance"] = 1.0 });
            using var whole = Render.Generated("block", 50, 30, new() { ["gap"] = _rounded, ["variance"] = 1.0 });
            Assert.Equal(0, Golden.MaxDiff(fractional, whole));
        }

        [Fact]
        public void DrawJoints_GapZero_DrawsNothing()
        {
            using var bitmap = Render.Draw(30, 30, _c => method.DrawJoints(_c, new SKSizeI(30, 30), 6, Values(_gap: 0)));
            Assert.All(Pixels(bitmap), _p => Assert.Equal(0, _p.Alpha));
            using var rounded = Render.Draw(30, 30, _c => method.DrawJoints(_c, new SKSizeI(30, 30), 6, Values(_gap: 0.4)));
            Assert.All(Pixels(rounded), _p => Assert.Equal(0, _p.Alpha));
        }

        [Fact]
        public void DrawJoints_RightAndBottomOfEachCell_InTheBaseColor()
        {
            // Cell 5, gap 2, area 13 x 11: joint columns 3-4 and 8-9 (cell 3 is cut off before its joint), rows 3-4 and 8-9.
            var color = new SKColor(0x20, 0x40, 0x60);
            using var bitmap = Render.Draw(13, 11, _c => method.DrawJoints(_c, new SKSizeI(13, 11), 5, Values(_gap: 2, _color: "#204060")));
            for (int y = 0; y < 11; y++)
            {
                for (int x = 0; x < 13; x++)
                {
                    bool joint = x % 5 >= 3 || y % 5 >= 3;
                    Assert.True(joint ? bitmap.GetPixel(x, y) == color : bitmap.GetPixel(x, y).Alpha == 0, $"({x},{y}) = {bitmap.GetPixel(x, y)}");
                }
            }
        }

        [Fact]
        public void DrawJoints_LastJointReachingIntoTheArea_IsDrawn()
        {
            // Cell 5, gap 2, width 12: the third column's joint starts at 13 (not drawn), the second at 8; width 9 cuts the
            // second joint after one column, which is still drawn.
            using var bitmap = Render.Draw(9, 4, _c => method.DrawJoints(_c, new SKSizeI(9, 4), 5, Values(_gap: 2)));
            Assert.Equal(255, bitmap.GetPixel(8, 0).Alpha);
            Assert.Equal(0, bitmap.GetPixel(7, 0).Alpha);
            Assert.Equal(255, bitmap.GetPixel(3, 0).Alpha);
        }

        [Fact]
        public void DrawJoints_GapLargerThanTheCell_FillsEverything()
        {
            var color = SKColor.Parse("#68768A");
            using var bitmap = Render.Draw(20, 14, _c => method.DrawJoints(_c, new SKSizeI(20, 14), 4, Values(_gap: 9)));
            Assert.All(Pixels(bitmap), _p => Assert.Equal(color, _p));
        }

        [Theory]
        [InlineData(1.4, 1)]
        [InlineData(1.6, 2)]
        public void DrawJoints_FractionalGap_Rounded(double _gap, int _width)
        {
            using var bitmap = Render.Draw(10, 1, _c => method.DrawJoints(_c, new SKSizeI(10, 1), 5, Values(_gap: _gap)));
            int joints = Enumerable.Range(0, 5).Count(_x => bitmap.GetPixel(_x, 0).Alpha == 255);
            Assert.Equal(_width, joints);
        }

        [Fact]
        public void Cells_BaseColorShiftedByTheHash()
        {
            // Every cell is base + variance · (hash(cell) - 0.5) on all three channels (half precision: within 1).
            var p = Values(_cell: 6, _variance: 0.8, _gap: 0, _color: "#806040", _seed: 77);
            var seed = LatticeHash.SeedDigits(77);
            using var bitmap = Render.Generated("block", 48, 30, new() { ["cellSize"] = 6, ["variance"] = 0.8, ["gap"] = 0 }, 77, "#806040");
            for (int cy = 0; cy < 5; cy++)
            {
                for (int cx = 0; cx < 8; cx++)
                {
                    double shift = 0.8 * (LatticeHash.Hash(cx, cy, seed) - 0.5);
                    int Channel(int _base) => (int)Math.Round(Math.Clamp(_base / 255.0 + shift, 0, 1) * 255);
                    var c = bitmap.GetPixel(cx * 6 + 2, cy * 6 + 3);
                    Assert.InRange(c.Red, Channel(0x80) - 1, Channel(0x80) + 1);
                    Assert.InRange(c.Green, Channel(0x60) - 1, Channel(0x60) + 1);
                    Assert.InRange(c.Blue, Channel(0x40) - 1, Channel(0x40) + 1);
                    Assert.Equal(c, bitmap.GetPixel(cx * 6, cy * 6));
                    Assert.Equal(c, bitmap.GetPixel(cx * 6 + 5, cy * 6 + 5));
                }
            }
            Assert.Equal(6, method.CellSize(p));
        }

        [Theory]
        [InlineData("#FFFFFF")]
        [InlineData("#000000")]
        public void Cells_ClampedAtTheEndsOfTheRange(string _color)
        {
            using var bitmap = Render.Generated("block", 60, 60, new() { ["cellSize"] = 4, ["variance"] = 1.0 }, 5, _color);
            var colors = Pixels(bitmap).Distinct().ToList();
            Assert.True(colors.Count > 1);
            Assert.All(colors, _c => Assert.True(_c.Red == _c.Green && _c.Green == _c.Blue && _c.Alpha == 255, _c.ToString()));
            // Half the cells shift beyond the end and stay there.
            byte end = _color == "#FFFFFF" ? (byte)255 : (byte)0;
            Assert.Contains(colors, _c => _c.Red == end);
        }

        [Fact]
        public void LargestCellAndGap_OneCellOrAllJoint()
        {
            using var oneCell = Render.Generated("block", 100, 60, new() { ["cellSize"] = 256, ["variance"] = 1.0 });
            Assert.Single(Pixels(oneCell).Distinct());
            using var allJoint = Render.Generated("block", 100, 60, new() { ["cellSize"] = 2, ["gap"] = 64, ["variance"] = 1.0 });
            Assert.All(Pixels(allJoint), _p => Assert.Equal(SKColor.Parse("#68768A"), _p));
        }

        private static IEnumerable<SKColor> Pixels(SKBitmap _bitmap)
        {
            for (int y = 0; y < _bitmap.Height; y++)
            {
                for (int x = 0; x < _bitmap.Width; x++)
                {
                    yield return _bitmap.GetPixel(x, y);
                }
            }
        }
    }
}
