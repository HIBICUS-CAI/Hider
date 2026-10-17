using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>Finds, reads and closes the system message box (dialog class #32770) another process shows.</summary>
    [SupportedOSPlatform("windows")]
    internal static partial class MessageBoxes
    {
        private const uint WmClose = 0x0010;
        // The static control that holds a message box's text.
        private const int TextControlId = 0xFFFF;

        /// <summary>
        /// The message box titled <paramref name="_caption"/> of <paramref name="_process"/>, once it is shown. Fails at once
        /// when the process ends without showing one, instead of waiting for the time limit.
        /// </summary>
        public static async Task<nint> WaitForAsync(Process _process, string _caption, CancellationToken _ct)
        {
            while (true)
            {
                nint box = Find(_process.Id, _caption);
                if (box != 0)
                {
                    return box;
                }
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"the process exited with {_process.ExitCode} without showing a message box titled {_caption}");
                }
                await Task.Delay(50, _ct);
            }
        }

        public static unsafe string Text(nint _box)
        {
            char* buffer = stackalloc char[4096];
            int length = GetDlgItemTextW(_box, TextControlId, buffer, 4096);
            return new string(buffer, 0, length);
        }

        /// <summary>Closes it as its × button does (an OK-only box returns IDOK).</summary>
        public static void Close(nint _box)
        {
            if (!PostMessageW(_box, WmClose, 0, 0))
            {
                throw new InvalidOperationException($"PostMessageW failed: error {Marshal.GetLastPInvokeError()}");
            }
        }

        private static nint Find(int _processId, string _caption)
        {
            nint window = 0;
            while ((window = FindWindowExW(0, window, "#32770", _caption)) != 0)
            {
                _ = GetWindowThreadProcessId(window, out uint owner);
                if (owner == (uint)_processId)
                {
                    return window;
                }
            }
            return 0;
        }

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint FindWindowExW(nint _parent, nint _after, string _class, string _title);

        [LibraryImport("user32.dll")]
        private static partial uint GetWindowThreadProcessId(nint _hwnd, out uint _processId);

        [LibraryImport("user32.dll")]
        private static unsafe partial int GetDlgItemTextW(nint _dialog, int _id, char* _buffer, int _max);

        [LibraryImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessageW(nint _hwnd, uint _message, nint _wParam, nint _lParam);
    }
}
