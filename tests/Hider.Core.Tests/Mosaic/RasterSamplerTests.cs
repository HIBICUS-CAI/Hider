using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>Every member and branch of <see cref="RasterSampler"/> (CpuRendererTests compares it with Skia's sampling).</summary>
    public class RasterSamplerTests
    {
        /// <summary>A w x h premultiplied image whose texel (x, y) is opaque gray x * 10 + y (blue), x (green), y (red).</summary>
        private static SKImage Ramp(int _w, int _h, SKColorType _type = SKColorType.Bgra8888, SKAlphaType _alpha = SKAlphaType.Premul, int _padding = 0)
        {
            var info = new SKImageInfo(_w, _h, _type, _alpha);
            int rowBytes = _w * 4 + _padding;
            var px = new byte[rowBytes * _h];
            Array.Fill(px, (byte)0xEE); // the padding is garbage
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int i = y * rowBytes + x * 4;
                    byte b = (byte)(x * 10 + y), g = (byte)x, r = (byte)y;
                    if (_type == SKColorType.Rgba8888)
                    {
                        (px[i], px[i + 1], px[i + 2]) = (r, g, b);
                    }
                    else
                    {
                        (px[i], px[i + 1], px[i + 2]) = (b, g, r);
                    }
                    px[i + 3] = 255;
                }
            }
            return SKImage.FromPixelCopy(info, px, rowBytes);
        }

        private static uint Bgra(int _x, int _y)
        {
            return 0xFF000000u | ((uint)_y << 16) | ((uint)_x << 8) | (uint)(_x * 10 + _y);
        }

        [Fact]
        public void CT131_Create_ImageWithoutCpuPixels_IsNull()
        {
            // A picture-backed image (like a GPU texture) has no pixels to peek.
            using var recorder = new SKPictureRecorder();
            recorder.BeginRecording(SKRect.Create(8, 8)).Clear(SKColors.Red);
            using var picture = recorder.EndRecording();
            using var image = SKImage.FromPicture(picture, new SKSizeI(8, 8));
            Assert.Null(image.PeekPixels());
            Assert.Null(RasterSampler.Create(image, 1, false));
        }

        [Theory]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Premul, true)]
        [InlineData(SKColorType.Rgba8888, SKAlphaType.Premul, true)]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Unpremul, false)]
        [InlineData(SKColorType.Rgba8888, SKAlphaType.Unpremul, false)]
        [InlineData(SKColorType.Bgra8888, SKAlphaType.Opaque, false)]
        [InlineData(SKColorType.Rgba8888, SKAlphaType.Opaque, false)]
        [InlineData(SKColorType.Gray8, SKAlphaType.Opaque, false)]
        [InlineData(SKColorType.Rgb565, SKAlphaType.Opaque, false)]
        [InlineData(SKColorType.RgbaF16, SKAlphaType.Premul, false)]
        public void CT131_Create_OnlyPremultiplied8BitBgraOrRgba(SKColorType _type, SKAlphaType _alpha, bool _supported)
        {
            var info = new SKImageInfo(4, 4, _type, _alpha);
            using var image = SKImage.FromPixelCopy(info, new byte[info.BytesSize], info.RowBytes);
            Assert.Equal(_type, image.ColorType);
            using var sampler = RasterSampler.Create(image, 1, false);
            Assert.Equal(_supported, sampler is not null);
        }

        [Theory]
        [InlineData(SKColorType.Bgra8888)]
        [InlineData(SKColorType.Rgba8888)] // red and blue swapped into BGRA order
        public void CT131_Nearest_EveryTexel_AsLittleEndianBgra(SKColorType _type)
        {
            using var image = Ramp(6, 5, _type);
            using var sampler = RasterSampler.Create(image, 1, false)!;
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    Assert.Equal(Bgra(x, y), sampler.Sample(x + 0.5f, y + 0.5f));
                    Assert.Equal(Bgra(x, y), sampler.Sample(x + 0.001f, y + 0.001f));
                    Assert.Equal(Bgra(x, y), sampler.Sample(x + 1, y + 1)); // the bottom-right corner belongs to the texel, as in Skia
                    Assert.Equal(Bgra(x, y), sampler.Sample(x + 0.999f, y + 0.999f));
                }
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(4)]
        public void CT131_Nearest_MatchesSkiasRasterAtTexelEdges(int _scale)
        {
            // On a texel edge (u a whole number) Skia's raster takes the texel before it: texel ceil(u) - 1. Compared with
            // Skia itself, through a runtime shader that evaluates the image shader at exactly the given point.
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, _scale, false)!;
            using var effect = SKRuntimeEffect.CreateShader("uniform float2 c; uniform shader src; half4 main(float2 p) { return src.eval(c); }", out string? errors);
            Assert.Null(errors);
            using var child = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, new SKSamplingOptions(SKFilterMode.Nearest),
                SKMatrix.CreateScale(_scale, _scale));
            var points = new List<float> { -3f, -0.5f, 0f, 0.25f, 100f };
            for (int i = 1; i <= 6; i++)
            {
                float edge = i * _scale;
                points.AddRange(new[] { edge, MathF.BitDecrement(edge), MathF.BitIncrement(edge), edge - 0.5f });
            }
            foreach (float x in points)
            {
                foreach (float y in new[] { 0f, 1f, 2.5f, 3f * _scale, MathF.BitIncrement(3f * _scale), 5f * _scale })
                {
                    using var uniforms = new SKRuntimeEffectUniforms(effect) { ["c"] = new[] { x, y } };
                    using var children = new SKRuntimeEffectChildren(effect) { ["src"] = child };
                    using var shader = effect.ToShader(uniforms, children);
                    using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
                    using var drawn = Imaging.Render.Draw(1, 1, _c => _c.DrawRect(0, 0, 1, 1, paint));
                    Assert.True((uint)drawn.GetPixel(0, 0) == sampler.Sample(x, y), $"scale {_scale} at ({x:R}, {y:R}): Skia {drawn.GetPixel(0, 0)}");
                }
            }
        }

        [Fact]
        public void CT131_Nearest_OutsideTheImage_ClampsToTheEdge()
        {
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 1, false)!;
            Assert.Equal(Bgra(0, 0), sampler.Sample(-100, -0.1f));
            Assert.Equal(Bgra(5, 4), sampler.Sample(6, 5));
            Assert.Equal(Bgra(5, 0), sampler.Sample(1000, -3));
            Assert.Equal(Bgra(0, 4), sampler.Sample(-1, 99));
        }

        [Fact]
        public void CT131_Nearest_Scale_DividesTheCoordinates()
        {
            // A pyramid level 2^k: area point (x, y) reads texel (x / 2^k, y / 2^k).
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 4, false)!;
            Assert.Equal(Bgra(0, 0), sampler.Sample(3.9f, 3.9f));
            Assert.Equal(Bgra(0, 0), sampler.Sample(4f, 0f)); // texel 0 ends at area x = 4
            Assert.Equal(Bgra(1, 0), sampler.Sample(4.01f, 0f));
            Assert.Equal(Bgra(2, 3), sampler.Sample(10f, 15f));
            Assert.Equal(Bgra(5, 4), sampler.Sample(100f, 100f));
        }

        [Fact]
        public void CT132_Linear_AtTexelCentres_TheTexelItself()
        {
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 1, true)!;
            for (int y = 0; y < 5; y++)
            {
                for (int x = 0; x < 6; x++)
                {
                    Assert.Equal(Bgra(x, y), sampler.Sample(x + 0.5f, y + 0.5f));
                }
            }
        }

        [Fact]
        public void CT132_Linear_BetweenTexels_InterpolatesAndRounds()
        {
            // Texels: blue = x * 10 + y. Halfway between (1, 0) and (2, 0): blue 15, green 1.5 -> 2 (to even), red 0.
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 1, true)!;
            Assert.Equal(0xFF000000u | (0u << 16) | (2u << 8) | 15u, sampler.Sample(2.0f, 0.5f));
            // A quarter of the way from (1, 0) to (2, 0): blue 12.5 -> 12, green 1.25 -> 1.
            Assert.Equal(0xFF000000u | (1u << 8) | 12u, sampler.Sample(1.75f, 0.5f));
            // The centre of four texels (1..2, 1..2): blue (11 + 21 + 12 + 22) / 4 = 16.5 -> 16, green 1.5 -> 2, red 1.5 -> 2.
            Assert.Equal(0xFF000000u | (2u << 16) | (2u << 8) | 16u, sampler.Sample(2.0f, 2.0f));
        }

        [Fact]
        public void CT132_Linear_OutsideTheImage_ClampsLikeSkia()
        {
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 1, true)!;
            Assert.Equal(Bgra(0, 0), sampler.Sample(0, 0));          // u = -0.5: both neighbours are texel 0
            Assert.Equal(Bgra(0, 0), sampler.Sample(-50, -50));
            Assert.Equal(Bgra(5, 4), sampler.Sample(6, 5));
            Assert.Equal(Bgra(5, 4), sampler.Sample(500, 500));
        }

        [Fact]
        public void CT132_Linear_Scale_PyramidLevelCoordinates()
        {
            // Scale 2: area point (2, 1) is level point (1, 0.5), the centre between texels (0, 0) and (1, 0).
            using var image = Ramp(6, 5);
            using var sampler = RasterSampler.Create(image, 2, true)!;
            Assert.Equal(0xFF000000u | (0u << 16) | (0u << 8) | 5u, sampler.Sample(2f, 1f));   // blue 0 / 10 -> 5, green 0 / 1 -> 0 (0.5 to even)
            Assert.Equal(Bgra(1, 1), sampler.Sample(3f, 3f));
        }

        [Fact]
        public void CT132_Linear_RgbaSource_SwappedBeforeInterpolating()
        {
            using var image = Ramp(6, 5, SKColorType.Rgba8888);
            using var sampler = RasterSampler.Create(image, 1, true)!;
            Assert.Equal(0xFF000000u | (2u << 8) | 15u, sampler.Sample(2.0f, 0.5f));
        }

        [Fact]
        public void CT132_Linear_TranslucentPremultipliedValues_InterpolatedAsStored()
        {
            var info = new SKImageInfo(2, 1, SKColorType.Bgra8888, SKAlphaType.Premul);
            var px = new byte[] { 100, 0, 0, 100, 0, 0, 0, 0 }; // premultiplied blue at alpha 100, then transparent
            using var image = SKImage.FromPixelCopy(info, px, info.RowBytes);
            using var sampler = RasterSampler.Create(image, 1, true)!;
            Assert.Equal((50u << 24) | 50u, sampler.Sample(1.0f, 0.5f));
        }

        [Fact]
        public void CT131_PaddedRows_UseTheRowStride()
        {
            using var image = Ramp(5, 4, _padding: 12);
            using (var pixmap = image.PeekPixels())
            {
                Assert.Equal(5 * 4 + 12, pixmap.RowBytes); // the padding survives the copy
            }
            using var nearest = RasterSampler.Create(image, 1, false)!;
            using var linear = RasterSampler.Create(image, 1, true)!;
            for (int y = 0; y < 4; y++)
            {
                Assert.Equal(Bgra(4, y), nearest.Sample(4.5f, y + 0.5f));
                Assert.Equal(Bgra(4, y), linear.Sample(4.5f, y + 0.5f));
                Assert.Equal(Bgra(4, y), linear.Sample(99f, y + 0.5f)); // never the garbage after the last texel
            }
        }

        [Fact]
        public void CT131_OnePixelImage_EveryPointIsThatPixel()
        {
            using var image = Ramp(1, 1);
            using var nearest = RasterSampler.Create(image, 1, false)!;
            using var linear = RasterSampler.Create(image, 8, true)!;
            foreach (var (x, y) in new[] { (0f, 0f), (0.5f, 0.5f), (-3f, 7f), (100f, -100f) })
            {
                Assert.Equal(Bgra(0, 0), nearest.Sample(x, y));
                Assert.Equal(Bgra(0, 0), linear.Sample(x, y));
            }
        }

        [Fact]
        public void CT131_Dispose_Twice_IsHarmless()
        {
            using var image = Ramp(2, 2);
            var sampler = RasterSampler.Create(image, 1, false)!;
            sampler.Dispose();
            sampler.Dispose();
        }
    }
}
