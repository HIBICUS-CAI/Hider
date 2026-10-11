using Avalonia;
using Avalonia.Controls;
using Hider.Core.Model;

namespace Hider.Platform
{
    /// <summary>
    /// <see cref="IAreaWindowHost"/> on Avalonia's window API: position in physical pixels (Window.Position), size in DIP for
    /// the screen the window goes to. Shared by the Win32 and X11 hosts.
    /// </summary>
    public class AvaloniaWindowHost : IAreaWindowHost
    {
        protected readonly Window window;

        public AvaloniaWindowHost(Window _window)
        {
            window = _window;
        }

        public nint Handle => window.TryGetPlatformHandle()?.Handle ?? 0;

        public double Scale => ScaleAt(window.Position);

        public virtual void Place(PhysicalRect _rect)
        {
            double scale = ScaleAt(new PixelPoint(_rect.X, _rect.Y));
            window.Position = new PixelPoint(_rect.X, _rect.Y);
            window.Width = _rect.Width / scale;
            window.Height = _rect.Height / scale;
        }

        public void SetTopmost(bool _on)
        {
            window.Topmost = _on;
        }

        private double ScaleAt(PixelPoint _point)
        {
            var screen = window.Screens.ScreenFromPoint(_point) ?? window.Screens.Primary;
            return screen?.Scaling ?? window.RenderScaling;
        }
    }
}
