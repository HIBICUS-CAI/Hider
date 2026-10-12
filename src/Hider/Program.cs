using Avalonia;
using Avalonia.Controls;
using Hider.Core;
using Hider.Core.Persistence;

namespace Hider
{
    internal static class Program
    {
        [STAThread]
        public static int Main(string[] _args)
        {
            // Command line switches are handled before Avalonia starts so that they work without a display.
            if (_args.Length > 0 && OperatingSystem.IsWindows())
            {
                Hider.Platform.Windows.Win32Console.AttachToParent();
            }
            if (_args.Contains("--version"))
            {
                Console.WriteLine($"Hider {AppInfo.Version}");
                return 0;
            }
            if (_args.Contains("--selftest"))
            {
                int core = SelfTest.Run(Console.Out);
                int platform = PlatformSelfTest.Run(Console.Out);
                bool passed = core == 0 && platform == 0;
                Console.WriteLine(passed ? "selftest passed" : "selftest failed");
                return passed ? 0 : 1;
            }
            Log.Enabled = _args.Contains("--verbose");

            // Single instance (FR-08): a second process does nothing and exits.
            using var instance = SingleInstance.TryAcquire(StoragePaths.InstanceLock);
            if (instance is null)
            {
                Log.Verbose("another instance is running");
                return 0;
            }
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(_args, ShutdownMode.OnExplicitShutdown);
        }

        // Also used by the Avalonia previewer.
        // XWAYLAND-ASSUMPTION: Linux runs as an X11 client (D-1); platform selection lives only here.
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<HiderApp>()
                .UsePlatformDetect()
                // Fixed WM_CLASS so the .desktop entry (StartupWMClass) matches the running window.
                .With(new X11PlatformOptions { WmClass = "Hider" })
                .LogToTrace();
        }
    }
}
