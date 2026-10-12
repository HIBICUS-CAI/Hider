using Avalonia;
using Avalonia.Controls;
using Hider.Core.Model;

namespace Hider.Platform
{
    /// <summary>
    /// <see cref="IAreaWindowHost"/> on Avalonia's window API: position in physical pixels (Window.Position), size in DIP for
    /// the screen the window goes to (<see cref="ScaleFor"/>). Shared by the Win32 and X11 hosts.
    /// </summary>
    public class AvaloniaWindowHost : IAreaWindowHost
    {
        private readonly Window window;
        private double? placedScale;

        public AvaloniaWindowHost(Window _window)
        {
            window = _window;
            window.ScalingChanged += (_, _) => DisplayChanged?.Invoke();
            window.Screens.Changed += (_, _) => DisplayChanged?.Invoke();
        }

        public event Action? DisplayChanged;

        public nint Handle => window.TryGetPlatformHandle()?.Handle ?? 0;

        /// <summary>The scale the window was last placed with (its size in DIP was worked out with it).</summary>
        public double Scale => placedScale ?? ScaleAt(window.Position);

        protected Window Window => window;

        public void Place(PhysicalRect _rect)
        {
            double scale = ScaleFor(_rect);
            placedScale = scale;
            PlaceNative(_rect);
            window.Position = new PixelPoint(_rect.X, _rect.Y);
            window.Width = AreaGeometry.DipLength(_rect.Width, scale);
            window.Height = AreaGeometry.DipLength(_rect.Height, scale);
        }

        public void SetTopmost(bool _on)
        {
            window.Topmost = _on;
        }

        /// <summary>The scale the window gets at <paramref name="_rect"/>: by default that of the screen at its top-left corner.</summary>
        protected virtual double ScaleFor(PhysicalRect _rect)
        {
            return ScaleAt(new PixelPoint(_rect.X, _rect.Y));
        }

        /// <summary>Moves and sizes the native window in one step before Avalonia applies the same values (optional).</summary>
        protected virtual void PlaceNative(PhysicalRect _rect)
        {
        }

        private double ScaleAt(PixelPoint _point)
        {
            var screen = window.Screens.ScreenFromPoint(_point) ?? window.Screens.Primary;
            return screen?.Scaling ?? window.RenderScaling;
        }
    }
}
