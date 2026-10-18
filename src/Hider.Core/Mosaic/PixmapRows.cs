using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>Writable access to a pixmap's rows for <see cref="ICpuRenderer"/> implementations.</summary>
    internal static class PixmapRows
    {
        /// <summary>
        /// The bytes from the first pixel to the last one, row padding included: row y starts at y · RowBytes.
        /// <see cref="SKPixmap.GetPixelSpan()"/> is only Width · Height · BytesPerPixel long, so it cannot be sliced by
        /// RowBytes when the rows are padded. Empty for a pixmap without pixels.
        /// </summary>
        public static unsafe Span<byte> All(SKPixmap _pixmap)
        {
            int w = _pixmap.Width, h = _pixmap.Height;
            if (w <= 0 || h <= 0)
            {
                return Span<byte>.Empty;
            }
            return new Span<byte>((void*)_pixmap.GetPixels(), checked(_pixmap.RowBytes * (h - 1) + w * _pixmap.BytesPerPixel));
        }
    }
}
