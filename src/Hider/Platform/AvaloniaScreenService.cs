using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Hider.Core.Model;
using Hider.Platform.Windows;

namespace Hider.Platform
{
    /// <summary>Screens from Avalonia (through any top level) and the pointer position where the OS can tell it.</summary>
    public sealed class AvaloniaScreenService : IScreenService
    {
        private readonly Func<Screens?> screens;

        public AvaloniaScreenService(Func<Screens?> _screens)
        {
            screens = _screens;
        }

        public IReadOnlyList<ScreenInfo> Screens => (screens()?.All ?? Array.Empty<Screen>()).Select(ToInfo).ToArray();

        public PhysicalRect? CursorScreen
        {
            get
            {
                if (OperatingSystem.IsWindows() && Win32Cursor.TryGetPosition(out int x, out int y))
                {
                    var screen = screens()?.ScreenFromPoint(new PixelPoint(x, y));
                    return screen is null ? null : ToRect(screen.Bounds);
                }
                // XWAYLAND-ASSUMPTION: XWayland only knows the pointer while it is over X11 windows, so the cursor monitor is
                // not reliable on GNOME Wayland; new areas go to the primary screen there.
                return null;
            }
        }

        private static ScreenInfo ToInfo(Screen _screen)
        {
            return new ScreenInfo(ToRect(_screen.Bounds), ToRect(_screen.WorkingArea), _screen.Scaling, _screen.IsPrimary);
        }

        private static PhysicalRect ToRect(PixelRect _rect)
        {
            return new PhysicalRect(_rect.X, _rect.Y, _rect.Width, _rect.Height);
        }
    }
}
