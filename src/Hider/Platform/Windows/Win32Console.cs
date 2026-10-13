using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.Platform.Windows
{
    /// <summary>
    /// Hider is a GUI-subsystem executable (WinExe), so a console that starts it gives it no standard output. The one-shot
    /// switches (--version, --selftest) attach to the parent's console for the streams that are not redirected (a pipe, a
    /// file, CI). The resident app never attaches: Ctrl+C or closing that console would kill it on the spot, losing a pending
    /// save. cmd still does not wait for a GUI program: use start /wait (README).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static partial class Win32Console
    {
        private const int StdOutputHandle = -11;
        private const int StdErrorHandle = -12;
        private const uint AttachParentProcess = 0xFFFFFFFF;

        public static void AttachToParent()
        {
            try
            {
                bool output = IsRedirected(StdOutputHandle), error = IsRedirected(StdErrorHandle);
                if ((output && error) || !AttachConsole(AttachParentProcess))
                {
                    return; // both redirected, or started without a console (Explorer, a shortcut)
                }
                var console = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                var writer = new StreamWriter(console) { AutoFlush = true };
                writer.WriteLine(); // the shell has printed its prompt already
                if (!output)
                {
                    Console.SetOut(writer);
                }
                if (!error)
                {
                    Console.SetError(writer);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // No console output then; the exit code still tells the result.
            }
        }

        private static bool IsRedirected(int _stream)
        {
            nint handle = GetStdHandle(_stream);
            return handle != 0 && handle != -1;
        }

        [LibraryImport("kernel32.dll")]
        private static partial nint GetStdHandle(int _handle);

        [LibraryImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool AttachConsole(uint _processId);
    }
}
