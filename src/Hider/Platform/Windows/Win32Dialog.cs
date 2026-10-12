using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.Platform.Windows
{
    /// <summary>A system message box, for errors before Avalonia starts (a GUI program started from Explorer has no console).</summary>
    [SupportedOSPlatform("windows")]
    internal static partial class Win32Dialog
    {
        private const uint MbOk = 0x0, MbIconError = 0x10, MbSetForeground = 0x10000;

        public static void ShowError(string _text, string _caption)
        {
            _ = MessageBoxW(0, _text, _caption, MbOk | MbIconError | MbSetForeground);
        }

        [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial int MessageBoxW(nint _owner, string _text, string _caption, uint _type);
    }
}
