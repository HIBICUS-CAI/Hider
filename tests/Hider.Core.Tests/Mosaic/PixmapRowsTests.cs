using System.Runtime.InteropServices;
using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    public class PixmapRowsTests
    {
        [Theory]
        [InlineData(10, 7, 40, 280)]   // tight: w * h * 4
        [InlineData(10, 7, 52, 352)]   // padded: 6 · 52 + 40, the end of the last pixel
        [InlineData(1, 1, 64, 4)]
        [InlineData(3, 2, 16, 28)]
        public void CT119_All_RunsFromTheFirstToTheLastPixel(int _w, int _h, int _rowBytes, int _length)
        {
            var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul);
            var buffer = new byte[_rowBytes * _h];
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                using var pixmap = new SKPixmap(info, handle.AddrOfPinnedObject(), _rowBytes);
                var span = PixmapRows.All(pixmap);
                Assert.Equal(_length, span.Length);
                // The span is the pixmap's memory: its last byte is the last pixel's.
                span[^1] = 0x5A;
                span[0] = 0x3C;
                Assert.Equal(0x5A, buffer[_length - 1]);
                Assert.Equal(0x3C, buffer[0]);
            }
            finally
            {
                handle.Free();
            }
        }

        [Fact]
        public void CT119_All_PixmapWithoutPixels_IsEmpty()
        {
            using var empty = new SKPixmap();
            Assert.Equal(0, PixmapRows.All(empty).Length);
        }

        [Fact]
        public void CT119_All_BitmapPixmap_EndsAtTheLastPixel()
        {
            using var bitmap = new SKBitmap(new SKImageInfo(5, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
            using var pixmap = bitmap.PeekPixels();
            Assert.Equal(bitmap.RowBytes * 3 + 5 * 4, PixmapRows.All(pixmap).Length);
        }
    }
}
