using System.Runtime.InteropServices;
using Hider.Core.Mosaic;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>
    /// Runs an <see cref="ICpuRenderer"/> into a pixmap whose rows are longer than the pixels (row padding), the way any
    /// caller of the public interface may hand one over, and compares it with the same call into a tight pixmap.
    /// </summary>
    internal static class PaddedTarget
    {
        public const byte Sentinel = 0xA5;

        /// <summary>
        /// Renders into a tight w x h pixmap and into one with <paramref name="_padding"/> extra bytes per row. Asserts that
        /// both accept, that every row of the padded one equals the tight one and that the padding bytes are untouched.
        /// </summary>
        public static void AssertSameAsTight(ICpuRenderer _renderer, MosaicInputs _inputs, ParamValues _p, int _w, int _h, int _padding)
        {
            var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888, SKAlphaType.Premul);
            byte[] tight = Render(_renderer, _inputs, _p, info, info.RowBytes);
            int rowBytes = info.RowBytes + _padding;
            byte[] padded = Render(_renderer, _inputs, _p, info, rowBytes);
            for (int y = 0; y < _h; y++)
            {
                Assert.True(tight.AsSpan(y * info.RowBytes, info.RowBytes).SequenceEqual(padded.AsSpan(y * rowBytes, info.RowBytes)), $"row {y} differs");
                Assert.True(padded.AsSpan(y * rowBytes + info.RowBytes, _padding).IndexOfAnyExcept(Sentinel) < 0, $"padding of row {y} was written");
            }
        }

        private static byte[] Render(ICpuRenderer _renderer, MosaicInputs _inputs, ParamValues _p, SKImageInfo _info, int _rowBytes)
        {
            var buffer = new byte[_rowBytes * _info.Height];
            Array.Fill(buffer, Sentinel);
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                using var pixmap = new SKPixmap(_info, handle.AddrOfPinnedObject(), _rowBytes);
                Assert.Equal(_rowBytes, pixmap.RowBytes);
                Assert.True(_renderer.TryRender(pixmap, _inputs, _p));
            }
            finally
            {
                handle.Free();
            }
            return buffer;
        }
    }
}
