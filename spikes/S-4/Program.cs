using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Spike
{
    /// <summary>
    /// S-4 (design §10): enumerate top-level windows in Z order (top first) like Win32UnderlayCapture will, capture each
    /// visible, non-cloaked, non-empty one with PrintWindow(PW_RENDERFULLCONTENT) and report whether the result is all
    /// black (every pixel's RGB = 0), how long it took and the DWM frame bounds.
    ///   S4.PrintWindow [--save &lt;folder&gt;]   --save writes one BMP per window for visual checking.
    /// Output: a Markdown table on stdout (paste it into docs/spikes.md).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static unsafe class Program
    {
        public static int Main(string[] _args)
        {
            if (!OperatingSystem.IsWindows())
            {
                Console.Error.WriteLine("Windows only.");
                return 2;
            }
            string? saveDir = null;
            int i = Array.IndexOf(_args, "--save");
            if (i >= 0 && i + 1 < _args.Length)
            {
                saveDir = _args[i + 1];
                Directory.CreateDirectory(saveDir);
            }

            var windows = new List<nint>();
            Native.EnumWindows((h, _) => { windows.Add(h); return true; }, 0);

            Console.WriteLine($"Windows {Environment.OSVersion.Version}, elevated={Environment.IsPrivilegedProcess}");
            Console.WriteLine("| z | class | title | process | bounds (DWM) | black | ms |");
            Console.WriteLine("|---|---|---|---|---|---|---|");
            int z = 0;
            foreach (nint hwnd in windows)
            {
                z++;
                if (!Native.IsWindowVisible(hwnd))
                {
                    continue;
                }
                int cloaked = 0;
                Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, &cloaked, sizeof(int));
                if (cloaked != 0)
                {
                    continue;
                }
                Native.Rect frame;
                if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, &frame, sizeof(Native.Rect)) != 0)
                {
                    Native.GetWindowRect(hwnd, out frame);
                }
                Native.GetWindowRect(hwnd, out var windowRect);
                int w = windowRect.Right - windowRect.Left, h = windowRect.Bottom - windowRect.Top;
                if (w <= 0 || h <= 0)
                {
                    continue;
                }

                var sw = Stopwatch.StartNew();
                bool ok = Capture(hwnd, w, h, out byte[] pixels);
                double ms = sw.Elapsed.TotalMilliseconds;
                bool black = !ok || IsBlack(pixels);
                if (saveDir is not null && ok)
                {
                    SaveBmp(Path.Combine(saveDir, $"{z:D3}_{Sanitize(ClassName(hwnd))}.bmp"), pixels, w, h);
                }
                Console.WriteLine($"| {z} | {ClassName(hwnd)} | {Escape(Title(hwnd))} | {ProcessName(hwnd)} | {frame.Left},{frame.Top} {frame.Right - frame.Left}x{frame.Bottom - frame.Top} | {(ok ? (black ? "**black**" : "no") : "**failed**")} | {ms:F1} |");
            }
            return 0;
        }

        private static bool Capture(nint _hwnd, int _w, int _h, out byte[] _pixels)
        {
            _pixels = new byte[_w * _h * 4];
            nint screen = Native.GetDC(0);
            nint dc = Native.CreateCompatibleDC(screen);
            var info = new Native.BitmapInfoHeader { Size = (uint)sizeof(Native.BitmapInfoHeader), Width = _w, Height = -_h, Planes = 1, BitCount = 32 };
            nint bits;
            nint bitmap = Native.CreateDIBSection(dc, &info, 0, &bits, 0, 0);
            nint old = Native.SelectObject(dc, bitmap);
            bool ok = Native.PrintWindow(_hwnd, dc, Native.PW_RENDERFULLCONTENT);
            if (ok)
            {
                Marshal.Copy(bits, _pixels, 0, _pixels.Length);
            }
            Native.SelectObject(dc, old);
            Native.DeleteObject(bitmap);
            Native.DeleteDC(dc);
            Native.ReleaseDC(0, screen);
            return ok;
        }

        private static bool IsBlack(byte[] _bgra)
        {
            for (int i = 0; i < _bgra.Length; i += 4)
            {
                if (_bgra[i] != 0 || _bgra[i + 1] != 0 || _bgra[i + 2] != 0)
                {
                    return false;
                }
            }
            return true;
        }

        private static void SaveBmp(string _path, byte[] _bgra, int _w, int _h)
        {
            using var f = new BinaryWriter(File.Create(_path));
            int size = 54 + _bgra.Length;
            f.Write((byte)'B'); f.Write((byte)'M'); f.Write(size); f.Write(0); f.Write(54);
            f.Write(40); f.Write(_w); f.Write(-_h); f.Write((short)1); f.Write((short)32); f.Write(0); f.Write(_bgra.Length); f.Write(2835); f.Write(2835); f.Write(0); f.Write(0);
            f.Write(_bgra);
        }

        private static string ClassName(nint _hwnd)
        {
            char* buffer = stackalloc char[256];
            int n = Native.GetClassNameW(_hwnd, buffer, 256);
            return new string(buffer, 0, n);
        }

        private static string Title(nint _hwnd)
        {
            char* buffer = stackalloc char[256];
            int n = Native.GetWindowTextW(_hwnd, buffer, 256);
            return new string(buffer, 0, n);
        }

        private static string ProcessName(nint _hwnd)
        {
            Native.GetWindowThreadProcessId(_hwnd, out uint pid);
            try
            {
                return Process.GetProcessById((int)pid).ProcessName;
            }
            catch (Exception)
            {
                return pid.ToString();
            }
        }

        private static string Escape(string _s) => _s.Replace("|", "\\|");

        private static string Sanitize(string _s)
        {
            var sb = new StringBuilder();
            foreach (char c in _s)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }
    }

    [SupportedOSPlatform("windows")]
    internal static unsafe partial class Native
    {
        public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;
        public const int DWMWA_CLOAKED = 14;
        public const uint PW_RENDERFULLCONTENT = 2;

        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        public struct BitmapInfoHeader
        {
            public uint Size;
            public int Width, Height;
            public ushort Planes, BitCount;
            public uint Compression, SizeImage;
            public int XPelsPerMeter, YPelsPerMeter;
            public uint ClrUsed, ClrImportant;
        }

        public delegate bool EnumWindowsProc(nint _hwnd, nint _param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool EnumWindows(EnumWindowsProc _proc, nint _param);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool IsWindowVisible(nint _hwnd);

        [LibraryImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool GetWindowRect(nint _hwnd, out Rect _rect);

        [LibraryImport("user32.dll")]
        public static partial int GetClassNameW(nint _hwnd, char* _buffer, int _max);

        [LibraryImport("user32.dll")]
        public static partial int GetWindowTextW(nint _hwnd, char* _buffer, int _max);

        [LibraryImport("user32.dll")]
        public static partial uint GetWindowThreadProcessId(nint _hwnd, out uint _pid);

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

        [LibraryImport("dwmapi.dll")]
        public static partial int DwmGetWindowAttribute(nint _hwnd, int _attribute, void* _value, int _size);
    }
}
