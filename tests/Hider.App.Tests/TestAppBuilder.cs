using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(Hider.App.Tests.TestAppBuilder))]

namespace Hider.App.Tests
{
    public static class TestAppBuilder
    {
        // Real Skia drawing (not the headless no-op renderer) so tests can compare rendered pixels.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<HiderApp>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        }
    }
}
