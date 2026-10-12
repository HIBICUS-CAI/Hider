namespace Hider.Core.Model
{
    /// <summary>Pure geometry for moving and resizing areas (FR-32, FR-33). Everything is in physical pixels.</summary>
    public static class AreaGeometry
    {
        /// <summary>
        /// A window length in DIP for <paramref name="_px"/> physical pixels at <paramref name="_scale"/> (X11). The platform
        /// turns it back into pixels by truncating length × scale; a quarter pixel of slack keeps float error (175%: 61 / 1.75
        /// × 1.75 = 60.99…) from losing the last column, which would leave a line of the content uncovered (NFR-05). Win32
        /// stores the size it is given back from WM_SIZE without the slack, so it uses <see cref="StableLength"/> instead.
        /// </summary>
        public static double DipLength(int _px, double _scale)
        {
            return (_px + 0.25) / _scale;
        }

        /// <summary>
        /// The shortest length of at least <paramref name="_px"/> physical pixels that survives Avalonia's Win32 round trip at
        /// <paramref name="_scale"/>: WM_SIZE stores px / scale and Resize sends back (int)(that × scale). A length that does
        /// not survive (175%: 61 / 1.75 × 1.75 = 60.99…) would end up a pixel short, leaving a line of the content uncovered
        /// (NFR-05); a longer stable length covers it. Windows scales are dpi / 96: over all of 96..480 dpi and lengths up to
        /// 16383 px the stable length is at most 37 px longer (263 dpi: 1479 becomes 1516), usually 0 to 5.
        /// </summary>
        public static int StableLength(int _px, double _scale)
        {
            // The search is not cut short: any unstable value would shrink below the area. 4096 is far beyond the longest run
            // of unstable lengths (37) and only keeps a malformed scale from looping forever.
            for (int p = _px; p < _px + 4096; p++)
            {
                if ((int)(p / _scale * _scale) == p)
                {
                    return p;
                }
            }
            return _px + 4096;
        }

        /// <summary>Minimum width / height in physical pixels for a window scale (24 DIP, FR-32).</summary>
        public static int MinSizePx(double _scale)
        {
            return (int)Math.Ceiling(AreaDefaults.MinSizeDip * _scale);
        }

        /// <summary>
        /// Resizes <paramref name="_start"/> by dragging <paramref name="_handle"/> by (dx, dy). Only the dragged edges
        /// move, and they stop where the size would go under the minimum (the opposite edge never moves).
        /// </summary>
        public static PhysicalRect Resize(PhysicalRect _start, ResizeHandle _handle, int _dx, int _dy, double _scale)
        {
            int min = MinSizePx(_scale);
            int left = _start.X, top = _start.Y, right = _start.Right, bottom = _start.Bottom;
            if (_handle.HasFlag(ResizeHandle.Left))
            {
                left = Math.Min(left + _dx, right - min);
            }
            if (_handle.HasFlag(ResizeHandle.Right))
            {
                right = Math.Max(right + _dx, left + min);
            }
            if (_handle.HasFlag(ResizeHandle.Top))
            {
                top = Math.Min(top + _dy, bottom - min);
            }
            if (_handle.HasFlag(ResizeHandle.Bottom))
            {
                bottom = Math.Max(bottom + _dy, top + min);
            }
            return new PhysicalRect(left, top, right - left, bottom - top);
        }

        /// <summary>Arrow key move: 1 px, or 10 px with Shift (FR-33).</summary>
        public static PhysicalRect Nudge(PhysicalRect _rect, int _dirX, int _dirY, bool _shift)
        {
            int step = _shift ? 10 : 1;
            return _rect.Offset(Math.Sign(_dirX) * step, Math.Sign(_dirY) * step);
        }

        /// <summary>Clamps a restored or edited size to the minimum without moving the origin.</summary>
        public static PhysicalRect EnsureMinSize(PhysicalRect _rect, double _scale)
        {
            int min = MinSizePx(_scale);
            var r = _rect.Normalized();
            return r with { Width = Math.Max(r.Width, min), Height = Math.Max(r.Height, min) };
        }
    }
}
