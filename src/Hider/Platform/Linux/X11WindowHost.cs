using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Area window host on X11 (XWayland, D-1).
    /// XWAYLAND-ASSUMPTION: the client chooses its absolute position and Mutter honours _NET_WM_STATE_ABOVE; under native
    /// Wayland this host is replaced (docs/wayland-migration.md).
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class X11WindowHost : AvaloniaWindowHost
    {
        public X11WindowHost(Window _window) : base(_window)
        {
        }
    }
}
