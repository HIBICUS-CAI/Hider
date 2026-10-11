namespace Hider.Core.Model
{
    /// <summary>Where areas go on screen. Screens are given as physical-pixel rectangles (work areas).</summary>
    public static class Placement
    {
        /// <summary>
        /// Restore rule (FR-63): a rectangle that touches any screen stays as it is. One that touches none is moved to the
        /// centre of the primary screen, shrunk to fit it first when it is larger.
        /// </summary>
        // XWAYLAND-ASSUMPTION: relies on knowing absolute screen coordinates of every monitor.
        public static PhysicalRect Fit(PhysicalRect _rect, IReadOnlyList<PhysicalRect> _screens, int _primaryIndex)
        {
            var rect = _rect.Normalized();
            if (_screens.Count == 0 || _screens.Any(s => s.Intersects(rect)))
            {
                return rect;
            }
            var primary = _screens[Math.Clamp(_primaryIndex, 0, _screens.Count - 1)];
            int width = Math.Min(rect.Width, primary.Width);
            int height = Math.Min(rect.Height, primary.Height);
            return new PhysicalRect(primary.X + (primary.Width - width) / 2, primary.Y + (primary.Height - height) / 2, width, height);
        }

        /// <summary>A new area: 400 x 240 DIP converted with the screen's scale, centred on that screen (FR-14).</summary>
        public static PhysicalRect NewArea(PhysicalRect _screen, double _scale)
        {
            int width = Math.Min((int)Math.Round(AreaDefaults.NewAreaWidthDip * _scale), _screen.Width);
            int height = Math.Min((int)Math.Round(AreaDefaults.NewAreaHeightDip * _scale), _screen.Height);
            return new PhysicalRect(_screen.X + (_screen.Width - width) / 2, _screen.Y + (_screen.Height - height) / 2, width, height);
        }
    }
}
