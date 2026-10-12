using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Platform.Windows;

namespace Hider
{
    /// <summary>
    /// Platform checks added to "--selftest" (run by CI on the trimmed binary): the native code paths that the Core checks
    /// cannot reach. Windows: window enumeration and one composed capture of the desktop corner (LibraryImport, function
    /// pointer callback, DWM attributes, PrintWindow) must run without throwing. Linux: a libhidercap.so shipped next to the
    /// executable must load and answer (packaging and LibraryImport after trimming).
    /// </summary>
    internal static class PlatformSelfTest
    {
        public static int Run(TextWriter _output)
        {
            if (OperatingSystem.IsLinux())
            {
                return RunLinux(_output);
            }
            if (!OperatingSystem.IsWindows())
            {
                return 0;
            }
            try
            {
                var source = new Win32WindowSource();
                var windows = source.EnumerateTopDown();
                var result = new UnderlayComposer(source).Compose(new CaptureRequest(new PhysicalRect(0, 0, 64, 64), Array.Empty<nint>(), 0));
                _output.WriteLine($"[ok]   win32 capture: {windows.Count} window(s), result {result.Failure}");
                return 0;
            }
            catch (Exception e)
            {
                _output.WriteLine($"[fail] win32 capture: {e}");
                return 1;
            }
        }

        [SupportedOSPlatform("linux")]
        private static int RunLinux(TextWriter _output)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "libhidercap.so");
            if (!File.Exists(path))
            {
                _output.WriteLine("[skip] libhidercap.so not shipped: the Screenshot portal is used (D-6)");
                return 0;
            }
            try
            {
                var helper = new Hider.Platform.Linux.Hidercap();
                if (!helper.IsAvailable)
                {
                    _output.WriteLine("[fail] libhidercap.so is shipped but does not load (is libpipewire-0.3 installed?)");
                    return 1;
                }
                _output.WriteLine($"[ok]   libhidercap: loaded, last sequence {helper.LastSequence()}");
                return 0;
            }
            catch (Exception e)
            {
                _output.WriteLine($"[fail] libhidercap: {e.GetType().Name}: {e.Message} (an old libhidercap.so?)");
                return 1;
            }
        }
    }
}
