using Avalonia;
using Avalonia.Controls;
using Hider.Core;

namespace Hider
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] _args)
        {
            // Command line switches are handled before Avalonia starts so that they work without a display.
            if (_args.Contains("--version"))
            {
                Console.WriteLine($"Hider {AppInfo.Version}");
                return 0;
            }
            if (_args.Contains("--selftest"))
            {
                return SelfTest.Run(Console.Out);
            }

            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(_args, ShutdownMode.OnExplicitShutdown);
        }

        // Also used by the Avalonia previewer.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                // Fixed WM_CLASS so the .desktop entry (StartupWMClass) matches the running window.
                .With(new X11PlatformOptions { WmClass = "Hider" })
                .LogToTrace();
        }
    }
}
