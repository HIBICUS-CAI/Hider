using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Hider
{
    /// <summary>
    /// The mosaic tray icon (FR-71). Drawn in code until Step 9 adds the generated .ico / PNG files.
    /// </summary>
    public static class AppIcons
    {
        private static Bitmap? tray;

        public static Bitmap Tray => tray ??= CreateMosaic(32);

        private static unsafe Bitmap CreateMosaic(int _size)
        {
            var bitmap = new WriteableBitmap(new PixelSize(_size, _size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using var buffer = bitmap.Lock();
            uint[] palette = { 0xFF3E4756, 0xFF68768A, 0xFF8A8F99, 0xFFC8CED8 };
            int cell = _size / 4;
            for (int y = 0; y < _size; y++)
            {
                var row = (uint*)((byte*)buffer.Address + y * buffer.RowBytes);
                for (int x = 0; x < _size; x++)
                {
                    int cx = x / cell, cy = y / cell;
                    row[x] = palette[(cx * 3 + cy * 5 + cx * cy) % palette.Length];
                }
            }
            return bitmap;
        }
    }
}
