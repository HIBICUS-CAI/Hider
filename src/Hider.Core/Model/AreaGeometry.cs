namespace Hider.Core.Model
{
    /// <summary>Pure geometry for moving and resizing areas (FR-32, FR-33). Everything is in physical pixels.</summary>
    public static class AreaGeometry
    {
        /// <summary>
        /// A window length in DIP for <paramref name="_px"/> physical pixels at <paramref name="_scale"/>. The platform turns
        /// it back into pixels by truncating length × scale; a quarter pixel of slack keeps float error (175%: 61 / 1.75 ×
        /// 1.75 = 60.99…) from losing the last column, which would leave a line of the content uncovered (NFR-05).
        /// </summary>
        public static double DipLength(int _px, double _scale)
        {
            return (_px + 0.25) / _scale;
        }

        /// <summary>
        /// The index of the screen sharing the most area with <paramref name="_rect"/> (Windows gives a window the DPI of that
        /// monitor); the one containing the top-left corner when none overlaps; -1 for no screens.
        /// </summary>
        public static int MostOverlapping(PhysicalRect _rect, IReadOnlyList<PhysicalRect> _screens)
        {
            int best = -1;
            long bestArea = 0;
            for (int i = 0; i < _screens.Count; i++)
            {
                var part = _screens[i].Intersect(_rect);
                long area = part.IsEmpty ? 0 : (long)part.Width * part.Height;
                if (area > bestArea)
                {
                    best = i;
                    bestArea = area;
                }
            }
            if (best >= 0)
            {
                return best;
            }
            for (int i = 0; i < _screens.Count; i++)
            {
                var s = _screens[i];
                if (_rect.X >= s.X && _rect.X < s.Right && _rect.Y >= s.Y && _rect.Y < s.Bottom)
                {
                    return i;
                }
            }
            return _screens.Count > 0 ? 0 : -1;
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
