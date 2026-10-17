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
            var (output, error) = Choose(IsRedirected(StdOutputHandle), IsRedirected(StdErrorHandle), () => AttachConsole(AttachParentProcess), OpenConsole);
            if (output is not null)
            {
                Console.SetOut(output);
            }
            if (error is not null)
            {
                Console.SetError(error);
            }
        }

        /// <summary>
        /// The writers for standard output and standard error, null for a stream that stays as it is. Both stay when both are
        /// redirected (the console is then not even attached), when there is no console to attach to (started from Explorer
        /// or a shortcut) and when the console cannot be opened (then only the exit code tells the result). Otherwise every
        /// stream that is not redirected gets the one console writer, which first ends the line of the shell's prompt.
        /// </summary>
        /// <param name="_outputRedirected">Standard output goes to a pipe or a file.</param>
        /// <param name="_errorRedirected">Standard error goes to a pipe or a file.</param>
        /// <param name="_attach">Attaches to the parent's console; false when there is none.</param>
        /// <param name="_open">Opens the attached console for writing.</param>
        internal static (TextWriter? Output, TextWriter? Error) Choose(bool _outputRedirected, bool _errorRedirected, Func<bool> _attach, Func<TextWriter> _open)
        {
            if ((_outputRedirected && _errorRedirected) || !_attach())
            {
                return (null, null);
            }
            TextWriter writer;
            try
            {
                writer = _open();
                writer.WriteLine(); // the shell has printed its prompt already
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (null, null);
            }
            return (_outputRedirected ? null : writer, _errorRedirected ? null : writer);
        }

        private static TextWriter OpenConsole()
        {
            var console = new FileStream("CONOUT$", FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
            return new StreamWriter(console) { AutoFlush = true };
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
