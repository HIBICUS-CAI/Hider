using SkiaSharp;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>
    /// Deterministic stand-in for captured content (test tab "ゴールデン画像"): a gradient background, a 16 px grid,
    /// flat color blocks and thin text-like strokes. Generated with integer arithmetic only, so every platform produces the
    /// same bytes (CT-41) and no binary input image is kept in the repository.
    /// </summary>
    public static class SyntheticImage
    {
        public const int Width = 320;
        public const int Height = 180;

        /// <summary>Premultiplied BGRA8888, opaque.</summary>
        public static byte[] CreatePixels(int _width = Width, int _height = Height)
        {
            var px = new byte[_width * _height * 4];
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int r = 40 + 180 * x / Math.Max(1, _width - 1);
                    int g = 60 + 120 * y / Math.Max(1, _height - 1);
                    int b = 200 - 140 * (x + y) / Math.Max(1, _width + _height - 2);
                    if (x % 16 == 0 || y % 16 == 0)
                    {
                        (r, g, b) = (r / 2, g / 2, b / 2);
                    }
                    Set(px, _width, x, y, r, g, b);
                }
            }
            Fill(px, _width, _height, 200, 20, 90, 50, 0xC8, 0x2A, 0x2A);   // flat red block
            Fill(px, _width, _height, 30, 120, 60, 40, 0xF0, 0xF0, 0xF0);   // flat white block

            // Text-like strokes: short dark segments on light lines, from a fixed LCG.
            uint state = 12345;
            uint Next()
            {
                state = state * 1664525u + 1013904223u;
                return state >> 8;
            }
            for (int line = 8; line + 8 < _height; line += 14)
            {
                Fill(px, _width, _height, 100, line - 2, 180, 11, 0xFA, 0xFA, 0xF5);
                for (int x = 104; x < 276;)
                {
                    int w = 1 + (int)(Next() % 3);
                    int h = 3 + (int)(Next() % 6);
                    Fill(px, _width, _height, x, line + 7 - h, w, h, 0x20, 0x24, 0x2C);
                    x += w + 1 + (int)(Next() % 4);
                }
            }
            return px;
        }

        public static SKImage CreateImage(int _width = Width, int _height = Height)
        {
            var info = new SKImageInfo(_width, _height, SKColorType.Bgra8888, SKAlphaType.Premul);
            return SKImage.FromPixelCopy(info, CreatePixels(_width, _height), info.RowBytes);
        }

        private static void Fill(byte[] _px, int _width, int _height, int _x, int _y, int _w, int _h, int _r, int _g, int _b)
        {
            for (int y = Math.Max(0, _y); y < Math.Min(_height, _y + _h); y++)
            {
                for (int x = Math.Max(0, _x); x < Math.Min(_width, _x + _w); x++)
                {
                    Set(_px, _width, x, y, _r, _g, _b);
                }
            }
        }

        private static void Set(byte[] _px, int _width, int _x, int _y, int _r, int _g, int _b)
        {
            int i = (_y * _width + _x) * 4;
            _px[i] = (byte)_b;
            _px[i + 1] = (byte)_g;
            _px[i + 2] = (byte)_r;
            _px[i + 3] = 255;
        }
    }
}
