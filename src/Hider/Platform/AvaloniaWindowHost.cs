using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
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
        private bool scalingChanged;
        private bool screensChanged;
        private bool displayChangePosted;

        public AvaloniaWindowHost(Window _window)
        {
            window = _window;
            window.ScalingChanged += OnScalingChanged;
            // Screens is backed by one screen implementation shared by the whole process: unsubscribed in Dispose.
            window.Screens.Changed += OnScreensChanged;
        }

        public void Dispose()
        {
            window.ScalingChanged -= OnScalingChanged;
            window.Screens.Changed -= OnScreensChanged;
            DisplayChanged = null;
            GC.SuppressFinalize(this);
        }

        private void OnScalingChanged(object? _sender, EventArgs _e) => QueueDisplayChanged(true);

        private void OnScreensChanged(object? _sender, EventArgs _e) => QueueDisplayChanged(false);

        public event Action? DisplayChanged;

        public nint Handle => window.TryGetPlatformHandle()?.Handle ?? 0;

        /// <summary>The scale the window was last placed with (its size in DIP was worked out with it).</summary>
        public double Scale => placedScale ?? ScaleAt(window.Position);

        protected Window Window => window;

        public void Place(PhysicalRect _rect)
        {
            double scale = ScaleFor(_rect);
            placedScale = scale;
            int width = WindowLength(_rect.Width, scale), height = WindowLength(_rect.Height, scale);
            PlaceNative(new PhysicalRect(_rect.X, _rect.Y, width, height));
            window.Position = new PixelPoint(_rect.X, _rect.Y);
            window.Width = DipLength(width, scale);
            window.Height = DipLength(height, scale);
        }

        public void SetTopmost(bool _on)
        {
            window.Topmost = _on;
        }

        /// <summary>The scale the window gets at <paramref name="_rect"/>: by default that of the screen at its top-left corner.</summary>
        public virtual double ScaleFor(PhysicalRect _rect)
        {
            return ScaleAt(new PixelPoint(_rect.X, _rect.Y));
        }

        /// <summary>The window length in physical pixels for an area length (at least as long: the window may cover more).</summary>
        protected virtual int WindowLength(int _px, double _scale)
        {
            return _px;
        }

        /// <summary>The DIP length Avalonia turns back into <paramref name="_px"/> physical pixels at <paramref name="_scale"/>.</summary>
        protected virtual double DipLength(int _px, double _scale)
        {
            return AreaGeometry.DipLength(_px, _scale);
        }

        /// <summary>Moves and sizes the native window in one step before Avalonia applies the same values (optional).</summary>
        protected virtual void PlaceNative(PhysicalRect _rect)
        {
        }

        /// <summary>
        /// Avalonia raises ScalingChanged before it applies the system's suggested rectangle (Win32 WM_DPICHANGED), and from
        /// inside Place when the move itself changes the DPI. The new layout therefore runs once the current message is done,
        /// and not at all for a scale the window was already placed with (the change Place itself caused).
        /// </summary>
        internal void QueueDisplayChanged(bool _scaling)
        {
            if (_scaling)
            {
                scalingChanged = true;
            }
            else
            {
                screensChanged = true;
            }
            if (displayChangePosted)
            {
                return;
            }
            displayChangePosted = true;
            Dispatcher.UIThread.Post(() =>
            {
                displayChangePosted = false;
                bool notify = screensChanged || (scalingChanged && (placedScale is not { } placed || Math.Abs(window.RenderScaling - placed) > 1e-6));
                scalingChanged = screensChanged = false;
                if (notify)
                {
                    DisplayChanged?.Invoke();
                }
            });
        }

        private double ScaleAt(PixelPoint _point)
        {
            var screen = window.Screens.ScreenFromPoint(_point) ?? window.Screens.Primary;
            return screen?.Scaling ?? window.RenderScaling;
        }
    }
}
