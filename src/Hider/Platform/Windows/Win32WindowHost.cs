using System.Runtime.Versioning;
using Avalonia.Controls;
using Hider.Core.Model;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// Area window host on Win32. Windows gives a window the DPI of the monitor holding most of it, so the size is worked
    /// out with that monitor's scale (an area across a 150% / 100% border would otherwise get the wrong size), and the move
    /// and the resize go to the window in one SetWindowPos (two would show the old size at the new place for a frame while a
    /// left or top edge is dragged).
    /// </summary>
    [SupportedOSPlatform("windows")]
    public sealed class Win32WindowHost : AvaloniaWindowHost
    {
        public Win32WindowHost(Window _window) : base(_window)
        {
        }

        protected override double ScaleFor(PhysicalRect _rect)
        {
            var screens = Window.Screens.All;
            var bounds = new PhysicalRect[screens.Count];
            for (int i = 0; i < bounds.Length; i++)
            {
                var b = screens[i].Bounds;
                bounds[i] = new PhysicalRect(b.X, b.Y, b.Width, b.Height);
            }
            int index = AreaGeometry.MostOverlapping(_rect, bounds);
            return index >= 0 ? screens[index].Scaling : Window.RenderScaling;
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
