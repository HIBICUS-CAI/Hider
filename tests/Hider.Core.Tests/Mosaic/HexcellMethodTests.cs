using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class HexcellMethodTests
    {
        private static readonly HexcellMethod method = (HexcellMethod)MethodRegistry.Find("hexcell")!;

        private static ParamValues Values(double? _radius, double _opacity = 1.0)
        {
            var settings = new MethodSettings<MosaicCommon>();
            if (_radius is { } radius)
            {
                settings.ParamsFor("hexcell")["radius"] = radius;
            }
            return ParamValues.ForMosaic(method, settings, _opacity);
        }

        private static SKBitmap DrawShader(SKShader _shader, int _w, int _h)
        {
            using (_shader)
            {
                using var paint = new SKPaint { Shader = _shader, BlendMode = SKBlendMode.Src };
                return Render.Draw(_w, _h, _c => _c.DrawRect(0, 0, _w, _h, paint));
            }
        }

        [Fact]
        public void CT114_Identity()
        {
            Assert.Equal("hexcell", method.Id);
            Assert.Equal("method.hexcell", method.LabelKey);
            Assert.Equal(MethodKind.Mosaic, method.Kind);
            Assert.Equal(PlatformSupport.All, method.Support);
            Assert.IsAssignableFrom<ICpuRenderer>(method);
            Assert.False(((IMosaicMethod)method) is ICellMethod);
            Assert.False(((IMosaicMethod)method) is IPreBlurMethod);
        }

        [Fact]
        public void CT04_Params_RadiusFadingToOne()
        {
            var spec = Assert.Single(method.Params);
            Assert.Equal(new ParamSpec("radius", "param.radius", ParamUnit.Px, 2, 256, 2, 64, 10, LerpWithOpacity: true, LerpFloor: 1), spec);
        }

        [Theory]
        [InlineData(10)]
        [InlineData(2)]
        [InlineData(3.7)]
        [InlineData(37)]
        public void CT72_CellCentre_IsTheNearestLatticePoint(double _radius)
        {
            // Every point belongs to the hexagon of the nearest centre r·(√3 (i + j / 2), 1.5 j). All three branches of the
            // cube rounding are reached on this grid.
            for (double y = -3 * _radius; y < 3 * _radius; y += _radius / 7.3)
            {
                for (double x = -3 * _radius; x < 3 * _radius; x += _radius / 6.1)
                {
                    var (cx, cy) = HexcellMethod.CellCentre(x, y, _radius);
                    double best = double.MaxValue;
                    for (int j = -5; j <= 5; j++)
                    {
                        for (int i = -6; i <= 6; i++)
                        {
                            double px = _radius * Math.Sqrt(3) * (i + j / 2.0), py = _radius * 1.5 * j;
                            best = Math.Min(best, Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y)));
                        }
                    }
                    double found = Math.Sqrt((cx - x) * (cx - x) + (cy - y) * (cy - y));
                    Assert.True(found <= best + 1e-9, $"({x}, {y}): centre ({cx}, {cy}) at {found}, nearest at {best}");
                    // The centre is a lattice point.
                    double j0 = cy / (1.5 * _radius), i0 = cx / (_radius * Math.Sqrt(3)) - j0 / 2;
                    Assert.Equal(Math.Round(j0), j0, 9);
                    Assert.Equal(Math.Round(i0), i0, 9);
                }
            }
        }

        [Theory]
        [InlineData(10, 0, 0, 0, 0)]
        [InlineData(10, 8.6, 0, 0, 0)]         // just inside the first hexagon's right side (half width 8.66)
        [InlineData(10, 8.7, 0, 17.320508, 0)] // just past it
        [InlineData(10, 0, 9.9, 0, 0)]         // the top corner is 10 above the centre
        [InlineData(10, 0.1, 10.1, 8.660254, 15)]   // below the corner: the lower right neighbour
        [InlineData(10, -0.1, 10.1, -8.660254, 15)] // or the lower left one
        public void CT72_CellCentre_EdgesAndCorners(double _radius, double _x, double _y, double _cx, double _cy)
        {
            var (cx, cy) = HexcellMethod.CellCentre(_x, _y, _radius);
            Assert.Equal(_cx, cx, 5);
            Assert.Equal(_cy, cy, 5);
        }

        [Theory]
        [InlineData(10.0, 0.0, true)]    // effective 1
        [InlineData(2.0, 0.0, true)]
        [InlineData(2.0, 1.0, false)]    // smallest radius
        [InlineData(2.0, 0.01, false)]   // effective 1.01
        [InlineData(256.0, 1.0, false)]
        [InlineData(null, 1.0, false)]
        public void CT73_IsPassThrough_AtEffectiveRadiusOne(double? _radius, double _opacity, bool _expected)
        {
            Assert.Equal(_expected, method.IsPassThrough(Values(_radius, _opacity)));
        }

        [Fact]
        public void CT119_TryRender_DeclinesThePassThrough()
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(16, 16) };
            using var bitmap = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pixmap = bitmap.PeekPixels();
            Assert.False(method.TryRender(pixmap, inputs, Values(10, 0.0)));
            // Without the spec an unclamped radius below 1 is a pass-through too.
            var tiny = new ParamValues(Array.Empty<ParamSpec>(), new Dictionary<string, double> { ["radius"] = 0.5 }, 1.0);
            Assert.False(method.TryRender(pixmap, inputs, tiny));
        }

        [Fact]
        public void CT119_TryRender_DeclinesASourceWithoutCpuPixels()
        {
            // Effective radius 1.1 reads level 0 (the source); a picture-backed image has no pixels, so the shader is used.
            using var recorder = new SKPictureRecorder();
            recorder.BeginRecording(SKRect.Create(16, 16)).Clear(SKColors.Green);
            using var picture = recorder.EndRecording();
            using var lazy = SKImage.FromPicture(picture, new SKSizeI(16, 16));
            var inputs = new MosaicInputs { Source = lazy, Pyramid = new[] { lazy }, AreaSize = new SKSizeI(16, 16) };
            using var bitmap = new SKBitmap(16, 16, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var pixmap = bitmap.PeekPixels();
            var p = Values(2, 0.1);
            Assert.Equal(1.1, p.Effective("radius"), 9);
            Assert.False(method.TryRender(pixmap, inputs, p));
            // The pipeline then draws the shader: the green capture comes through.
            using var drawn = Render.Draw(16, 16, _c => new MosaicPipeline().Draw(_c, SKRect.Create(16, 16), inputs, method, p, 256));
            Assert.Equal(SKColors.Green, drawn.GetPixel(8, 8));
        }

        [Theory]
        [InlineData(2.0, 1.0)]   // 2: level 1
        [InlineData(3.0, 1.0)]   // level 2
        [InlineData(10.0, 1.0)]  // level 4
        [InlineData(256.0, 1.0)] // level 8 (the last of 9)
        [InlineData(64.0, 0.5)]  // 32.5: level 5
        public void CT119_CpuPath_MatchesTheShader(double _radius, double _opacity)
        {
            var p = Values(_radius, _opacity);
            using var source = SyntheticImage.CreateImage(211, 143);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                MethodConsistencyTests.AssertClose(method, inputs, p, 0.005);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(2.0, 0.1)]    // 1.1: width 1.9, level 0; centres at y = 1.65 j are whole every 20th row of hexagons
        [InlineData(2.0, 0.15)]   // 1.15: whole every 40th row
        [InlineData(10.0, 0.01)]  // 1.09
        [InlineData(2.0, 0.05)]   // 1.05: centres at y = 1.575 j, whole every 40th row
        public void CT119_LevelZero_CpuPathIsTheShaderExactly_TexelEdgesIncluded(double _radius, double _opacity)
        {
            var p = Values(_radius, _opacity);
            using var source = SyntheticImage.CreateImage(211, 143);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                AssertLevelZero(inputs, p);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(10.0)]
        [InlineData(7.3)]
        [InlineData(256.0)]
        public void CT119_ShortPyramid_BothPathsSampleTheSourceNearest(double _radius)
        {
            // Radius 10 wants level 4; with only the source in the pyramid both paths sample it (nearest).
            using var source = SyntheticImage.CreateImage(97, 61);
            var p = Values(_radius);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(97, 61) };
            AssertLevelZero(inputs, p);
            using var plain = Render.ToBitmap(source);
            using var drawn = DrawShader(method.CreateShader(inputs, p), 97, 61);
            // The hexagon at the origin has its centre clamped to (0.5, 0.5): the first source pixel.
            Assert.Equal(plain.GetPixel(0, 0), drawn.GetPixel(0, 0));
            Assert.Equal(plain.GetPixel(0, 0), drawn.GetPixel(1, 1));
        }

        /// <summary>
        /// Level 0 (nearest sampling of the source): the CPU path equals the shader on every pixel. Hexagon centres often lie
        /// exactly on a texel edge (y = 1.5 r j is whole for many radii); there Skia's raster takes the texel before the edge,
        /// and so does <see cref="RasterSampler"/>. Each pixel is also one of the source's pixels, the one under a hexagon centre
        /// near the pixel (independently of the float arithmetic: within one radius plus a texel).
        /// </summary>
        private static void AssertLevelZero(MosaicInputs _inputs, ParamValues _p)
        {
            int w = _inputs.AreaSize.Width, h = _inputs.AreaSize.Height;
            using var cpu = Render.Draw(w, h, _c => new MosaicPipeline().Draw(_c, SKRect.Create(w, h), _inputs, method, _p, 256));
            using var reference = DrawShader(method.CreateShader(_inputs, _p), w, h);
            Assert.Equal(0, Golden.MaxDiff(reference, cpu));
            using var plain = Render.ToBitmap(_inputs.Source!);
            double radius = _p.Effective("radius");
            int reach = (int)Math.Ceiling(radius) + 1;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var (cx, cy) = HexcellMethod.CellCentre(x + 0.5, y + 0.5, radius);
                    int tx = Math.Clamp((int)Math.Floor(cx), 0, w - 1), ty = Math.Clamp((int)Math.Floor(cy), 0, h - 1);
                    bool found = false;
                    for (int dy = -1; dy <= 1 && !found; dy++)
                    {
                        for (int dx = -1; dx <= 1 && !found; dx++)
                        {
                            int sx = Math.Clamp(tx + dx, 0, w - 1), sy = Math.Clamp(ty + dy, 0, h - 1);
                            found = Math.Abs(sx - x) <= reach && Math.Abs(sy - y) <= reach && plain.GetPixel(sx, sy) == cpu.GetPixel(x, y);
                        }
                    }
                    Assert.True(found, $"({x},{y}): {cpu.GetPixel(x, y)} is not the source near the centre ({cx}, {cy})");
                }
            }
        }

        [Theory]
        [InlineData(10, 7, 12, 2.0)]   // level 1
        [InlineData(10, 7, 4, 10.0)]   // level 3 of the 10 x 7 pyramid
        [InlineData(37, 23, 64, 2.0)]
        [InlineData(1, 1, 12, 2.0)]
        [InlineData(1, 9, 4, 10.0)]
        public void CT119_TryRender_PaddedRows_WritesEachRowAndLeavesThePadding(int _w, int _h, int _padding, double _radius)
        {
            // ICpuRenderer takes any pixmap: rows longer than w * 4 are written row by row, the padding is left alone.
            using var source = SyntheticImage.CreateImage(_w, _h);
            var p = Values(_radius);
            var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
            try
            {
                PaddedTarget.AssertSameAsTight(method, inputs, p, _w, _h, _padding);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT73_RadiusBelowOneWithoutSpec_ShaderIsTheIdentity()
        {
            // Level(): a hexagon narrower than a pixel reads level 0; the shader's own radius test returns the source.
            using var source = SyntheticImage.CreateImage(40, 30);
            var tiny = new ParamValues(Array.Empty<ParamSpec>(), new Dictionary<string, double> { ["radius"] = 0.5 }, 1.0);
            var inputs = MosaicPipeline.Prepare(source, default, tiny);
            try
            {
                using var drawn = DrawShader(method.CreateEffectShader(inputs, tiny), 40, 30);
                using var plain = Render.ToBitmap(source);
                Assert.Equal(0, Golden.MaxDiff(plain, drawn));
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(2.0, 0.1)]   // 1.1 (level 0)
        [InlineData(2.0, 0.15)]  // 1.15 (level 0)
        [InlineData(2.0, 1.0)]   // level 1
        [InlineData(3.7, 1.0)]
        [InlineData(10.0, 1.0)]
        [InlineData(12.0, 1.0)]
        [InlineData(37.0, 1.0)]
        public void CT72_OneColorPerHexagon(double _radius, double _opacity)
        {
            // The hexagons come from CellCentre (double, nearest lattice point, checked on its own above), independent of the
            // float arithmetic of the shader and the CPU path. Only pixels whose centre lies within 0.01 px of a hexagon's
            // edge may fall to the neighbour through float rounding; those are left out.
            using var bitmap = Render.Mosaic("hexcell", _opacity, _params: new() { ["radius"] = _radius });
            double radius = 1 + (_radius - 1) * _opacity;
            var colorOfCell = new Dictionary<(double, double), SKColor>();
            int checkedPixels = 0;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var centre = HexcellMethod.CellCentre(x + 0.5, y + 0.5, radius);
                    if (NearAnEdge(x + 0.5, y + 0.5, radius, centre))
                    {
                        continue;
                    }
                    checkedPixels++;
                    var key = (Math.Round(centre.X, 3), Math.Round(centre.Y, 3));
                    var c = bitmap.GetPixel(x, y);
                    if (colorOfCell.TryGetValue(key, out var seen))
                    {
                        Assert.True(seen == c, $"({x},{y}) in the hexagon at {key}: {c} vs {seen}");
                    }
                    else
                    {
                        colorOfCell[key] = c;
                    }
                }
            }
            Assert.True(checkedPixels > bitmap.Width * bitmap.Height * 0.9, $"{checkedPixels} pixels checked");
            // About one hexagon per 2.6 r² px; the area is 320 x 180.
            Assert.InRange(colorOfCell.Count, 320 * 180 / (2.6 * radius * radius) * 0.8, 320 * 180 / (2.6 * radius * radius) * 1.3 + 40);
        }

        /// <summary>True when a point's distance to its own centre and to another lattice point's differ by less than 0.01 px.</summary>
        private static bool NearAnEdge(double _x, double _y, double _radius, (double X, double Y) _centre)
        {
            double own = Math.Sqrt((_x - _centre.X) * (_x - _centre.X) + (_y - _centre.Y) * (_y - _centre.Y));
            double w = _radius * Math.Sqrt(3);
            (double, double)[] neighbours = { (w, 0), (-w, 0), (w / 2, 1.5 * _radius), (-w / 2, 1.5 * _radius), (w / 2, -1.5 * _radius), (-w / 2, -1.5 * _radius) };
            foreach (var (dx, dy) in neighbours)
            {
                double nx = _centre.X + dx, ny = _centre.Y + dy;
                if (Math.Sqrt((_x - nx) * (_x - nx) + (_y - ny) * (_y - ny)) - own < 0.01)
                {
                    return true;
                }
            }
            return false;
        }

        [Fact]
        public void CT120_AboveTheMaximum_SameAsTheMaximum()
        {
            using var max = Render.Mosaic("hexcell", _params: new() { ["radius"] = 256 });
            using var above = Render.Mosaic("hexcell", _params: new() { ["radius"] = 5000 });
            using var below = Render.Mosaic("hexcell", _params: new() { ["radius"] = 1 });
            using var min = Render.Mosaic("hexcell", _params: new() { ["radius"] = 2 });
            Assert.Equal(0, Golden.MaxDiff(max, above));
            Assert.Equal(0, Golden.MaxDiff(min, below));
        }
    }
}
