using System.Buffers.Binary;
using SkiaSharp;

namespace Hider.Core.Mosaic
{
    /// <summary>
    /// Reads a raster image the way the methods' child shader samples it: area pixel coordinates divided by
    /// <c>scale</c> (a pyramid level), clamped at the edges, nearest or linear. For <see cref="ICpuRenderer"/> implementations.
    /// </summary>
    public sealed class RasterSampler : IDisposable
    {
        private readonly SKPixmap pixmap;
        private readonly float invScale;
        private readonly bool linear;
        private readonly bool swapRedBlue;

        // Every SKPixmap property is a native call: read once, the pixels stay where they are while the pixmap lives.
        private readonly int width;
        private readonly int height;
        private readonly int rowBytes;
        private readonly nint pixels;
        private readonly int length;

        private RasterSampler(SKPixmap _pixmap, float _scale, bool _linear)
        {
            pixmap = _pixmap;
            invScale = 1f / _scale;
            linear = _linear;
            swapRedBlue = _pixmap.ColorType == SKColorType.Rgba8888;
            width = _pixmap.Width;
            height = _pixmap.Height;
            rowBytes = _pixmap.RowBytes;
            pixels = _pixmap.GetPixels();
            length = checked(rowBytes * (height - 1) + width * 4);
        }

        /// <summary>Null when the image has no CPU pixels (a GPU texture) or an unsupported format.</summary>
        public static RasterSampler? Create(SKImage _image, float _scale, bool _linear)
        {
            var pixmap = _image.PeekPixels();
            if (pixmap is null)
            {
                return null;
            }
            if (pixmap.ColorType is not (SKColorType.Bgra8888 or SKColorType.Rgba8888) || pixmap.AlphaType != SKAlphaType.Premul)
            {
                pixmap.Dispose();
                return null;
            }
            return new RasterSampler(pixmap, _scale, _linear);
        }

        /// <summary>Premultiplied BGRA (little-endian uint: 0xAARRGGBB) at area point (x, y).</summary>
        public uint Sample(float _x, float _y)
        {
            float u = _x * invScale, v = _y * invScale;
            int w = width, h = height;
            if (!linear)
            {
                return Texel(Math.Clamp((int)MathF.Floor(u), 0, w - 1), Math.Clamp((int)MathF.Floor(v), 0, h - 1));
            }
            u -= 0.5f;
            v -= 0.5f;
            float fu = MathF.Floor(u), fv = MathF.Floor(v);
            float tx = u - fu, ty = v - fv;
            int x0 = Math.Clamp((int)fu, 0, w - 1), x1 = Math.Clamp((int)fu + 1, 0, w - 1);
            int y0 = Math.Clamp((int)fv, 0, h - 1), y1 = Math.Clamp((int)fv + 1, 0, h - 1);
            uint a = Texel(x0, y0), b = Texel(x1, y0), c = Texel(x0, y1), d = Texel(x1, y1);
            uint result = 0;
            for (int shift = 0; shift < 32; shift += 8)
            {
                float top = Channel(a, shift) + (Channel(b, shift) - Channel(a, shift)) * tx;
                float bottom = Channel(c, shift) + (Channel(d, shift) - Channel(c, shift)) * tx;
                float value = top + (bottom - top) * ty;
                result |= (uint)Math.Clamp((int)MathF.Round(value), 0, 255) << shift;
            }
            return result;
        }

        private static float Channel(uint _pixel, int _shift)
        {
            return (_pixel >> _shift) & 0xFF;
        }

        private unsafe uint Texel(int _x, int _y)
        {
            var span = new ReadOnlySpan<byte>((void*)pixels, length);
            uint p = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(_y * rowBytes + _x * 4, 4));
            return swapRedBlue ? (p & 0xFF00FF00) | ((p & 0xFF) << 16) | ((p >> 16) & 0xFF) : p;
        }

        public void Dispose()
        {
            pixmap.Dispose();
        }
    }
}
