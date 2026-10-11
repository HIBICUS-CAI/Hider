using Hider.Core.Model;

namespace Hider.Core.Capture
{
    /// <summary>A top-level window as the Windows capture sees it (spec §6.2).</summary>
    /// <param name="WindowRect">GetWindowRect (physical px): the area PrintWindow draws, including invisible resize borders.</param>
    /// <param name="FrameBounds">DWMWA_EXTENDED_FRAME_BOUNDS (physical px): the visible frame.</param>
    public sealed record WindowInfo(nint Handle, PhysicalRect WindowRect, PhysicalRect FrameBounds, bool Visible, bool Cloaked, string ClassName);

    /// <summary>One window's PrintWindow result: an image of its <see cref="WindowInfo.WindowRect"/>, or why there is none.</summary>
    public sealed record WindowImage(FrameBuffer? Image, CaptureFailure Failure);

    /// <summary>Source of windows and their images; Win32 in the app, a fake list in tests.</summary>
    public interface IWindowSource
    {
        /// <summary>Top-level windows in Z order, topmost first (EnumWindows order).</summary>
        IReadOnlyList<WindowInfo> EnumerateTopDown();

        /// <summary>PrintWindow(PW_RENDERFULLCONTENT) of a window, giving up after <paramref name="_timeout"/>.</summary>
        WindowImage Capture(WindowInfo _window, TimeSpan _timeout);
    }

    /// <summary>
    /// Builds the image under an area from the windows below it (spec §6.2, design §5.2), as a pure function over
    /// <see cref="IWindowSource"/>. Windows are walked from the top: only visible, non-cloaked, non-empty windows that are below
    /// the area's own window, are not Hider windows and intersect the area are captured, and a window is skipped when the
    /// windows above it already cover its part. Pixels come from inside each window's visible frame only.
    /// </summary>
    public sealed class UnderlayComposer
    {
        public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(250);

        private readonly IWindowSource source;
        private HashSet<nint> skipNextTime = new();

        public UnderlayComposer(IWindowSource _source, TimeSpan? _timeout = null)
        {
            source = _source;
            Timeout = _timeout ?? DefaultTimeout;
        }

        public TimeSpan Timeout { get; }

        /// <summary>Windows captured by the last call (for tests and logs).</summary>
        public IReadOnlyList<nint> LastCaptured { get; private set; } = Array.Empty<nint>();

        /// <summary>
        /// Captures the area. <see cref="CaptureRequest.ExcludeWindows"/>[0] is the area's own window (windows above it are
        /// already visible and are not captured); the rest are Hider's other windows. A window that times out fails this capture
        /// with <see cref="CaptureFailure.Timeout"/> and is skipped next time; any black window fails it with
        /// <see cref="CaptureFailure.Black"/> (FR-52, conservative).
        /// </summary>
        public CaptureResult Compose(CaptureRequest _request, FrameBuffer? _target = null)
        {
            var area = _request.Rect;
            if (area.IsEmpty)
            {
                return CaptureResult.Failed(CaptureFailure.Unavailable);
            }
            var windows = source.EnumerateTopDown();
            var exclude = new HashSet<nint>(_request.ExcludeWindows);
            int start = 0;
            if (_request.ExcludeWindows.Count > 0)
            {
                int own = IndexOf(windows, _request.ExcludeWindows[0]);
                start = own < 0 ? 0 : own + 1;
            }

            var frame = _target ?? new FrameBuffer();
            frame.Resize(area.Width, area.Height);
            var pixels = frame.Bgra.Span;
            pixels.Slice(0, area.Width * area.Height * 4).Clear();
            var covered = new bool[area.Width * area.Height];
            int remaining = covered.Length;
            var skip = skipNextTime;
            skipNextTime = new HashSet<nint>();
            var captured = new List<nint>();
            var failure = CaptureFailure.None;

            for (int i = start; i < windows.Count && remaining > 0; i++)
            {
                var w = windows[i];
                if (exclude.Contains(w.Handle) || !w.Visible || w.Cloaked || w.FrameBounds.IsEmpty || w.WindowRect.IsEmpty)
                {
                    continue;
                }
                var part = w.FrameBounds.Intersect(w.WindowRect).Intersect(area);
                if (part.IsEmpty || !HasUncovered(covered, area, part))
                {
                    continue;
                }
                if (skip.Contains(w.Handle))
                {
                    continue;
                }
                var image = source.Capture(w, Timeout);
                captured.Add(w.Handle);
                if (image.Failure == CaptureFailure.Timeout)
                {
                    skipNextTime.Add(w.Handle);
                    failure = CaptureFailure.Timeout;
                    break;
                }
                if (image.Failure != CaptureFailure.None || image.Image is null || IsBlack(image.Image))
                {
                    failure = image.Failure == CaptureFailure.None ? CaptureFailure.Black : image.Failure;
                    break;
                }
                remaining -= CopyUncovered(image.Image, w.WindowRect, part, area, pixels, covered);
            }
            LastCaptured = captured;
            return failure == CaptureFailure.None ? new CaptureResult(frame, CaptureFailure.None) : CaptureResult.Failed(failure);
        }

        /// <summary>Every pixel's RGB is 0 (what PrintWindow returns for content it cannot read).</summary>
        public static bool IsBlack(FrameBuffer _image)
        {
            var px = _image.Bgra.Span;
            for (int y = 0; y < _image.Height; y++)
            {
                var row = px.Slice(y * _image.Stride, _image.Width * 4);
                for (int x = 0; x < row.Length; x += 4)
                {
                    if (row[x] != 0 || row[x + 1] != 0 || row[x + 2] != 0)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static int IndexOf(IReadOnlyList<WindowInfo> _windows, nint _handle)
        {
            for (int i = 0; i < _windows.Count; i++)
            {
                if (_windows[i].Handle == _handle)
                {
                    return i;
                }
            }
            return -1;
        }

        private static bool HasUncovered(bool[] _covered, PhysicalRect _area, PhysicalRect _part)
        {
            for (int y = _part.Y; y < _part.Bottom; y++)
            {
                int row = (y - _area.Y) * _area.Width - _area.X;
                for (int x = _part.X; x < _part.Right; x++)
                {
                    if (!_covered[row + x])
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>Copies the window's pixels of <paramref name="_part"/> that nothing above has covered; returns how many.</summary>
        private static int CopyUncovered(FrameBuffer _image, PhysicalRect _windowRect, PhysicalRect _part, PhysicalRect _area, Span<byte> _dst, bool[] _covered)
        {
            int copied = 0;
            var src = _image.Bgra.Span;
            for (int y = _part.Y; y < _part.Bottom; y++)
            {
                int sy = y - _windowRect.Y;
                if (sy < 0 || sy >= _image.Height)
                {
                    continue;
                }
                int dstRow = (y - _area.Y) * _area.Width;
                for (int x = _part.X; x < _part.Right; x++)
                {
                    int sx = x - _windowRect.X;
                    int d = dstRow + x - _area.X;
                    if (_covered[d] || sx < 0 || sx >= _image.Width)
                    {
                        continue;
                    }
                    int si = sy * _image.Stride + sx * 4;
                    int di = d * 4;
                    _dst[di] = src[si];
                    _dst[di + 1] = src[si + 1];
                    _dst[di + 2] = src[si + 2];
                    _dst[di + 3] = 255;
                    _covered[d] = true;
                    copied++;
                }
            }
            return copied;
        }
    }
}
