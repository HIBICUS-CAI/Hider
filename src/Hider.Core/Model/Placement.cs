namespace Hider.Core.Model
{
    /// <summary>Where areas go on screen. Screens are given as their bounds in physical pixels (not the work areas).</summary>
    public static class Placement
    {
        /// <summary>
        /// Restore rule (FR-63): a rectangle that touches any screen stays where it is, cut to the screens' bounding box when
        /// it is larger than that box. One that touches none is moved to the centre of the primary screen, shrunk to fit it
        /// first when it is larger.
        /// </summary>
        // XWAYLAND-ASSUMPTION: relies on knowing absolute screen coordinates of every monitor.
        public static PhysicalRect Fit(PhysicalRect _rect, IReadOnlyList<PhysicalRect> _screens, int _primaryIndex)
        {
            var rect = _rect.Normalized();
            if (_screens.Count == 0)
            {
                return rect;
            }
            if (_screens.Any(_s => _s.Intersects(rect)))
            {
                var all = _screens.Aggregate((_a, _b) => _a.Union(_b));
                return rect.Width > all.Width || rect.Height > all.Height ? rect.Intersect(all) : rect;
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
