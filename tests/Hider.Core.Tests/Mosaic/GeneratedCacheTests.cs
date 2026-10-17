using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>Every member and branch of <see cref="GeneratedCache"/> (CT-57 checks the render count per key).</summary>
    public class GeneratedCacheTests
    {
        /// <summary>A generated method whose shader cannot be made.</summary>
        private sealed class Broken : IMosaicMethod
        {
            public string Id => "broken";
            public string LabelKey => "method.block";
            public MethodKind Kind => MethodKind.Generated;
            public PlatformSupport Support => PlatformSupport.All;
            public IReadOnlyList<ParamSpec> Params => Array.Empty<ParamSpec>();

            public SKShader CreateShader(MosaicInputs _inputs, ParamValues _p)
            {
                throw new InvalidOperationException("no shader");
            }
        }

        private static readonly IMosaicMethod block = MethodRegistry.Find("block")!;
        private static readonly IMosaicMethod noise = MethodRegistry.Find("noise")!;

        private static ParamValues Values(IMosaicMethod _method, int _seed = 1234)
        {
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.Seed = _seed;
            return ParamValues.ForGenerated(_method, settings);
        }

        private static bool IsDisposed(SKImage _image)
        {
            return _image.Handle == IntPtr.Zero;
        }

        [Fact]
        public void NewCache_NothingRendered()
        {
            using var cache = new GeneratedCache();
            Assert.Equal(0, cache.RenderCount);
            Assert.Null(cache.Detach());
        }

        [Fact]
        public void Get_TheMethodDrawnAtTheSize()
        {
            using var cache = new GeneratedCache();
            var image = cache.Get(block, Values(block), new SKSizeI(60, 40));
            Assert.Equal((60, 40), (image.Width, image.Height));
            using var actual = Render.ToBitmap(image);
            using var expected = Render.Generated("block", 60, 40);
            Assert.Equal(0, Golden.MaxDiff(expected, actual));
        }

        [Fact]
        public void Get_SameKey_SameImageWithoutDrawing()
        {
            using var cache = new GeneratedCache();
            var first = cache.Get(noise, Values(noise), new SKSizeI(30, 20));
            for (int i = 0; i < 3; i++)
            {
                Assert.Same(first, cache.Get(noise, Values(noise), new SKSizeI(30, 20), null));
            }
            Assert.Equal(1, cache.RenderCount);
        }

        [Fact]
        public void Get_AnotherMethodSizeOrValue_DrawsAndFreesTheOldImage()
        {
            using var cache = new GeneratedCache();
            var first = cache.Get(block, Values(block), new SKSizeI(30, 20));
            var otherMethod = cache.Get(noise, Values(noise), new SKSizeI(30, 20));
            Assert.True(IsDisposed(first));
            var otherWidth = cache.Get(noise, Values(noise), new SKSizeI(31, 20));
            Assert.True(IsDisposed(otherMethod));
            var otherHeight = cache.Get(noise, Values(noise), new SKSizeI(31, 21));
            Assert.True(IsDisposed(otherWidth));
            var otherSeed = cache.Get(noise, Values(noise, 1), new SKSizeI(31, 21));
            Assert.True(IsDisposed(otherHeight));
            Assert.False(IsDisposed(otherSeed));
            Assert.Equal(5, cache.RenderCount);
            Assert.Same(otherSeed, cache.Get(noise, Values(noise, 1), new SKSizeI(31, 21)));
        }

        [Theory]
        [InlineData(0, 0, 1, 1)]
        [InlineData(0, 7, 1, 7)]
        [InlineData(9, -3, 9, 1)]
        public void Get_EmptySize_AtLeastOnePixel(int _w, int _h, int _ew, int _eh)
        {
            using var cache = new GeneratedCache();
            var image = cache.Get(block, Values(block), new SKSizeI(_w, _h));
            Assert.Equal((_ew, _eh), (image.Width, image.Height));
            Assert.Same(image, cache.Get(block, Values(block), new SKSizeI(_w, _h)));
        }

        [Fact]
        public void Get_TheKeyIsTheRequestedSize_NotTheImageSize()
        {
            // 0 x 0 and 1 x 1 give the same 1 x 1 image but are separate keys: switching draws again.
            using var cache = new GeneratedCache();
            cache.Get(block, Values(block), new SKSizeI(0, 0));
            cache.Get(block, Values(block), new SKSizeI(1, 1));
            Assert.Equal(2, cache.RenderCount);
        }

        [Fact]
        public void Get_ThrowingDraw_LeavesNoFreedImageBehind()
        {
            using var cache = new GeneratedCache();
            var first = cache.Get(block, Values(block), new SKSizeI(10, 10));
            Assert.Throws<InvalidOperationException>(() => cache.Get(new Broken(), Values(block), new SKSizeI(10, 10)));
            Assert.True(IsDisposed(first));
            Assert.Null(cache.Detach()); // nothing cached
            var again = cache.Get(block, Values(block), new SKSizeI(10, 10)); // the old key is not a hit any more
            Assert.NotSame(first, again);
            Assert.False(IsDisposed(again));
            Assert.Equal(2, cache.RenderCount); // the failed draw is not counted
        }

        [Fact]
        public void Detach_TheCallerOwnsTheImage()
        {
            var cache = new GeneratedCache();
            var image = cache.Get(block, Values(block), new SKSizeI(10, 10));
            Assert.Same(image, cache.Detach());
            cache.Dispose();
            Assert.False(IsDisposed(image)); // the cache no longer frees it
            image.Dispose();
        }

        [Fact]
        public void Detach_ForgetsTheKey()
        {
            using var cache = new GeneratedCache();
            var first = cache.Get(block, Values(block), new SKSizeI(10, 10));
            using var detached = cache.Detach();
            var again = cache.Get(block, Values(block), new SKSizeI(10, 10));
            Assert.NotSame(first, again);
            Assert.Equal(2, cache.RenderCount);
        }

        [Fact]
        public void Dispose_FreesTheImage_RepeatableAndOnAnEmptyCache()
        {
            new GeneratedCache().Dispose();
            var cache = new GeneratedCache();
            var image = cache.Get(block, Values(block), new SKSizeI(10, 10));
            cache.Dispose();
            Assert.True(IsDisposed(image));
            cache.Dispose();
            Assert.Null(cache.Detach());
        }

        [Fact]
        public void Get_AfterDispose_DrawsAgain()
        {
            var cache = new GeneratedCache();
            var first = cache.Get(block, Values(block), new SKSizeI(10, 10));
            cache.Dispose();
            var again = cache.Get(block, Values(block), new SKSizeI(10, 10));
            Assert.NotSame(first, again);
            Assert.False(IsDisposed(again));
            Assert.Equal(2, cache.RenderCount);
            cache.Dispose();
        }

        [Fact]
        public void Get_IsOpaqueBgraPremul()
        {
            using var cache = new GeneratedCache();
            var image = cache.Get(noise, Values(noise), new SKSizeI(16, 16));
            Assert.Equal(SKColorType.Bgra8888, image.ColorType);
            Assert.Equal(SKAlphaType.Premul, image.AlphaType);
            using var bitmap = Render.ToBitmap(image);
            for (int y = 0; y < 16; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    Assert.Equal(255, bitmap.GetPixel(x, y).Alpha);
                }
            }
        }
    }
}
