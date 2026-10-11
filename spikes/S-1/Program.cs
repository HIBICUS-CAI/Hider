using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Avalonia.Themes.Fluent;

namespace Spike
{
    internal static class Program
    {
        public static bool Strip;

        [STAThread]
        public static int Main(string[] _args)
        {
            Strip = _args.Contains("--strip");
            return AppBuilder.Configure<SpikeApp>()
                .UsePlatformDetect()
                .With(new X11PlatformOptions { WmClass = "HiderSpike" })
                .StartWithClassicDesktopLifetime(_args, ShutdownMode.OnExplicitShutdown);
        }

        public static void Log(string _message)
        {
            Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {_message}");
        }
    }

    internal sealed class SpikeApp : Application
    {
        private TrayIcon? tray;
        private int areaCount;

        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        public override void OnFrameworkInitializationCompleted()
        {
            var menu = new NativeMenu();
            var add = new NativeMenuItem("新しいエリアを追加");
            add.Click += (_, _) => AddArea();
            var quit = new NativeMenuItem("終了");
            quit.Click += (_, _) => Quit();
            menu.Items.Add(add);
            menu.Items.Add(new NativeMenuItemSeparator());
            menu.Items.Add(quit);

            tray = new TrayIcon { Icon = new WindowIcon(MosaicIcon()), ToolTipText = "Hider spike", Menu = menu, IsVisible = true };
            tray.Clicked += (_, _) =>
            {
                Program.Log("tray: Clicked (left click)");
                ShowPopupMenu();
            };
            TrayIcon.SetIcons(this, new TrayIcons { tray });
            Program.Log($"started. strip={Program.Strip}. Left click the tray icon for the custom menu, right click for the native menu.");
            AddArea();
            base.OnFrameworkInitializationCompleted();
        }

        private void AddArea()
        {
            areaCount++;
            var window = new AreaSpikeWindow(areaCount);
            window.Show();
        }

        private void ShowPopupMenu()
        {
            // Borderless small window at the cursor with the same items as the native menu (design §6.3).
            var panel = new StackPanel { Margin = new Thickness(6), Spacing = 2 };
            var popup = new Window
            {
                WindowDecorations = WindowDecorations.None,
                Topmost = true,
                ShowInTaskbar = false,
                SizeToContent = SizeToContent.WidthAndHeight,
                CanResize = false,
                Content = panel,
            };
            var add = new Button { Content = "新しいエリアを追加", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            add.Click += (_, _) => { popup.Close(); AddArea(); };
            var quit = new Button { Content = "終了", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch };
            quit.Click += (_, _) => Quit();
            panel.Children.Add(add);
            panel.Children.Add(quit);
            popup.Deactivated += (_, _) => popup.Close();
            popup.Opened += (_, _) =>
            {
                var screen = popup.Screens.ScreenFromPoint(popup.Position) ?? popup.Screens.Primary;
                Program.Log($"popup opened at {popup.Position}, screen {screen?.Bounds}");
            };
            popup.WindowStartupLocation = WindowStartupLocation.Manual;
            popup.Position = CursorPosition();
            popup.Show();
            popup.Activate();
        }

        private static PixelPoint CursorPosition()
        {
            if (OperatingSystem.IsWindows() && Win32.GetCursorPos(out var p))
            {
                return new PixelPoint(p.X - 120, p.Y - 90);
            }
            return new PixelPoint(100, 100);
        }

        private void Quit()
        {
            tray?.Dispose();
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private static Bitmap MosaicIcon()
        {
            var bitmap = new WriteableBitmap(new PixelSize(16, 16), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = bitmap.Lock())
            {
                unsafe
                {
                    uint[] colors = { 0xFF3E4756, 0xFF68768A, 0xFF8A8F99, 0xFFC8CED8 };
                    for (int y = 0; y < 16; y++)
                    {
                        var row = (uint*)((byte*)fb.Address + y * fb.RowBytes);
                        for (int x = 0; x < 16; x++)
                        {
                            row[x] = colors[((x / 4) * 3 + (y / 4) * 5) % 4];
                        }
                    }
                }
            }
            return bitmap;
        }
    }

    internal sealed class AreaSpikeWindow : Window
    {
        private readonly AreaSurface surface;

        public AreaSpikeWindow(int _index)
        {
            Title = $"area {_index}";
            Width = 400;
            Height = 240;
            WindowDecorations = WindowDecorations.None;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            surface = new AreaSurface();
            Content = surface;

            Opened += (_, _) => Program.Log($"{Title}: opened at {Position} scaling={RenderScaling} transparency={ActualTransparencyLevel} active={IsActive}");
            Activated += (_, _) => Program.Log($"{Title}: ACTIVATED (took focus)");
            Deactivated += (_, _) => Program.Log($"{Title}: deactivated");
            DoubleTapped += (_, _) => Program.Log($"{Title}: double click");
            PointerPressed += (_, e) => Program.Log($"{Title}: pointer pressed {e.GetPosition(this)}");

            if (Program.Strip)
            {
                // Sweep an 8 DIP transparent band over the window so S-2 can check the pixels below show through.
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
                timer.Tick += (_, _) =>
                {
                    surface.StripY = surface.StripY + 8 >= Height ? 0 : surface.StripY + 8;
                    surface.InvalidateVisual();
                };
                timer.Start();
            }
        }
    }

    /// <summary>Opaque checker pattern with an optional fully transparent band; reports whether Skia renders on the GPU.</summary>
    internal sealed class AreaSurface : Control
    {
        public double StripY = -1000;
        private static bool reported;

        public override void Render(DrawingContext _context)
        {
            var bounds = new Rect(Bounds.Size);
            for (int y = 0; y < bounds.Height; y += 12)
            {
                for (int x = 0; x < bounds.Width; x += 12)
                {
                    var brush = ((x + y) / 12) % 2 == 0 ? Brushes.SlateGray : Brushes.DimGray;
                    var cell = new Rect(x, y, 12, 12).Intersect(bounds);
                    if (cell.Bottom <= StripY || cell.Top >= StripY + 8)
                    {
                        _context.FillRectangle(brush, cell);
                    }
                    else
                    {
                        if (cell.Top < StripY)
                        {
                            _context.FillRectangle(brush, new Rect(cell.X, cell.Top, cell.Width, StripY - cell.Top));
                        }
                        if (cell.Bottom > StripY + 8)
                        {
                            _context.FillRectangle(brush, new Rect(cell.X, StripY + 8, cell.Width, cell.Bottom - StripY - 8));
                        }
                    }
                }
            }
            if (!reported)
            {
                _context.Custom(new GpuProbe(bounds));
            }
        }

        private sealed class GpuProbe : ICustomDrawOperation
        {
            public GpuProbe(Rect _bounds)
            {
                Bounds = _bounds;
            }

            public Rect Bounds { get; }

            public bool HitTest(Point _p)
            {
                return false;
            }

            public bool Equals(ICustomDrawOperation? _other)
            {
                return false;
            }

            public void Render(ImmediateDrawingContext _context)
            {
                if (reported)
                {
                    return;
                }
                reported = true;
                var feature = _context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                using var lease = feature?.Lease();
                Program.Log($"render: skia lease={(lease is not null)} gpu={(lease?.GrContext is not null)} backend={lease?.GrContext?.Backend}");
            }

            public void Dispose()
            {
            }
        }
    }

    internal static partial class Win32
    {
        public struct Point
        {
            public int X;
            public int Y;
        }

        [System.Runtime.InteropServices.LibraryImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static partial bool GetCursorPos(out Point _point);
    }
}
