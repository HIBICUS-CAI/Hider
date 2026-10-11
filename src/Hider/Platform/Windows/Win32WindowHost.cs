using System.Runtime.Versioning;
using Avalonia.Controls;

namespace Hider.Platform.Windows
{
    /// <summary>Area window host on Win32. Uses Avalonia's per-monitor DPI handling as is.</summary>
    [SupportedOSPlatform("windows")]
    public sealed class Win32WindowHost : AvaloniaWindowHost
    {
        public Win32WindowHost(Window _window) : base(_window)
        {
        }
    }
}
