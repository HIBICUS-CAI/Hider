using Avalonia;
using Avalonia.Controls;
using Hider.Core.Model;
using Hider.Platform;

namespace Hider.Core.Tests.Fakes
{
    /// <summary>
    /// <see cref="IAreaWindowHost"/> with a fixed scale that records every placement and applies it to the window the way
    /// the real hosts do (position in physical pixels, size in DIP), so layout and GUI tests run headless.
    /// </summary>
    public sealed class FakeAreaWindowHost : IAreaWindowHost
    {
        private readonly Window window;

        public FakeAreaWindowHost(Window _window, double _scale)
        {
            window = _window;
            Scale = _scale;
        }

        public nint Handle => 0;
        public double Scale { get; }
        public List<PhysicalRect> Placements { get; } = new();
        public bool Topmost { get; private set; } = true;
        public PhysicalRect Last => Placements[^1];

        public void Place(PhysicalRect _rect)
        {
            Placements.Add(_rect);
            window.Position = new PixelPoint(_rect.X, _rect.Y);
            window.Width = _rect.Width / Scale;
            window.Height = _rect.Height / Scale;
        }

        public void SetTopmost(bool _on)
        {
            Topmost = _on;
        }
    }
}
