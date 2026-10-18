using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;

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

        /// <summary>The cursor position in physical pixels; null when the system cannot tell it (e.g. a locked session).</summary>
        [SupportedOSPlatform("windows")]
        public static PixelPoint? Position()
        {
            return TryGetPosition(out int x, out int y) ? new PixelPoint(x, y) : null;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool GetCursorPos(out Point _point);
    }
}
