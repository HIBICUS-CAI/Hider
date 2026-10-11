using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// <see cref="IWindowSource"/> on Win32: EnumWindows (Z order, topmost first), visibility / cloaking / DWM frame bounds,
    /// and PrintWindow(PW_RENDERFULLCONTENT), which reads the DWM surface so a window is captured even while Hider covers it
    /// (spec §6.2). PrintWindow can block on a hung window, so it runs on its own task and is abandoned after the timeout.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed unsafe class Win32WindowSource : IWindowSource
    {
        // Reused managed copies of the window bitmaps, per window handle (captures run one at a time).
        private readonly Dictionary<nint, FrameBuffer> images = new();
        // PrintWindow calls per window handle that have not returned yet (a hung window blocks them indefinitely).
        private readonly Dictionary<nint, Task<WindowImage>> inFlight = new();

        public IReadOnlyList<WindowInfo> EnumerateTopDown()
        {
            var handles = new List<nint>();
            var gc = GCHandle.Alloc(handles);
            try
            {
                Win32Native.EnumWindows(&OnWindow, GCHandle.ToIntPtr(gc));
            }
            finally
            {
                gc.Free();
            }
            var result = new List<WindowInfo>(handles.Count);
            foreach (nint hwnd in handles)
            {
                bool visible = Win32Native.IsWindowVisible(hwnd) && !Win32Native.IsIconic(hwnd);
                if (!visible || !Win32Native.GetWindowRect(hwnd, out var rect))
                {
                    continue;
                }
                int cloaked = 0;
                Win32Native.DwmGetWindowAttribute(hwnd, Win32Native.DWMWA_CLOAKED, &cloaked, sizeof(int));
                Win32Native.Rect frame;
                if (Win32Native.DwmGetWindowAttribute(hwnd, Win32Native.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, sizeof(Win32Native.Rect)) != 0)
                {
                    frame = rect;
                }
                result.Add(new WindowInfo(hwnd, ToRect(rect), ToRect(frame), true, cloaked != 0, ClassName(hwnd)));
            }
            return result;
        }

        /// <summary>
        /// One PrintWindow per window at a time: while an earlier, abandoned call for a hung window has not returned, the
        /// window times out at once instead of starting another call. Otherwise every capture (10 per second) would leave a
        /// blocked thread-pool thread, a DC and a DIB section behind, and the late calls would write the shared bitmap cache
        /// while later captures read it.
        /// </summary>
        public WindowImage Capture(WindowInfo _window, TimeSpan _timeout)
        {
            Task<WindowImage> task;
            lock (inFlight)
            {
                if (inFlight.TryGetValue(_window.Handle, out var running) && !running.IsCompleted)
                {
                    return new WindowImage(null, CaptureFailure.Timeout);
                }
                task = Task.Run(() => PrintWindow(_window));
                inFlight[_window.Handle] = task;
            }
            if (!task.Wait(_timeout))
            {
                return new WindowImage(null, CaptureFailure.Timeout);
            }
            return task.Result;
        }

        private WindowImage PrintWindow(WindowInfo _window)
        {
            int w = _window.WindowRect.Width, h = _window.WindowRect.Height;
            nint screen = Win32Native.GetDC(0);
            nint dc = Win32Native.CreateCompatibleDC(screen);
            var info = new Win32Native.BitmapInfoHeader { Size = (uint)sizeof(Win32Native.BitmapInfoHeader), Width = w, Height = -h, Planes = 1, BitCount = 32 };
            nint bits;
            nint bitmap = Win32Native.CreateDIBSection(dc, &info, Win32Native.DIB_RGB_COLORS, &bits, 0, 0);
            try
            {
                if (bitmap == 0)
                {
                    return new WindowImage(null, CaptureFailure.Unavailable);
                }
                nint old = Win32Native.SelectObject(dc, bitmap);
                bool ok = Win32Native.PrintWindow(_window.Handle, dc, Win32Native.PW_RENDERFULLCONTENT);
                Win32Native.GdiFlush();
                Win32Native.SelectObject(dc, old);
                if (!ok)
                {
                    return new WindowImage(null, CaptureFailure.Black);
                }
                FrameBuffer image;
                lock (images)
                {
                    if (!images.TryGetValue(_window.Handle, out image!))
                    {
                        image = new FrameBuffer();
                        images[_window.Handle] = image;
                    }
                }
                image.Resize(w, h);
                new ReadOnlySpan<byte>((void*)bits, w * h * 4).CopyTo(image.Bgra.Span);
                return new WindowImage(image, CaptureFailure.None);
            }
            finally
            {
                if (bitmap != 0)
                {
                    Win32Native.DeleteObject(bitmap);
                }
                Win32Native.DeleteDC(dc);
                Win32Native.ReleaseDC(0, screen);
            }
        }

        /// <summary>Drops cached bitmaps of windows that no longer exist.</summary>
        public void Trim(IEnumerable<nint> _alive)
        {
            var alive = new HashSet<nint>(_alive);
            lock (images)
            {
                foreach (nint handle in images.Keys.Where(h => !alive.Contains(h)).ToArray())
                {
                    images.Remove(handle);
                }
            }
            lock (inFlight)
            {
                foreach (nint handle in inFlight.Keys.Where(h => !alive.Contains(h) && inFlight[h].IsCompleted).ToArray())
                {
                    inFlight.Remove(handle);
                }
            }
        }

        [UnmanagedCallersOnly]
        private static int OnWindow(nint _hwnd, nint _param)
        {
            ((List<nint>)GCHandle.FromIntPtr(_param).Target!).Add(_hwnd);
            return 1;
        }

        private static string ClassName(nint _hwnd)
        {
            char* buffer = stackalloc char[256];
            int n = Win32Native.GetClassNameW(_hwnd, buffer, 256);
            return new string(buffer, 0, Math.Max(0, n));
        }

        private static PhysicalRect ToRect(Win32Native.Rect _r)
        {
            return new PhysicalRect(_r.Left, _r.Top, _r.Right - _r.Left, _r.Bottom - _r.Top);
        }
    }
}
