using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>Windows for <see cref="UnderlayComposer"/> tests: each is filled with one color, or fails.</summary>
    public sealed class FakeWindowSource : IWindowSource
    {
        public sealed record Entry(WindowInfo Info, (byte R, byte G, byte B) Color, CaptureFailure Failure = CaptureFailure.None, bool Black = false);

        public List<Entry> TopDown { get; } = new();
        public List<nint> Captured { get; } = new();

        public FakeWindowSource Add(nint _handle, PhysicalRect _frame, (byte, byte, byte) _color, string _class = "App",
            bool _visible = true, bool _cloaked = false, CaptureFailure _failure = CaptureFailure.None, bool _black = false, int _border = 0)
        {
            var windowRect = new PhysicalRect(_frame.X - _border, _frame.Y, _frame.Width + 2 * _border, _frame.Height + _border);
            TopDown.Add(new Entry(new WindowInfo(_handle, windowRect, _frame, _visible, _cloaked, _class), _color, _failure, _black));
            return this;
        }

        public IReadOnlyList<WindowInfo> EnumerateTopDown()
        {
            return TopDown.Select(e => e.Info).ToArray();
        }

        public WindowImage Capture(WindowInfo _window, TimeSpan _timeout)
        {
            Captured.Add(_window.Handle);
            var entry = TopDown.Single(e => e.Info.Handle == _window.Handle);
            if (entry.Failure != CaptureFailure.None)
            {
                return new WindowImage(null, entry.Failure);
            }
            var r = _window.WindowRect;
            var image = FrameBuffer.Allocate(r.Width, r.Height);
            var px = image.Bgra.Span;
            var (cr, cg, cb) = entry.Black ? ((byte)0, (byte)0, (byte)0) : entry.Color;
            for (int i = 0; i < px.Length; i += 4)
            {
                px[i] = cb;
                px[i + 1] = cg;
                px[i + 2] = cr;
                px[i + 3] = 255;
            }
            // Mark the invisible border (outside the DWM frame) so tests can see it is never copied.
            for (int y = 0; y < r.Height; y++)
            {
                for (int x = 0; x < r.Width; x++)
                {
                    int sx = r.X + x, sy = r.Y + y;
                    if (!_window.FrameBounds.Intersects(new PhysicalRect(sx, sy, 1, 1)))
                    {
                        int i = (y * r.Width + x) * 4;
                        px[i] = 1; px[i + 1] = 2; px[i + 2] = 3;
                    }
                }
            }
            return new WindowImage(image, CaptureFailure.None);
        }
    }
}
