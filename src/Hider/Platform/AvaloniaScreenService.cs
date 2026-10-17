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
        private readonly Func<PixelPoint?>? pointer;

        /// <param name="_screens">Any top level's screens (null: none yet).</param>
        public AvaloniaScreenService(Func<Screens?> _screens)
            : this(_screens, OperatingSystem.IsWindows() ? new Func<PixelPoint?>(Win32Cursor.Position) : null)
        {
        }

        /// <param name="_screens">Any top level's screens (null: none yet).</param>
        /// <param name="_pointer">
        /// The pointer position in physical pixels where the OS can tell it (Windows), null for a position it cannot tell
        /// now; null where the pointer is never asked.
        /// </param>
        internal AvaloniaScreenService(Func<Screens?> _screens, Func<PixelPoint?>? _pointer)
        {
            screens = _screens;
            pointer = _pointer;
        }

        public IReadOnlyList<ScreenInfo> Screens => (screens()?.All ?? Array.Empty<Screen>()).Select(ToInfo).ToArray();

        public PhysicalRect? CursorScreen
        {
            get
            {
                // XWAYLAND-ASSUMPTION: XWayland only knows the pointer while it is over X11 windows, so the cursor monitor is
                // not reliable on GNOME Wayland; Linux asks no pointer and new areas go to the primary screen there.
                if (pointer?.Invoke() is not { } point)
                {
                    return null;
                }
                var screen = screens()?.ScreenFromPoint(point);
                return screen is null ? null : ToRect(screen.Bounds);
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
