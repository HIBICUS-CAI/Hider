using Hider.Core.Model;
using Hider.Platform;

namespace Hider.Core.Tests.Fakes
{
    public sealed class FakeScreenService : IScreenService
    {
        public FakeScreenService(params ScreenInfo[] _screens)
        {
            Screens = _screens.Length > 0 ? _screens : new[] { OneScreen() };
        }

        public IReadOnlyList<ScreenInfo> Screens { get; set; }
        public PhysicalRect? CursorScreen { get; set; }

        public static ScreenInfo OneScreen(double _scale = 1.0)
        {
            var bounds = new PhysicalRect(0, 0, 1920, 1080);
            return new ScreenInfo(bounds, bounds with { Height = 1040 }, _scale, true);
        }
    }
}
