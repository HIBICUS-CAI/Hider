using System.Runtime.Versioning;
using Avalonia.Controls;
using Hider.Core.Model;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// Area window host on Win32.
    /// <list type="bullet">
    /// <item>Windows gives a window the DPI of the monitor MonitorFromRect picks for it (the one holding most of it), so the
    /// size is worked out with that monitor's DPI, asked from the system with the same rule.</item>
    /// <item>Avalonia keeps the client size as px / scale (WM_SIZE) and sends (int)(width × scale) back (Resize), so a
    /// length is only stable if it survives that round trip: the window takes the shortest such length not shorter than the
    /// area (175%: 61 px becomes 62, never 60).</item>
    /// <item>The move and the resize go to the window in one SetWindowPos (two would show the old size at the new place for
    /// a frame while a left or top edge is dragged).</item>
    /// </list>
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class Win32WindowHost : AvaloniaWindowHost
    {
        public Win32WindowHost(Window _window) : base(_window)
        {
        }

        public override double ScaleFor(PhysicalRect _rect)
        {
            var r = new Win32Native.Rect { Left = _rect.X, Top = _rect.Y, Right = _rect.Right, Bottom = _rect.Bottom };
            nint monitor = Win32Native.MonitorFromRect(r, Win32Native.MONITOR_DEFAULTTONEAREST);
            return monitor != 0 && Win32Native.GetDpiForMonitor(monitor, Win32Native.MDT_EFFECTIVE_DPI, out uint dpi, out _) == 0 && dpi > 0
                ? dpi / 96.0
                : Window.RenderScaling;
        }

        protected override int WindowLength(int _px, double _scale)
        {
            return AreaGeometry.StableLength(_px, _scale);
        }

        protected override double DipLength(int _px, double _scale)
        {
            // Exactly what WM_SIZE stores, so the size Avalonia keeps and the one set here never differ.
            return _px / _scale;
        }

        protected override void PlaceNative(PhysicalRect _rect)
        {
            nint hwnd = Handle;
            if (hwnd != 0)
            {
                // Failure only costs the one-step move: Avalonia applies the same position and size right after.
                _ = Win32Native.SetWindowPos(hwnd, 0, _rect.X, _rect.Y, _rect.Width, _rect.Height,
                    Win32Native.SWP_NOZORDER | Win32Native.SWP_NOOWNERZORDER | Win32Native.SWP_NOACTIVATE);
            }
        }
    }
}
