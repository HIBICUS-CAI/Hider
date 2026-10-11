using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hider.Platform.Windows
{
    /// <summary>user32 / gdi32 / dwmapi calls used by the window capture (design §2.3). LibraryImport only.</summary>
    [SupportedOSPlatform("windows")]
    internal static unsafe partial class Win32Native
    {
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;
        public const uint PW_RENDERFULLCONTENT = 2;
        public const uint DIB_RGB_COLORS = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
        }

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnumWindows(delegate* unmanaged<nint, nint, int> _callback, nint _param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(nint _hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsIconic(nint _hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(nint _hwnd, out Rect _rect);

        [LibraryImport("user32.dll")]
        public static partial int GetClassNameW(nint _hwnd, char* _buffer, int _max);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool PrintWindow(nint _hwnd, nint _hdc, uint _flags);

        [LibraryImport("user32.dll")]
        public static partial nint GetDC(nint _hwnd);

        [LibraryImport("user32.dll")]
        public static partial int ReleaseDC(nint _hwnd, nint _hdc);

        [LibraryImport("gdi32.dll")]
        public static partial nint CreateCompatibleDC(nint _hdc);

        [LibraryImport("gdi32.dll")]
        public static partial nint CreateDIBSection(nint _hdc, BitmapInfoHeader* _info, uint _usage, nint* _bits, nint _section, uint _offset);

        [LibraryImport("gdi32.dll")]
        public static partial nint SelectObject(nint _hdc, nint _object);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(nint _object);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteDC(nint _hdc);

        [LibraryImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GdiFlush();

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmGetWindowAttribute(nint _hwnd, int _attribute, void* _value, int _size);
    }
}
