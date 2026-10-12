using Hider.Core.Model;

namespace Hider.Platform
{
    /// <summary>
    /// The OS-specific side of an area window (work instructions §3.3, §6.1). Only implementations of this interface touch
    /// Avalonia's Position / Width / Height / Topmost / platform handle, and they are the one place where physical pixels and
    /// DIP are converted. A future Wayland host (GNOME Shell extension) replaces the implementation, not the callers.
    /// </summary>
    public interface IAreaWindowHost
    {
        /// <summary>Native window handle (HWND / X11 window id), used to exclude Hider's own windows from capture.</summary>
        nint Handle { get; }

        /// <summary>Physical pixels per DIP for the window's current screen.</summary>
        double Scale { get; }

        /// <summary>Physical pixels per DIP a window gets at <paramref name="_rect"/> (the scale of the screen the system puts it on).</summary>
        double ScaleFor(PhysicalRect _rect);

        /// <summary>
        /// Moves and sizes the window: position in physical pixels, size converted to DIP for the target screen. The window may
        /// come out a pixel or two longer than <paramref name="_rect"/> where the platform could not keep the exact length (it never
        /// comes out shorter).
        /// </summary>
        void Place(PhysicalRect _rect);

        void SetTopmost(bool _on);

        /// <summary>The window's scale or the monitor layout changed (DPI change, monitor plugged or unplugged): lay out again.</summary>
        event Action? DisplayChanged;
    }

    /// <summary>Physical pixels of the window layout expressed in DIP via the host scale.</summary>
    public static class AreaWindowHostExtensions
    {
        public static double PxToDip(this IAreaWindowHost _host, int _px)
        {
            return _px / _host.Scale;
        }
    }
}
