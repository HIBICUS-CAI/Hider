using System.Collections;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>Every member and branch of <see cref="MosaicPipeline"/> on the CPU raster.</summary>
    public class MosaicPipelineTests
    {
        /// <summary>A mosaic method that only passes the capture through, with an optional pre-blur radius and cell size.</summary>
        private sealed class Probe : IMosaicMethod, IPreBlurMethod, ICellMethod
        {
            private readonly DummyMethod shader = new("probe", MethodKind.Mosaic, PlatformSupport.All);

            public double Radius { get; init; }
            public int Cell { get; init; } = 1;
            public string Id => shader.Id;
            public string LabelKey => shader.LabelKey;
            public MethodKind Kind => shader.Kind;
            public PlatformSupport Support => shader.Support;
            public IReadOnlyList<ParamSpec> Params => shader.Params;

            public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
            {
                return _inputs.Source!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
            }

            public double PreBlurRadius(ParamValues _p)
            {
                return Radius;
            }

            public int CellSize(ParamValues _p)
            {
                return Cell;
            }
        }

        /// <summary>
        /// A mosaic method with a CPU path that records the pixmap it is handed and fills it with one color. The shader
        /// path passes the capture through.
        /// </summary>
        private sealed class CpuProbe : IMosaicMethod, ICpuRenderer
        {
            private readonly DummyMethod shader = new("cpuprobe", MethodKind.Mosaic, PlatformSupport.All);

            public SKColor Color { get; init; } = new(100, 150, 200);
            public List<(int Width, int Height, int RowBytes, SKColorType ColorType, SKAlphaType AlphaType)> Targets { get; } = new();
            public string Id => shader.Id;
            public string LabelKey => shader.LabelKey;
            public MethodKind Kind => shader.Kind;
            public PlatformSupport Support => shader.Support;
            public IReadOnlyList<ParamSpec> Params => shader.Params;

            public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
            {
                return _inputs.Source!.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest));
            }

            public bool TryRender(SKPixmap _dst, MosaicInputs _inputs, ParamValues _p)
            {
                Targets.Add((_dst.Width, _dst.Height, _dst.RowBytes, _dst.ColorType, _dst.AlphaType));
                return _dst.Erase(Color);
            }
        }

        private static bool IsDisposed(SKImage _image)
        {
            return _image.Handle == IntPtr.Zero;
        }

        private static ParamValues MosaicValues(double _blur, double _opacity = 1.0)
        {
            var settings = new MethodSettings<MosaicCommon>();
            settings.Common.Blur = _blur;
            return ParamValues.ForMosaic(MethodRegistry.Find("pixelate")!, settings, _opacity);
        }

        [Fact]
        public void CT124_Constants()
        {
            Assert.Equal(0.5f, MosaicPipeline.SigmaPerRadius);
            Assert.Equal(9, MosaicPipeline.MaxPyramidLevels);
        }

        [Fact]
        public void CT124_Prepare_NoBlur_TheCaptureItselfWithALazyPyramid()
        {
            using var source = SyntheticImage.CreateImage(40, 30);
            var inputs = MosaicPipeline.Prepare(source, new SKPointI(-7, 9), MosaicValues(0));
            Assert.Same(source, inputs.Source);
            Assert.Equal(new SKSizeI(40, 30), inputs.AreaSize);
            Assert.Equal(new SKPointI(-7, 9), inputs.AreaOrigin);
            // The pyramid is read on demand: [0] is the (here unblurred) source.
            Assert.Equal(7, inputs.Pyramid.Count); // 40x30, 20x15, 10x8, 5x4, 3x2, 2x1, 1x1
            Assert.Same(source, inputs.Pyramid[0]);
            Assert.Equal(new SKSizeI(1, 1), new SKSizeI(inputs.Pyramid[6].Width, inputs.Pyramid[6].Height));
            MosaicPipeline.Release(inputs, source);
            Assert.False(IsDisposed(source));
        }

        [Fact]
        public void CT124_Prepare_DefaultArguments_NoMethodMeansNoOwnRadius()
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(0));
            Assert.Same(source, inputs.Source);
            Assert.Equal(default, inputs.AreaOrigin);
            MosaicPipeline.Release(inputs, source);
        }

        [Fact]
        public void CT124_Prepare_CommonBlur_ANewBlurredImage()
        {
            using var source = SyntheticImage.CreateImage(40, 30);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(4));
            try
            {
                Assert.NotSame(source, inputs.Source);
                Assert.Equal((40, 30), (inputs.Source!.Width, inputs.Source.Height));
                Assert.Same(inputs.Source, inputs.Pyramid[0]); // the pyramid is built from the blurred image
                using var plain = Render.ToBitmap(source);
                using var blurred = Render.ToBitmap(inputs.Source);
                Assert.True(Golden.MaxDiff(plain, blurred) > 10);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT05_Prepare_BlurFadesWithTheOpacity()
        {
            // Effective blur lerp(a, 0, 8): at opacity 0 nothing is blurred; at 0.5 it is the blur of radius 4.
            using var source = SyntheticImage.CreateImage(40, 30);
            var none = MosaicPipeline.Prepare(source, default, MosaicValues(8, 0.0));
            Assert.Same(source, none.Source);
            var half = MosaicPipeline.Prepare(source, default, MosaicValues(8, 0.5));
            var four = MosaicPipeline.Prepare(source, default, MosaicValues(4, 1.0));
            try
            {
                using var a = Render.ToBitmap(half.Source!);
                using var b = Render.ToBitmap(four.Source!);
                Assert.Equal(0, Golden.MaxDiff(a, b));
            }
            finally
            {
                MosaicPipeline.Release(none, source);
                MosaicPipeline.Release(half, source);
                MosaicPipeline.Release(four, source);
            }
        }

        [Theory]
        [InlineData(0.01, false)] // radius must be above 0.01 px
        [InlineData(0.011, true)]
        public void CT124_Prepare_RadiusThreshold(double _blur, bool _blurred)
        {
            using var source = SyntheticImage.CreateImage(8, 8);
            var p = new ParamValues(Array.Empty<ParamSpec>(), new Dictionary<string, double> { [CommonParams.Blur] = _blur }, 1.0);
            var inputs = MosaicPipeline.Prepare(source, default, p);
            Assert.Equal(_blurred, !ReferenceEquals(source, inputs.Source));
            MosaicPipeline.Release(inputs, source);
        }

        [Fact]
        public void CT124_Prepare_OwnRadiusCombinesAsGaussians()
        {
            // sqrt(3² + 4²) = 5: common 3 with own 4 equals common 5 alone.
            using var source = SyntheticImage.CreateImage(40, 30);
            var combined = MosaicPipeline.Prepare(source, default, MosaicValues(3), null, new Probe { Radius = 4 });
            var single = MosaicPipeline.Prepare(source, default, MosaicValues(5));
            var ownOnly = MosaicPipeline.Prepare(source, default, MosaicValues(0), null, new Probe { Radius = 5 });
            try
            {
                using var a = Render.ToBitmap(combined.Source!);
                using var b = Render.ToBitmap(single.Source!);
                using var c = Render.ToBitmap(ownOnly.Source!);
                Assert.Equal(0, Golden.MaxDiff(a, b));
                Assert.Equal(0, Golden.MaxDiff(c, b));
            }
            finally
            {
                MosaicPipeline.Release(combined, source);
                MosaicPipeline.Release(single, source);
                MosaicPipeline.Release(ownOnly, source);
            }
        }

        [Fact]
        public void CT124_Prepare_NegativeOwnRadius_CountsAsZero()
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(0), null, new Probe { Radius = -5 });
            Assert.Same(source, inputs.Source);
            var withCommon = MosaicPipeline.Prepare(source, default, MosaicValues(3), null, new Probe { Radius = -5 });
            var common = MosaicPipeline.Prepare(source, default, MosaicValues(3));
            try
            {
                using var a = Render.ToBitmap(withCommon.Source!);
                using var b = Render.ToBitmap(common.Source!);
                Assert.Equal(0, Golden.MaxDiff(a, b));
            }
            finally
            {
                MosaicPipeline.Release(withCommon, source);
                MosaicPipeline.Release(common, source);
            }
        }

        [Fact]
        public void CT124_Prepare_BlurClampsAtTheEdges()
        {
            // A uniform image stays uniform when blurred: the edges repeat instead of fading to transparent.
            using var source = Solid(20, 10, new SKColor(10, 200, 30));
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(16));
            try
            {
                using var blurred = Render.ToBitmap(inputs.Source!);
                Assert.Equal(new SKColor(10, 200, 30), blurred.GetPixel(0, 0));
                Assert.Equal(new SKColor(10, 200, 30), blurred.GetPixel(19, 9));
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT125_Release_FreesTheBlurredImageAndTheBuiltPyramid_NeverTheCapture()
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(4));
            var blurred = inputs.Source!;
            var levels = inputs.Pyramid.ToList(); // builds it
            Assert.Equal(5, levels.Count);
            MosaicPipeline.Release(inputs, source);
            Assert.False(IsDisposed(source));
            Assert.True(IsDisposed(blurred));
            Assert.All(levels, _l => Assert.True(IsDisposed(_l)));
        }

        [Fact]
        public void CT125_Release_UnbuiltPyramid_OnlyTheSourceIsFreed()
        {
            using var source = SyntheticImage.CreateImage(16, 16);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(4));
            var blurred = inputs.Source!;
            MosaicPipeline.Release(inputs, source);
            Assert.True(IsDisposed(blurred));
            Assert.False(IsDisposed(source));
        }

        [Fact]
        public void CT125_Release_PlainInputs_FreesEachImageOnce_NoSourceIsFine()
        {
            using var original = SyntheticImage.CreateImage(8, 8);
            var other = SyntheticImage.CreateImage(4, 4);
            // The same image twice (Source and Pyramid[1]) is disposed once; the original is kept.
            MosaicPipeline.Release(new MosaicInputs { Source = other, Pyramid = new[] { original, other } }, original);
            Assert.True(IsDisposed(other));
            Assert.False(IsDisposed(original));

            var level = SyntheticImage.CreateImage(4, 4);
            MosaicPipeline.Release(new MosaicInputs { Source = null, Pyramid = new[] { level } }, original);
            Assert.True(IsDisposed(level));
            MosaicPipeline.Release(new MosaicInputs(), original); // generated inputs: nothing to free
        }

        [Fact]
        public void CT50_LazyPyramid_ListMembers()
        {
            using var source = SyntheticImage.CreateImage(8, 4);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(0));
            try
            {
                var generic = new List<SKImage>();
                foreach (var image in inputs.Pyramid)
                {
                    generic.Add(image);
                }
                var plain = new List<object>();
                var enumerator = ((IEnumerable)inputs.Pyramid).GetEnumerator();
                while (enumerator.MoveNext())
                {
                    plain.Add(enumerator.Current);
                }
                Assert.Equal(4, inputs.Pyramid.Count); // 8x4, 4x2, 2x1, 1x1
                Assert.Equal(generic, plain.Cast<SKImage>());
                Assert.Equal(Enumerable.Range(0, 4).Select(_i => inputs.Pyramid[_i]), generic);
                Assert.Same(inputs.Pyramid[1], inputs.Pyramid[1]); // built once
                Assert.Throws<ArgumentOutOfRangeException>(() => inputs.Pyramid[4]);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT50_Prepare_PyramidStopsAtMaxPyramidLevels()
        {
            using var source = SyntheticImage.CreateImage(1024, 2);
            var inputs = MosaicPipeline.Prepare(source, default, MosaicValues(0));
            try
            {
                Assert.Equal(MosaicPipeline.MaxPyramidLevels, inputs.Pyramid.Count);
                Assert.Equal(4, inputs.Pyramid[^1].Width); // 1024 / 2^8
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Theory]
        [InlineData(5, 3, 10, new[] { 5, 3, 3, 2, 2, 1, 1, 1 })]
        [InlineData(8, 1, 10, new[] { 8, 1, 4, 1, 2, 1, 1, 1 })]
        [InlineData(1, 6, 10, new[] { 1, 6, 1, 3, 1, 2, 1, 1 })]
        [InlineData(1, 1, 10, new[] { 1, 1 })]
        [InlineData(16, 16, 1, new[] { 16, 16 })]
        [InlineData(16, 16, 0, new[] { 16, 16 })]
        [InlineData(16, 16, 3, new[] { 16, 16, 8, 8, 4, 4 })]
        public void CT50_BuildPyramid_SizesRoundUp_StopsAt1x1OrTheLimit(int _w, int _h, int _max, int[] _sizes)
        {
            using var source = SyntheticImage.CreateImage(_w, _h);
            var pyramid = MosaicPipeline.BuildPyramid(source, _max);
            try
            {
                Assert.Same(source, pyramid[0]);
                Assert.Equal(_sizes, pyramid.SelectMany(_l => new[] { _l.Width, _l.Height }));
            }
            finally
            {
                foreach (var level in pyramid.Skip(1))
                {
                    level.Dispose();
                }
            }
        }

        [Fact]
        public void CT50_BuildPyramid_OddSize_RepeatsTheLastColumn()
        {
            // 3x1 [0, 100, 200]: level 1 is [avg(0, 100), avg(200, 200)] = [50, 200].
            var bitmap = new SKBitmap(new SKImageInfo(3, 1, SKColorType.Bgra8888, SKAlphaType.Premul));
            bitmap.SetPixel(0, 0, new SKColor(0, 0, 0));
            bitmap.SetPixel(1, 0, new SKColor(100, 100, 100));
            bitmap.SetPixel(2, 0, new SKColor(200, 200, 200));
            using (bitmap)
            {
                using var source = SKImage.FromBitmap(bitmap);
                var pyramid = MosaicPipeline.BuildPyramid(source, 2, null);
                using var level1 = Render.ToBitmap(pyramid[1]);
                pyramid[1].Dispose();
                Assert.InRange(level1.GetPixel(0, 0).Red, 49, 51);
                Assert.Equal(200, level1.GetPixel(1, 0).Red);
            }
        }

        [Fact]
        public void CT50_BuildPyramid_TwoArgumentOverload_SameAsWithoutContext()
        {
            using var source = SyntheticImage.CreateImage(30, 20);
            var a = MosaicPipeline.BuildPyramid(source, 9);
            var b = MosaicPipeline.BuildPyramid(source, 9, null);
            try
            {
                Assert.Equal(a.Count, b.Count);
                for (int i = 0; i < a.Count; i++)
                {
                    using var x = Render.ToBitmap(a[i]);
                    using var y = Render.ToBitmap(b[i]);
                    Assert.Equal(0, Golden.MaxDiff(x, y));
                }
            }
            finally
            {
                foreach (var level in a.Skip(1).Concat(b.Skip(1)))
                {
                    level.Dispose();
                }
            }
        }

        [Theory]
        [InlineData(256)]
        [InlineData(257)]
        [InlineData(int.MaxValue)]
        public void CT47_LevelFilter_256AndAbove_IsNoFilter(int _levels)
        {
            Assert.Null(new MosaicPipeline().LevelFilter(_levels));
        }

        [Fact]
        public void CT46_LevelFilter_CachedPerLevelCount_BelowTwoIsTwo()
        {
            var pipeline = new MosaicPipeline();
            var two = pipeline.LevelFilter(2);
            Assert.NotNull(two);
            Assert.Same(two, pipeline.LevelFilter(2));
            Assert.Same(two, pipeline.LevelFilter(1));
            Assert.Same(two, pipeline.LevelFilter(-5));
            Assert.NotSame(two, pipeline.LevelFilter(3));
            Assert.NotSame(two, new MosaicPipeline().LevelFilter(2)); // per pipeline
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(7)]
        [InlineData(16)]
        [InlineData(255)]
        public void CT46_LevelFilter_FloorCTimesLOverLMinusOne_AlphaUnchanged(int _levels)
        {
            // Every channel value 0..255 through the filter: q = min(L - 1, floor(c · L / 256)), out = round(q · 255 / (L - 1)).
            var pipeline = new MosaicPipeline();
            var info = new SKImageInfo(256, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
            var px = new byte[256 * 4];
            for (int i = 0; i < 256; i++)
            {
                px[i * 4] = (byte)i;
                px[i * 4 + 1] = (byte)(255 - i);
                px[i * 4 + 2] = (byte)i;
                px[i * 4 + 3] = 255;
            }
            using var image = SKImage.FromPixelCopy(info, px, info.RowBytes);
            using var paint = new SKPaint { ColorFilter = pipeline.LevelFilter(_levels), BlendMode = SKBlendMode.Src };
            using var result = Render.Draw(256, 1, _c => _c.DrawImage(image, 0, 0, paint));
            byte Expected(int _c) => (byte)Math.Round(Math.Min(_levels - 1, _c * _levels / 256) * 255.0 / (_levels - 1));
            var allowed = new HashSet<byte>();
            for (int i = 0; i < 256; i++)
            {
                var c = result.GetPixel(i, 0);
                Assert.Equal(new SKColor(Expected(i), Expected(255 - i), Expected(i), 255), c);
                allowed.Add(c.Red);
            }
            Assert.Equal(_levels, allowed.Count);
            Assert.Contains((byte)0, allowed);
            Assert.Contains((byte)255, allowed);
        }

        [Fact]
        public void CT126_Draw_IntoAnOffsetScaledRect_ClippedToIt()
        {
            // The area's pixels are scaled to the destination; nothing is drawn outside it.
            using var source = SyntheticImage.CreateImage(20, 10);
            var method = new Probe();
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(20, 10) };
            using var result = Render.Draw(60, 40, _c => new MosaicPipeline().Draw(_c, SKRect.Create(10, 5, 40, 20), inputs, method, p, 256));
            using var expected = Render.Draw(60, 40, _c => _c.DrawImage(source, SKRect.Create(10, 5, 40, 20), new SKSamplingOptions(SKFilterMode.Nearest)));
            Assert.Equal(0, Golden.MaxDiff(expected, result));
            Assert.Equal(0, result.GetPixel(9, 10).Alpha);
            Assert.Equal(0, result.GetPixel(50, 10).Alpha);
            Assert.Equal(0, result.GetPixel(20, 25).Alpha);
            Assert.Equal(255, result.GetPixel(10, 5).Alpha);
            Assert.Equal(255, result.GetPixel(49, 24).Alpha);
        }

        [Fact]
        public void CT126_Draw_CellPath_IntoAnOffsetRect_ClippedToIt()
        {
            // 20 x 10 in cells of 3: the upscaled cells cover 21 x 12 area pixels, one column and two rows more than the
            // area. Drawn at (10, 5) scaled by 2 they would reach x = 52 and y = 29; the clip keeps them inside (10, 5, 40, 20).
            using var source = SyntheticImage.CreateImage(20, 10);
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(20, 10) };
            using var result = Render.Draw(60, 40, _c => new MosaicPipeline().Draw(_c, SKRect.Create(10, 5, 40, 20), inputs, new Probe { Cell = 3 }, p, 256));
            using var plain = Render.ToBitmap(source);
            for (int y = 0; y < 40; y++)
            {
                for (int x = 0; x < 60; x++)
                {
                    bool inside = x >= 10 && x < 50 && y >= 5 && y < 25;
                    if (inside)
                    {
                        int ax = (x - 10) / 2, ay = (y - 5) / 2;
                        Assert.Equal(plain.GetPixel(ax / 3 * 3, ay / 3 * 3), result.GetPixel(x, y));
                    }
                    else
                    {
                        Assert.True(result.GetPixel(x, y).Alpha == 0, $"({x},{y}) outside the destination was drawn");
                    }
                }
            }
            Assert.Equal(0, result.GetPixel(50, 10).Alpha); // the 21st cell column
            Assert.Equal(0, result.GetPixel(20, 25).Alpha); // the 11th and 12th cell rows
            Assert.Equal(0, result.GetPixel(9, 10).Alpha);
            Assert.Equal(255, result.GetPixel(10, 5).Alpha);
            Assert.Equal(255, result.GetPixel(49, 24).Alpha);
        }

        [Fact]
        public void CT126_Draw_CpuPath_IntoAnOffsetScaledRect_ClippedToIt()
        {
            var method = new CpuProbe();
            using var source = SyntheticImage.CreateImage(20, 10);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(20, 10) };
            using var result = Render.Draw(60, 40, _c =>
            {
                _c.Clear(SKColors.Red);
                new MosaicPipeline().Draw(_c, SKRect.Create(10, 5, 40, 20), inputs, method, MosaicValues(0), 256);
            });
            for (int y = 0; y < 40; y++)
            {
                for (int x = 0; x < 60; x++)
                {
                    bool inside = x >= 10 && x < 50 && y >= 5 && y < 25;
                    Assert.Equal(inside ? method.Color : SKColors.Red, result.GetPixel(x, y));
                }
            }
        }

        [Theory]
        [InlineData(20, 10)]
        [InlineData(1, 1)]
        [InlineData(37, 3)]
        [InlineData(0, 0)] // counts as 1 x 1
        public void CT119_Draw_CpuPath_HandsOverATightAreaSizedBgraPixmap(int _w, int _h)
        {
            // The pipeline's pixmap is tight (RowBytes == w * 4), area-sized, premultiplied BGRA.
            var method = new CpuProbe();
            var inputs = new MosaicInputs { AreaSize = new SKSizeI(_w, _h) };
            using var result = Render.Draw(8, 8, _c => new MosaicPipeline().Draw(_c, SKRect.Create(8, 8), inputs, method, MosaicValues(0), 256));
            int w = Math.Max(1, _w), h = Math.Max(1, _h);
            Assert.Equal((w, h, w * 4, SKColorType.Bgra8888, SKAlphaType.Premul), Assert.Single(method.Targets));
            Assert.Equal(method.Color, result.GetPixel(7, 7));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(0)]
        [InlineData(-3)]
        public void CT119_Draw_CellSizeBelowTwo_EvaluatesEveryPixel(int _cell)
        {
            using var source = SyntheticImage.CreateImage(20, 10);
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(20, 10) };
            using var result = Render.Draw(20, 10, _c => new MosaicPipeline().Draw(_c, SKRect.Create(20, 10), inputs, new Probe { Cell = _cell }, p, 256));
            using var expected = Render.ToBitmap(source);
            Assert.Equal(0, Golden.MaxDiff(expected, result));
        }

        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        [InlineData(20)]
        [InlineData(64)]
        public void CT119_Draw_CellPath_EachCellIsItsTopLeftPixel(int _cell)
        {
            // The reduced surface evaluates the shader at the centre of each cell's top-left pixel; nearest upscaling
            // fills the cell (partial cells at the right and bottom included).
            using var source = SyntheticImage.CreateImage(45, 23);
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(45, 23) };
            using var result = Render.Draw(45, 23, _c => new MosaicPipeline().Draw(_c, SKRect.Create(45, 23), inputs, new Probe { Cell = _cell }, p, 256));
            using var plain = Render.ToBitmap(source);
            for (int y = 0; y < 23; y++)
            {
                for (int x = 0; x < 45; x++)
                {
                    Assert.Equal(plain.GetPixel(x / _cell * _cell, y / _cell * _cell), result.GetPixel(x, y));
                }
            }
        }

        [Fact]
        public void CT119_Draw_CellPath_ScaledDestination()
        {
            using var source = SyntheticImage.CreateImage(12, 6);
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(12, 6) };
            using var result = Render.Draw(24, 12, _c => new MosaicPipeline().Draw(_c, SKRect.Create(24, 12), inputs, new Probe { Cell = 3 }, p, 256));
            using var plain = Render.ToBitmap(source);
            for (int y = 0; y < 12; y++)
            {
                for (int x = 0; x < 24; x++)
                {
                    Assert.Equal(plain.GetPixel(x / 2 / 3 * 3, y / 2 / 3 * 3), result.GetPixel(x, y));
                }
            }
        }

        [Fact]
        public void CT59_Draw_EmptyAreaSize_CountsAsOnePixel()
        {
            // A generated method with a 0 x 0 area still fills the destination (one area pixel scaled up).
            var method = MethodRegistry.Find("block")!;
            var p = ParamValues.ForGenerated(method, new MethodSettings<GeneratedCommon>());
            var inputs = new MosaicInputs { AreaSize = new SKSizeI(0, 0) };
            using var result = Render.Draw(4, 4, _c => new MosaicPipeline().Draw(_c, SKRect.Create(4, 4), inputs, method, p, 256));
            Assert.Equal(255, result.GetPixel(0, 0).Alpha);
            Assert.Equal(result.GetPixel(0, 0), result.GetPixel(3, 3));
        }

        [Fact]
        public void CT46_Draw_LevelsApplyToTheShaderPathAndTheCellPath()
        {
            using var source = SyntheticImage.CreateImage(32, 16);
            var p = MosaicValues(0);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(32, 16) };
            var allowed = new HashSet<byte> { 0, 255 };
            foreach (int cell in new[] { 1, 4 })
            {
                using var result = Render.Draw(32, 16, _c => new MosaicPipeline().Draw(_c, SKRect.Create(32, 16), inputs, new Probe { Cell = cell }, p, 2));
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 32; x++)
                    {
                        var c = result.GetPixel(x, y);
                        Assert.True(allowed.Contains(c.Red) && allowed.Contains(c.Green) && allowed.Contains(c.Blue), $"cell {cell} ({x},{y}) = {c}");
                    }
                }
            }
        }

        [Fact]
        public void CT126_Draw_ReplacesWhatWasThere()
        {
            // BlendMode.Src: a translucent capture replaces the canvas instead of blending over it.
            var info = new SKImageInfo(8, 8, SKColorType.Bgra8888, SKAlphaType.Premul);
            var px = new byte[8 * 8 * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i + 2] = 64;
                px[i + 3] = 64;
            }
            using var source = SKImage.FromPixelCopy(info, px, info.RowBytes);
            var inputs = new MosaicInputs { Source = source, Pyramid = new[] { source }, AreaSize = new SKSizeI(8, 8) };
            foreach (int cell in new[] { 1, 2 })
            {
                using var result = Render.Draw(8, 8, _c =>
                {
                    _c.Clear(SKColors.Blue);
                    new MosaicPipeline().Draw(_c, SKRect.Create(8, 8), inputs, new Probe { Cell = cell }, MosaicValues(0), 256);
                });
                Assert.Equal(new SKColor(255, 0, 0, 64), result.GetPixel(4, 4));
            }
        }

        [Fact]
        public void CT125_CreateSurface_RasterWithoutContext()
        {
            using var surface = MosaicPipeline.CreateSurface(null, new SKImageInfo(7, 5, SKColorType.Bgra8888, SKAlphaType.Premul));
            Assert.Null(surface.Context);
            using var image = surface.Snapshot();
            Assert.Equal((7, 5), (image.Width, image.Height));
            Assert.False(image.IsTextureBacked);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-1, 5)]
        public void CT125_CreateSurface_ImpossibleSize_Throws(int _w, int _h)
        {
            var e = Assert.Throws<InvalidOperationException>(() => MosaicPipeline.CreateSurface(null, new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul)));
            Assert.Equal($"Cannot create a {_w}x{_h} surface", e.Message);
        }

        private static SKImage Solid(int _w, int _h, SKColor _color)
        {
            using var surface = SKSurface.Create(new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul));
            surface.Canvas.Clear(_color);
            return surface.Snapshot();
        }
    }
}
