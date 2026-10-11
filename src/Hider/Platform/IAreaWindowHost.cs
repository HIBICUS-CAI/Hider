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

        /// <summary>Moves and sizes the window: position in physical pixels, size converted to DIP for the target screen.</summary>
        void Place(PhysicalRect _rect);

        void SetTopmost(bool _on);
    }

    /// <summary>DIP lengths used by the window layout (edit margins, toolbar) expressed in physical pixels via the host scale.</summary>
    public static class AreaWindowHostExtensions
    {
        public static int DipToPx(this IAreaWindowHost _host, double _dip)
        {
            return (int)Math.Ceiling(_dip * _host.Scale - 1e-9);
        }

        public static double PxToDip(this IAreaWindowHost _host, int _px)
        {
            return _px / _host.Scale;
        }
    }
}
