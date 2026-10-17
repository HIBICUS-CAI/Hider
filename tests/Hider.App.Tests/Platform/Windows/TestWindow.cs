using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Hider.Platform.Windows;

namespace Hider.App.Tests.Platform.Windows
{
    /// <summary>
    /// A 64 x 32 popup tool window the test owns, on a thread running its message loop. The top half is red and the bottom
    /// half blue (painted on WM_PAINT, WM_ERASEBKGND and WM_PRINTCLIENT), so a capture of it can be checked pixel by pixel.
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal sealed unsafe partial class TestWindow : IDisposable
    {
        public const int Width = 64, Height = 32;
        public const uint Red = 0x000000FF, Blue = 0x00FF0000; // COLORREF: 0x00BBGGRR

        private const uint WsPopup = 0x80000000, WsVisible = 0x10000000;
        private const uint WsExTopmost = 0x8, WsExToolWindow = 0x80, WsExNoActivate = 0x08000000;
        private const uint WmDestroy = 0x0002, WmClose = 0x0010, WmPaint = 0x000F, WmEraseBkgnd = 0x0014, WmPrintClient = 0x0318;

        private readonly Thread thread;
        private readonly ManualResetEventSlim created = new();
        private readonly bool topmost;
        private Exception? failure;

        public TestWindow(int _x, int _y, bool _topmost = false)
        {
            X = _x;
            Y = _y;
            topmost = _topmost;
            ClassName = "HiderTest" + Guid.NewGuid().ToString("N");
            thread = new Thread(Run) { IsBackground = true, Name = "test window" };
            thread.Start();
            if (!created.Wait(TimeSpan.FromSeconds(30)))
            {
                throw new TimeoutException("the test window was not created within 30 seconds");
            }
            if (failure is not null)
            {
                throw new InvalidOperationException("the test window could not be created", failure);
            }
        }

        public int X { get; }

        public int Y { get; }

        public string ClassName { get; }

        public nint Handle { get; private set; }

        /// <summary>Closes the window (WM_CLOSE: DefWindowProc destroys it on its own thread) and ends the message loop.</summary>
        public void Destroy()
        {
            if (thread.IsAlive)
            {
                _ = PostMessageW(Handle, WmClose, 0, 0);
                if (!thread.Join(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("the test window did not close within 30 seconds");
                }
            }
        }

        public void Dispose()
        {
            Destroy();
            created.Dispose();
        }

        private void Run()
        {
            nint instance = GetModuleHandleW(null);
            try
            {
                fixed (char* name = ClassName)
                {
                    var wc = new WndClass { WndProc = &WndProc, Instance = instance, ClassName = name };
                    if (RegisterClassW(&wc) == 0)
                    {
                        throw new InvalidOperationException($"RegisterClassW failed: error {Marshal.GetLastPInvokeError()}");
                    }
                    uint exStyle = WsExToolWindow | WsExNoActivate | (topmost ? WsExTopmost : 0);
                    Handle = CreateWindowExW(exStyle, name, name, WsPopup | WsVisible, X, Y, Width, Height, 0, 0, instance, 0);
                    if (Handle == 0)
                    {
                        throw new InvalidOperationException($"CreateWindowExW failed: error {Marshal.GetLastPInvokeError()}");
                    }
                }
                _ = UpdateWindow(Handle); // WM_PAINT now (sent, not queued)
                _ = DwmFlush();           // and composed by DWM before the test captures it
            }
            catch (Exception e)
            {
                failure = e;
                created.Set();
                return;
            }
            created.Set();
            Msg msg;
            while (GetMessageW(&msg, 0, 0, 0) > 0)
            {
                _ = TranslateMessage(&msg);
                _ = DispatchMessageW(&msg);
            }
            fixed (char* name = ClassName)
            {
                _ = UnregisterClassW(name, instance);
            }
        }

        [UnmanagedCallersOnly]
        private static nint WndProc(nint _hwnd, uint _message, nint _wParam, nint _lParam)
        {
            switch (_message)
            {
                case WmPaint:
                    PaintStruct ps;
                    nint dc = BeginPaint(_hwnd, &ps);
                    Paint(dc);
                    _ = EndPaint(_hwnd, &ps);
                    return 0;
                case WmEraseBkgnd:
                    Paint(_wParam);
                    return 1;
                case WmPrintClient:
                    Paint(_wParam);
                    return 0;
                case WmDestroy:
                    PostQuitMessage(0);
                    return 0;
                default:
                    return DefWindowProcW(_hwnd, _message, _wParam, _lParam);
            }
        }

        private static void Paint(nint _dc)
        {
            Fill(_dc, new Win32Native.Rect { Left = 0, Top = 0, Right = Width, Bottom = Height / 2 }, Red);
            Fill(_dc, new Win32Native.Rect { Left = 0, Top = Height / 2, Right = Width, Bottom = Height }, Blue);
        }

        private static void Fill(nint _dc, Win32Native.Rect _rect, uint _color)
        {
            nint brush = CreateSolidBrush(_color);
            _ = FillRect(_dc, &_rect, brush);
            _ = Win32Native.DeleteObject(brush);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WndClass
        {
            public uint Style;
            public delegate* unmanaged<nint, uint, nint, nint, nint> WndProc;
            public int ClsExtra, WndExtra;
            public nint Instance, Icon, Cursor, Background;
            public char* MenuName, ClassName;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Msg
        {
            public nint Hwnd;
            public uint Message;
            public nint WParam, LParam;
            public uint Time;
            public int PointX, PointY;
            public uint Private;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PaintStruct
        {
            public nint Dc;
            public int Erase;
            public Win32Native.Rect Paint;
            public int Restore, IncUpdate;
            public fixed byte Reserved[32];
        }

        [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
        private static partial nint GetModuleHandleW(string? _name);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial ushort RegisterClassW(WndClass* _class);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UnregisterClassW(char* _name, nint _instance);

        [LibraryImport("user32.dll", SetLastError = true)]
        private static partial nint CreateWindowExW(uint _exStyle, char* _class, char* _name, uint _style, int _x, int _y, int _width, int _height,
            nint _parent, nint _menu, nint _instance, nint _param);

        [LibraryImport("user32.dll")]
        private static partial nint DefWindowProcW(nint _hwnd, uint _message, nint _wParam, nint _lParam);

        [LibraryImport("user32.dll")]
        private static partial int GetMessageW(Msg* _msg, nint _hwnd, uint _min, uint _max);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool TranslateMessage(Msg* _msg);

        [LibraryImport("user32.dll")]
        private static partial nint DispatchMessageW(Msg* _msg);

        [LibraryImport("user32.dll")]
        private static partial void PostQuitMessage(int _code);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool PostMessageW(nint _hwnd, uint _message, nint _wParam, nint _lParam);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool UpdateWindow(nint _hwnd);

        [LibraryImport("user32.dll")]
        private static partial nint BeginPaint(nint _hwnd, PaintStruct* _paint);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool EndPaint(nint _hwnd, PaintStruct* _paint);

        [LibraryImport("user32.dll")]
        private static partial int FillRect(nint _dc, Win32Native.Rect* _rect, nint _brush);

        [LibraryImport("gdi32.dll")]
        private static partial nint CreateSolidBrush(uint _color);

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmFlush();
    }
}
