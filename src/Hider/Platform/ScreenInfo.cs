using Hider.Core.Model;

namespace Hider.Platform
{
    /// <summary>A monitor in physical pixels.</summary>
    public sealed record ScreenInfo(PhysicalRect Bounds, PhysicalRect WorkingArea, double Scale, bool IsPrimary);

    /// <summary>Monitors and the pointer position, for placing new and restored areas (FR-13, FR-14, FR-63).</summary>
    public interface IScreenService
    {
        IReadOnlyList<ScreenInfo> Screens { get; }

        /// <summary>Pointer position in physical pixels, or null when it cannot be known (see docs/wayland-migration.md).</summary>
        PhysicalRect? CursorScreen { get; }
    }

    public static class ScreenServiceExtensions
    {
        public static ScreenInfo? Primary(this IScreenService _service)
        {
            var screens = _service.Screens;
            foreach (var screen in screens)
            {
                if (screen.IsPrimary)
                {
                    return screen;
                }
            }
            return screens.Count > 0 ? screens[0] : null;
        }

        /// <summary>The screen containing a point, else the primary screen.</summary>
        public static ScreenInfo? ScreenAt(this IScreenService _service, int _x, int _y)
        {
            return _service.Screens.FirstOrDefault(_s => _s.Bounds.Intersects(new PhysicalRect(_x, _y, 1, 1))) ?? _service.Primary();
        }

        public static int PrimaryIndex(this IScreenService _service)
        {
            int index = _service.Screens.ToList().FindIndex(_s => _s.IsPrimary);
            return index < 0 ? 0 : index;
        }
    }
}
