using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;

namespace Spike
{
    /// <summary>
    /// S-6 (design §10): ScreenCast portal (persist_mode 2) + libhidercap.so (the shipped native/hidercap, all rows).
    /// A topmost window flips between two colors; after each flip has been rendered, the program asks the helper for the
    /// first frame whose PTS is later than the flip and checks that the frame shows the new color where the window is.
    /// Reports: dialog on 2nd run (restore_token), frames waited per flip (target &lt;= 3), coordinate offset (target 0).
    /// </summary>
    internal static class Program
    {
        public static ScreenCastPortal.Session? Session;
        private static readonly string TokenPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hider-spike-s6", "restore_token");

        [STAThread]
        public static int Main(string[] _args)
        {
            if (Stopwatch.Frequency != 1_000_000_000)
            {
                Console.WriteLine($"unexpected Stopwatch frequency {Stopwatch.Frequency}");
            }
            string? token = File.Exists(TokenPath) ? File.ReadAllText(TokenPath).Trim() : null;
            Console.WriteLine(token is null ? "no restore token: a dialog is expected" : "restore token found: no dialog expected");
            var portal = new ScreenCastPortal();
            var sw = Stopwatch.StartNew();
            Session = portal.StartAsync(token).GetAwaiter().GetResult();
            Console.WriteLine($"portal: {sw.ElapsedMilliseconds} ms, node {Session.Stream.NodeId}, stream position {Session.Stream.X},{Session.Stream.Y} size {Session.Stream.Width}x{Session.Stream.Height}, new restore token: {Session.RestoreToken is not null}");
            if (Session.RestoreToken is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(TokenPath)!);
                File.WriteAllText(TokenPath, Session.RestoreToken); // tokens are single use: always keep the newest
            }
            int fd = (int)Session.PipeWireFd.DangerousGetHandle();
            Session.PipeWireFd.SetHandleAsInvalid(); // the helper owns the fd now
            int started = Native.hidercap_start(fd, Session.Stream.NodeId);
            Console.WriteLine($"hidercap_start: {started}");
            if (started != 0)
            {
                return 1;
            }
            try
            {
                return AppBuilder.Configure<Application>().UsePlatformDetect().StartWithClassicDesktopLifetime(_args, ShutdownMode.OnMainWindowClose);
            }
            finally
            {
                Native.hidercap_stop();
                portal.Dispose();
            }
        }
    }

    internal sealed class Application : Avalonia.Application
    {
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new FlipWindow();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal sealed class FlipWindow : Window
    {
        private readonly Border fill = new() { Background = Brushes.Red };

        public FlipWindow()
        {
            Width = 240;
            Height = 240;
            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowActivated = false;
            Content = fill;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint(300, 300);
            Opened += async (_, _) => await RunAsync();
        }

        private async Task RunAsync()
        {
            foreach (var screen in Screens.All)
            {
                Console.WriteLine($"x11 screen: bounds {screen.Bounds} scaling {screen.Scaling} primary {screen.IsPrimary}");
            }
            await Task.Delay(1000);
            var compositor = ElementComposition.GetElementVisual(this)!.Compositor;
            var frames = new List<ulong>();
            var times = new List<double>();
            int mismatches = 0;
            for (int i = 0; i < 20; i++)
            {
                var color = i % 2 == 0 ? Colors.Blue : Colors.Red;
                var sw = Stopwatch.StartNew();
                fill.Background = new SolidColorBrush(color);
                await compositor.RequestCompositionBatchCommitAsync().Rendered;
                long t0 = Stopwatch.GetTimestamp();
                ulong seq0 = Native.hidercap_last_sequence();
                var rect = new PixelRect(Position, PixelSize.FromSize(new Size(Width, Height), RenderScaling));
                var result = await Task.Run(() => Grab(t0));
                times.Add(sw.Elapsed.TotalMilliseconds);
                if (result is null)
                {
                    Console.WriteLine($"#{i}: timeout (no frame after the flip within 2 s; last seq {Native.hidercap_last_sequence()})");
                    mismatches++;
                    continue;
                }
                var (frame, info) = result.Value;
                ulong waited = info.Sequence - seq0;
                frames.Add(waited);
                var box = FindColor(frame, info, color);
                bool match = box is not null;
                if (!match)
                {
                    mismatches++;
                }
                Console.WriteLine($"#{i}: {waited} frame(s) after flip, {sw.Elapsed.TotalMilliseconds:F1} ms, pts_from_meta={info.PtsFromMeta}, color found={match}" +
                    (box is { } b ? $", frame box {b} vs window {rect} (dx {b.X - rect.X}, dy {b.Y - rect.Y}, dw {b.Width - rect.Width}, dh {b.Height - rect.Height})" : ""));
            }
            Console.WriteLine(frames.Count > 0
                ? $"summary: frames per flip max {frames.Max()} avg {frames.Average(f => (double)f):F2}, ms avg {times.Average():F1}, mismatches {mismatches}/20 (pass: max <= 3, offset 0)"
                : "summary: no frames");
            Close();
        }

        private static unsafe (byte[], Native.Frame)? Grab(long _notBefore)
        {
            var info = new Native.Frame();
            int r = Native.hidercap_grab_after(_notBefore, 2000, 0, int.MaxValue, null, 0, ref info);
            if (r != 1)
            {
                return null;
            }
            var buffer = new byte[info.Stride * info.Height];
            fixed (byte* p = buffer)
            {
                r = Native.hidercap_grab_after(_notBefore, 2000, 0, int.MaxValue, p, buffer.Length, ref info);
            }
            return r == 0 ? (buffer, info) : null;
        }

        /// <summary>Bounding box (frame pixels, offset by the stream position) of pixels close to the color.</summary>
        private static PixelRect? FindColor(byte[] _bgra, Native.Frame _info, Color _color)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
            for (int y = 0; y < _info.Height; y++)
            {
                int row = y * _info.Stride;
                for (int x = 0; x < _info.Width; x++)
                {
                    int i = row + x * 4;
                    if (Math.Abs(_bgra[i] - _color.B) < 8 && Math.Abs(_bgra[i + 1] - _color.G) < 8 && Math.Abs(_bgra[i + 2] - _color.R) < 8)
                    {
                        minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                        minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                    }
                }
            }
            if (maxX < 0)
            {
                return null;
            }
            var s = Program.Session!.Stream;
            return new PixelRect(minX + s.X, minY + s.Y, maxX - minX + 1, maxY - minY + 1);
        }
    }

    internal static unsafe partial class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct Frame
        {
            public int Width;
            public int Height;
            public int Stride;
            public int PtsFromMeta;
            public long PtsNs;
            public ulong Sequence;
        }

        [LibraryImport("hidercap")]
        public static partial int hidercap_start(int _fd, uint _nodeId);

        [LibraryImport("hidercap")]
        public static partial int hidercap_grab_after(long _notBeforeNs, int _timeoutMs, int _row0, int _row1, byte* _dst, int _dstSize, ref Frame _out);

        [LibraryImport("hidercap")]
        public static partial ulong hidercap_last_sequence();

        [LibraryImport("hidercap")]
        public static partial void hidercap_stop();
    }
}
