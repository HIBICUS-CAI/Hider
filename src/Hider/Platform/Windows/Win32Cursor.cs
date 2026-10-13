using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.Platform.Windows
{
    internal static partial class Win32Cursor
    {
        private struct Point
        {
            public int X;
            public int Y;
        }

        [SupportedOSPlatform("windows")]
        public static bool TryGetPosition(out int _x, out int _y)
        {
            bool ok = GetCursorPos(out var p);
            _x = p.X;
            _y = p.Y;
            return ok;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out Point _point);
    }
}
