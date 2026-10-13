using System.Diagnostics;
using System.Security.Cryptography;
using Hider.Core.Mosaic;
using Hider.Core.Mosaic.Methods;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class PipelineTests
    {
        private readonly ITestOutputHelper output;

        public PipelineTests(ITestOutputHelper _output)
        {
            output = _output;
        }

        // SHA-256 of SyntheticImage.CreatePixels() (BGRA). Changing the generator changes every golden.
        private const string SyntheticSha256 = "7A21C5314E37C22B8EE26785C80A9C9CE62866DFEA7F394FDD94EF8972504B08";

        [Fact]
        public void CT41_SyntheticImage_IsDeterministic()
        {
            var a = SyntheticImage.CreatePixels();
            var b = SyntheticImage.CreatePixels();
            Assert.Equal(a, b);
            string hash = Convert.ToHexString(SHA256.HashData(a));
            Assert.True(hash == SyntheticSha256, $"SHA-256 {hash}");
        }

        [Fact]
        public void CT42_Pixelate12_MatchesGolden()
        {
            using var actual = Render.Mosaic("pixelate");
            Golden.AssertMatches("CT42_pixelate_block12", actual, 1);
        }

        [Fact]
        public void CT43_PixelateOpacityZero_IsIdentity()
        {
            using var source = SyntheticImage.CreateImage();
            using var expected = Render.ToBitmap(source);
            using var actual = Render.Mosaic("pixelate", _opacity: 0.0, _source: source);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void CT44_PixelateHalfOpacity_RoundsBlockUp()
        {
            var method = (PixelateMethod)MethodRegistry.Find("pixelate")!;
            var p = ParamValues.ForMosaic(method, new MethodSettings<MosaicCommon>(), 0.5);
            Assert.Equal(6.5, p.Effective("block"), 9);
            Assert.Equal(7, PixelateMethod.EffectiveBlock(p));
            using var actual = Render.Mosaic("pixelate", _opacity: 0.5);
            AssertCells(actual, 7);
        }

        [Fact]
        public void CT45_CellBoundaries_FollowTheAreaOrigin()
        {
            using var atZero = Render.Mosaic("pixelate");
            AssertCells(atZero, 12);
            using var moved = Render.Mosaic("pixelate", _origin: new SKPointI(5, 3));
            Assert.Equal(0, Golden.MaxDiff(atZero, moved));
        }

        [Fact]
        public void CT46_Levels4_OnlyFourValues()
        {
            using var actual = Render.Mosaic("pixelate", _levels: 4);
            var allowed = new HashSet<byte> { 0, 85, 170, 255 };
            for (int y = 0; y < actual.Height; y++)
            {
                for (int x = 0; x < actual.Width; x++)
                {
                    var c = actual.GetPixel(x, y);
                    Assert.True(allowed.Contains(c.Red) && allowed.Contains(c.Green) && allowed.Contains(c.Blue), $"({x},{y}) = {c}");
                }
            }
        }

        [Fact]
        public void CT47_Levels256_IsIdentity()
        {
            using var source = SyntheticImage.CreateImage();
            using var expected = Render.ToBitmap(source);
            using var actual = Render.Mosaic("pixelate", _opacity: 0.0, _levels: 256, _source: source);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void CT48_PreBlur8_MatchesGolden()
        {
            using var source = SyntheticImage.CreateImage();
            var method = MethodRegistry.Find("pixelate")!;
            var settings = new MethodSettings<MosaicCommon>();
            settings.Common.Blur = 8;
            var p = ParamValues.ForMosaic(method, settings, 1.0);
            var inputs = MosaicPipeline.Prepare(source, default, p);
            try
            {
                using var blurred = Render.ToBitmap(inputs.Source!);
                Golden.AssertMatches("CT48_blur_r8", blurred, 2);
            }
            finally
            {
                MosaicPipeline.Release(inputs, source);
            }
        }

        [Fact]
        public void CT49_BlurRunsBeforePixelate()
        {
            using var pipeline = Render.Mosaic("pixelate", _blur: 8);
            Golden.AssertMatches("CT49_blur8_pixelate12", pipeline, 2);

            // The other order (pixelate, then blur the cells) gives a different picture.
            using var pixelated = Render.Mosaic("pixelate");
            using var pixelatedImage = SKImage.FromBitmap(pixelated);
            using var reversed = Render.Draw(pixelated.Width, pixelated.Height, _c =>
            {
                using var blur = SKImageFilter.CreateBlur(8 * MosaicPipeline.SigmaPerRadius, 8 * MosaicPipeline.SigmaPerRadius, SKShaderTileMode.Clamp);
                using var paint = new SKPaint { ImageFilter = blur };
                _c.DrawImage(pixelatedImage, 0, 0, paint);
            });
            Assert.True(Golden.MaxDiff(pipeline, reversed) > 8, "blur → pixelate should differ from pixelate → blur");
            AssertCells(pipeline, 12);
        }

        [Fact]
        public void CT51_Block_DeterministicPerSeed()
        {
            using var a = Render.Generated("block", 200, 120);
            using var b = Render.Generated("block", 200, 120);
            using var c = Render.Generated("block", 200, 120, _seed: 4321);
            Assert.Equal(0, Golden.MaxDiff(a, b));
            Assert.True(Golden.MaxDiff(a, c) > 0);
            Golden.AssertMatches("CT51_block_seed1234", a, 1);
        }

        [Fact]
        public void CT52_Block_ResizeKeepsExistingCells()
        {
            using var small = Render.Generated("block", 200, 120);
            using var wide = Render.Generated("block", 300, 120);
            for (int y = 0; y < 120; y++)
            {
                for (int x = 0; x < 200; x++)
                {
                    Assert.Equal(small.GetPixel(x, y), wide.GetPixel(x, y));
                }
            }
            var newCells = Enumerable.Range(0, 120 / 12).Select(_r => wide.GetPixel(250, _r * 12 + 6)).Distinct().Count();
            Assert.True(newCells > 1, "the added columns have cells too");
        }

        [Fact]
        public void CT53_BlockGap_JointsAreBaseColor()
        {
            using var bitmap = Render.Generated("block", 120, 72, new() { ["cellSize"] = 12, ["variance"] = 1.0, ["gap"] = 2 });
            var baseColor = SKColor.Parse("#68768A");
            for (int y = 0; y < 72; y++)
            {
                for (int x = 0; x < 120; x++)
                {
                    if (x % 12 >= 10 || y % 12 >= 10)
                    {
                        Assert.Equal(baseColor, bitmap.GetPixel(x, y));
                    }
                }
            }
            Assert.NotEqual(baseColor, bitmap.GetPixel(5, 5));
        }

        [Theory]
        [InlineData(12, 6)]
        [InlineData(12, 11)]
        [InlineData(8, 4)]
        [InlineData(2, 1)]
        public void CT53_WideGap_CellsKeepTheirColors(int _cell, int _gap)
        {
            // gap ≥ cell / 2 put the cell centre on a joint; the cells must still vary and the joints stay the base color.
            using var bitmap = Render.Generated("block", 96, 48, new() { ["cellSize"] = _cell, ["variance"] = 1.0, ["gap"] = _gap });
            var baseColor = SKColor.Parse("#68768A");
            var interiors = new HashSet<SKColor>();
            for (int y = 0; y < 48; y++)
            {
                for (int x = 0; x < 96; x++)
                {
                    if (x % _cell >= _cell - _gap || y % _cell >= _cell - _gap)
                    {
                        Assert.Equal(baseColor, bitmap.GetPixel(x, y));
                    }
                    else
                    {
                        interiors.Add(bitmap.GetPixel(x, y));
                    }
                }
            }
            Assert.True(interiors.Count > 1, $"cell {_cell} gap {_gap}: {interiors.Count} interior color(s)");
        }

        [Fact]
        public void CT54_BlockVarianceZero_AllBaseColor()
        {
            using var bitmap = Render.Generated("block", 96, 60, new() { ["cellSize"] = 12, ["variance"] = 0, ["gap"] = 0 });
            var baseColor = SKColor.Parse("#68768A");
            for (int y = 0; y < 60; y++)
            {
                for (int x = 0; x < 96; x++)
                {
                    Assert.Equal(baseColor, bitmap.GetPixel(x, y));
                }
            }
        }

        [Fact]
        public void CT55_Generated_IsOpaque()
        {
            using var bitmap = Render.Generated("block", 64, 64, new() { ["gap"] = 3 });
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    Assert.Equal(255, bitmap.GetPixel(x, y).Alpha);
                }
            }
        }

        [Fact]
        public void CT56_Generated_IndependentOfAreaOrigin()
        {
            using var a = Render.Generated("block", 150, 90);
            using var b = Render.Generated("block", 150, 90, _origin: new SKPointI(-777, 345));
            Assert.Equal(0, Golden.MaxDiff(a, b));
        }

        [Fact]
        public void CT57_GeneratedCache_RendersOncePerKey()
        {
            var method = MethodRegistry.Find("block")!;
            var settings = new MethodSettings<GeneratedCommon>();
            using var cache = new GeneratedCache();
            var p = ParamValues.ForGenerated(method, settings);
            var first = cache.Get(method, p, new SKSizeI(100, 60));
            var again = cache.Get(method, ParamValues.ForGenerated(method, settings), new SKSizeI(100, 60));
            Assert.Same(first, again);
            Assert.Equal(1, cache.RenderCount);
            cache.Get(method, p, new SKSizeI(120, 60));
            Assert.Equal(2, cache.RenderCount);
            settings.Common.Seed = 7;
            cache.Get(method, ParamValues.ForGenerated(method, settings), new SKSizeI(120, 60));
            Assert.Equal(3, cache.RenderCount);
        }

        [Fact]
        public void GeneratedCache_Detach_HandsTheImageOver()
        {
            // The owner frees a detached image itself (on the render thread for GPU textures); the cache draws anew.
            var method = MethodRegistry.Find("block")!;
            var p = ParamValues.ForGenerated(method, new MethodSettings<GeneratedCommon>());
            using var cache = new GeneratedCache();
            var first = cache.Get(method, p, new SKSizeI(40, 30));
            using (var detached = cache.Detach())
            {
                Assert.Same(first, detached);
            }
            Assert.Null(cache.Detach());
            var again = cache.Get(method, p, new SKSizeI(40, 30));
            Assert.NotSame(first, again);
            Assert.Equal(2, cache.RenderCount);
        }

        [Fact]
        public void CT58_LargeArea_CpuPathCompletes()
        {
            using var source = SyntheticImage.CreateImage(1920, 1080);
            double Time(Func<SKBitmap> _draw)
            {
                _draw().Dispose(); // warm up: shader compilation and first allocations
                var sw = Stopwatch.StartNew();
                using var bitmap = _draw();
                Assert.Equal(1920, bitmap.Width);
                return sw.Elapsed.TotalMilliseconds;
            }
            double harness = Time(() => Render.Draw(1920, 1080, _ => { })); // surface + clear + read back, no drawing
            double mosaic = Time(() => Render.Mosaic("pixelate", _source: source));
            double blur = Time(() => Render.Mosaic("pixelate", _blur: 8, _source: source));
            double levels = Time(() => Render.Mosaic("pixelate", _levels: 8, _source: source));
            double block = Time(() => Render.Generated("block", 1920, 1080));
            double gap = Time(() => Render.Generated("block", 1920, 1080, new() { ["gap"] = 2 }));
            double blurMethod = Time(() => Render.Mosaic("blur", _source: source));
            double hexcell = Time(() => Render.Mosaic("hexcell", _source: source));
            double noise = Time(() => Render.Generated("noise", 1920, 1080));
            output.WriteLine($"CT-58 1920x1080 CPU raster (prepare + draw, incl. {harness:F1} ms harness): pixelate {mosaic:F1} ms, blur 8 + pixelate {blur:F1} ms, " +
                $"pixelate + levels 8 {levels:F1} ms, block {block:F1} ms, block with gap {gap:F1} ms, blur method {blurMethod:F1} ms, " +
                $"hexcell {hexcell:F1} ms, noise {noise:F1} ms");
            // The test items record the times only and do not judge by them (CT-58): a time limit would also make slow
            // runners flaky. Completing every path at this size is the check.
        }

        [Theory]
        [InlineData(24, 24)]
        [InlineData(1, 1)]
        [InlineData(1, 300)]
        public void CT59_TinyAreas_NoException(int _w, int _h)
        {
            using var source = SyntheticImage.CreateImage(_w, _h);
            using var mosaic = Render.Mosaic("pixelate", _source: source, _blur: 4, _levels: 8);
            using var generated = Render.Generated("block", _w, _h, new() { ["gap"] = 2 });
            Assert.Equal(_w, mosaic.Width);
            Assert.Equal(_h, generated.Height);
        }

        [Fact]
        public void CT60_Premultiplied_NotDarkened()
        {
            var info = new SKImageInfo(48, 48, SKColorType.Bgra8888, SKAlphaType.Premul);
            var px = new byte[48 * 48 * 4];
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = 50; px[i + 1] = 75; px[i + 2] = 100; px[i + 3] = 128; // premultiplied (100, 150, 200) at 50%
            }
            using var source = SKImage.FromPixelCopy(info, px, info.RowBytes);
            using var actual = Render.Mosaic("pixelate", _source: source);
            var c = actual.GetPixel(20, 20);
            Assert.InRange(c.Red, 197, 203);
            Assert.InRange(c.Green, 147, 153);
            Assert.InRange(c.Blue, 97, 103);
            Assert.InRange(c.Alpha, 126, 130);
        }

        /// <summary>Every block x block cell (aligned to 0,0) is a single color.</summary>
        private static void AssertCells(SKBitmap _bitmap, int _block)
        {
            for (int cy = 0; cy < _bitmap.Height; cy += _block)
            {
                for (int cx = 0; cx < _bitmap.Width; cx += _block)
                {
                    var first = _bitmap.GetPixel(cx, cy);
                    for (int y = cy; y < Math.Min(cy + _block, _bitmap.Height); y++)
                    {
                        for (int x = cx; x < Math.Min(cx + _block, _bitmap.Width); x++)
                        {
                            Assert.True(_bitmap.GetPixel(x, y) == first, $"cell ({cx},{cy}) is not uniform at ({x},{y})");
                        }
                    }
                }
            }
        }
    }
}
