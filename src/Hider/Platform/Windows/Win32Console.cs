using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// Hider is a GUI-subsystem executable (WinExe), so a console that starts it gives it no standard output. For the
    /// command line switches (--version, --selftest, --verbose) it attaches to the parent's console, unless the output is
    /// already redirected (a pipe, a file, CI). cmd still does not wait for a GUI program: use start /wait (README).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class Win32Console
    {
        private const int StdOutputHandle = -11;
        private const uint AttachParentProcess = 0xFFFFFFFF;

        public static void AttachToParent()
        {
            try
            {
                nint output = GetStdHandle(StdOutputHandle);
                if (output != 0 && output != -1)
                {
                    return; // redirected: keep it
                }
                if (!AttachConsole(AttachParentProcess))
                {
                    return; // started without a console (Explorer, a shortcut)
                }
                var console = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(console) { AutoFlush = true };
                writer.WriteLine(); // the shell has printed its prompt already
                Console.SetOut(writer);
                Console.SetError(writer);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No console output then; the exit code still tells the result.
            }
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint GetStdHandle(int _handle);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AttachConsole(uint _processId);
    }
}
