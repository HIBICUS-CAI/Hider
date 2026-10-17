using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>
    /// The Win32 declarations: struct layouts and constants are checked on every OS (they are plain data that must match
    /// the Windows headers); the calls themselves run on Windows only.
    /// </summary>
    [SupportedOSPlatform("windows")] // the declarations are Windows-only code; the layout checks run anywhere
    public class Win32NativeTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        [Fact]
        public void Rect_MatchesRECT()
        {
            // typedef struct tagRECT { LONG left, top, right, bottom; } RECT;
            Assert.Equal(16, Marshal.SizeOf<Win32Native.Rect>());
            Assert.Equal(16, Unsafe.SizeOf<Win32Native.Rect>());
            Assert.Equal(0, (int)Marshal.OffsetOf<Win32Native.Rect>(nameof(Win32Native.Rect.Left)));
            Assert.Equal(4, (int)Marshal.OffsetOf<Win32Native.Rect>(nameof(Win32Native.Rect.Top)));
            Assert.Equal(8, (int)Marshal.OffsetOf<Win32Native.Rect>(nameof(Win32Native.Rect.Right)));
            Assert.Equal(12, (int)Marshal.OffsetOf<Win32Native.Rect>(nameof(Win32Native.Rect.Bottom)));
        }

        [Fact]
        public void BitmapInfoHeader_MatchesBITMAPINFOHEADER()
        {
            // DWORD biSize; LONG biWidth, biHeight; WORD biPlanes, biBitCount; DWORD biCompression, biSizeImage;
            // LONG biXPelsPerMeter, biYPelsPerMeter; DWORD biClrUsed, biClrImportant;  (40 bytes)
            Assert.Equal(40, Marshal.SizeOf<Win32Native.BitmapInfoHeader>());
            Assert.Equal(40, Unsafe.SizeOf<Win32Native.BitmapInfoHeader>());
            (string Field, int Offset)[] fields =
            {
                (nameof(Win32Native.BitmapInfoHeader.Size), 0),
                (nameof(Win32Native.BitmapInfoHeader.Width), 4),
                (nameof(Win32Native.BitmapInfoHeader.Height), 8),
                (nameof(Win32Native.BitmapInfoHeader.Planes), 12),
                (nameof(Win32Native.BitmapInfoHeader.BitCount), 14),
                (nameof(Win32Native.BitmapInfoHeader.Compression), 16),
                (nameof(Win32Native.BitmapInfoHeader.SizeImage), 20),
                (nameof(Win32Native.BitmapInfoHeader.XPelsPerMeter), 24),
                (nameof(Win32Native.BitmapInfoHeader.YPelsPerMeter), 28),
                (nameof(Win32Native.BitmapInfoHeader.ClrUsed), 32),
                (nameof(Win32Native.BitmapInfoHeader.ClrImportant), 36),
            };
            foreach (var (field, offset) in fields)
            {
                Assert.Equal(offset, (int)Marshal.OffsetOf<Win32Native.BitmapInfoHeader>(field));
            }
        }

        [Fact]
        public void Constants_MatchTheWindowsHeaders()
        {
            Assert.Equal(9, Win32Native.DWMWA_EXTENDED_FRAME_BOUNDS);   // dwmapi.h
            Assert.Equal(14, Win32Native.DWMWA_CLOAKED);
            Assert.Equal(2u, Win32Native.PW_RENDERFULLCONTENT);         // winuser.h
            Assert.Equal(0u, Win32Native.DIB_RGB_COLORS);               // wingdi.h
            Assert.Equal(0x0004u, Win32Native.SWP_NOZORDER);
            Assert.Equal(0x0010u, Win32Native.SWP_NOACTIVATE);
            Assert.Equal(0x0200u, Win32Native.SWP_NOOWNERZORDER);
            Assert.Equal(2u, Win32Native.MONITOR_DEFAULTTONEAREST);
            Assert.Equal(0, Win32Native.MDT_EFFECTIVE_DPI);             // shellscalingapi.h
        }

        [Fact(Skip = "Windows 専用（user32 / gdi32 / shcore の呼び出し）", SkipUnless = nameof(IsWindows))]
        public void Calls_OnNoWindow_FailWithoutThrowing()
        {
            // The declarations bind (LibraryImport entry points exist) and a bogus handle fails cleanly. (The pointer taking
            // ones, GetClassNameW / DwmGetWindowAttribute / CreateDIBSection, run through Win32WindowSource in Win32CaptureTests.)
            const nint bogus = 0x7FFFFFF0;
            Assert.False(Win32Native.IsWindowVisible(bogus));
            Assert.False(Win32Native.IsIconic(bogus));
            Assert.False(Win32Native.GetWindowRect(bogus, out _));
            Assert.False(Win32Native.PrintWindow(bogus, 0, Win32Native.PW_RENDERFULLCONTENT));
            Assert.False(Win32Native.SetWindowPos(bogus, 0, 0, 0, 10, 10, Win32Native.SWP_NOZORDER | Win32Native.SWP_NOACTIVATE));
            Assert.False(Win32Native.DeleteObject(0));
            Assert.False(Win32Native.DeleteDC(0));
        }

        [Fact(Skip = "Windows 専用（user32 / gdi32 / shcore の呼び出し）", SkipUnless = nameof(IsWindows))]
        public void Calls_ScreenAndMemoryDc_Work()
        {
            nint screen = Win32Native.GetDC(0);
            Assert.NotEqual(0, screen);
            nint dc = Win32Native.CreateCompatibleDC(screen);
            Assert.NotEqual(0, dc);
            Assert.True(Win32Native.GdiFlush());
            Assert.True(Win32Native.DeleteDC(dc));
            Assert.Equal(1, Win32Native.ReleaseDC(0, screen));
        }

        [Fact(Skip = "Windows 専用（user32 / gdi32 / shcore の呼び出し）", SkipUnless = nameof(IsWindows))]
        public void Calls_MonitorAndDpi_AnswerForAnyRect()
        {
            // MONITOR_DEFAULTTONEAREST always finds a monitor, even far off every screen.
            foreach (var rect in new[] { new Win32Native.Rect { Right = 10, Bottom = 10 }, new Win32Native.Rect { Left = -90000, Top = -90000, Right = -89990, Bottom = -89990 } })
            {
                nint monitor = Win32Native.MonitorFromRect(rect, Win32Native.MONITOR_DEFAULTTONEAREST);
                Assert.NotEqual(0, monitor);
                Assert.Equal(0, Win32Native.GetDpiForMonitor(monitor, Win32Native.MDT_EFFECTIVE_DPI, out uint x, out uint y));
                Assert.InRange(x, 96u, 96u * 5);
                Assert.Equal(x, y);
            }
        }

        [Fact(Skip = "Windows 専用（user32 / gdi32 / shcore の呼び出し）", SkipUnless = nameof(IsWindows))]
        public void Cursor_PositionIsKnownInADesktopSession()
        {
            Assert.True(Win32Cursor.TryGetPosition(out int x, out int y));
            var rect = new Win32Native.Rect { Left = x, Top = y, Right = x + 1, Bottom = y + 1 };
            Assert.NotEqual(0, Win32Native.MonitorFromRect(rect, Win32Native.MONITOR_DEFAULTTONEAREST));
        }
    }
}
