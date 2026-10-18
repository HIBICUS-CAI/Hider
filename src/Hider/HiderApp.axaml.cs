using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Persistence;
using Hider.Platform;
using Hider.Platform.Linux;
using Hider.Platform.Windows;
using Hider.Tray;

namespace Hider
{
    public sealed partial class HiderApp : Application, IDisposable
    {
        private AreaManager? areas;
        private TrayController? tray;
        private Window? screenProbe;
        private PosixSignalRegistration? sigterm;
        private IUnderlayCapture? capture;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Headless tests run without a desktop lifetime and build what they need themselves.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Start(desktop, StoragePaths.AreasFile, CreateHost, true, null, OperatingSystem.IsLinux() ? ProbeTray : null);
            }
            base.OnFrameworkInitializationCompleted();
        }

        internal AreaManager? Areas => areas;
        internal TrayController? Tray => tray;
        /// <summary>SIGTERM shuts the app down cleanly instead of ending the process (registered by Start).</summary>
        internal bool HandlesSignals => sigterm is not null;
        /// <summary>The never-shown window that gives the screens (closed by Stop).</summary>
        internal Window? ScreenProbe => screenProbe;

        /// <summary>No main window (FR-01): the tray icon and the restored areas (FR-02).</summary>
        /// <param name="_desktop">The desktop lifetime (Shutdown on Exit, ShutdownRequested saves).</param>
        /// <param name="_areasPath">areas.json to restore from and save to.</param>
        /// <param name="_hostFactory">Creates each area window's host (platform hosts; a fake in tests).</param>
        /// <param name="_handleSignals">Whether SIGTERM shuts down cleanly (not in tests).</param>
        /// <param name="_capture">The underlay capture; null for the platform's (<see cref="CreateCapture"/>).</param>
        /// <param name="_trayProbe">Whether a tray exists (Linux, D-9); null never shows the menu window.</param>
        internal void Start(IClassicDesktopStyleApplicationLifetime _desktop, string _areasPath, Func<AreaWindow, IAreaWindowHost> _hostFactory, bool _handleSignals,
            IUnderlayCapture? _capture, Func<Task<bool>>? _trayProbe)
        {
            // Screens need a top level; this window is never shown.
            screenProbe = new Window();
            var screens = new AvaloniaScreenService(() => screenProbe.Screens);
            Action<Action> uiPost = _a => Dispatcher.UIThread.Post(_a);
            var stripHost = new AreaStripHost(_f => Dispatcher.UIThread.InvokeAsync(_f));
            capture = _capture ?? CreateCapture(stripHost);
            areas = new AreaManager(new AreaServices(screens, _hostFactory, _areasPath, TimeProvider.System,
                uiPost, OperatingSystem.IsWindows(), capture, AreaServices.OnThreadPool));
            stripHost.Attach(areas);
            areas.Restore();
            tray = new TrayController(this, areas, () => _desktop.Shutdown(), _trayProbe);

            // Save on exit and session end (FR-06, FR-61); every change is also saved as it happens.
            _desktop.ShutdownRequested += (_, _) => areas.SaveNow();
            _desktop.Exit += (_, _) => Stop();
            if (_handleSignals && !OperatingSystem.IsWindows())
            {
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ctx =>
                {
                    _ctx.Cancel = true;
                    Dispatcher.UIThread.Post(() => _desktop.Shutdown());
                });
            }
        }

        /// <summary>
        /// Windows: window capture (design §5.2). Linux: strips (design §5.3) grabbed through ScreenCast + libhidercap
        /// (stage 2), falling back to the Screenshot portal (stage 1) when the helper, the portal or the monitor does not fit.
        /// </summary>
        private static IUnderlayCapture CreateCapture(AreaStripHost _stripHost)
        {
            if (OperatingSystem.IsWindows())
            {
                return new Win32UnderlayCapture();
            }
            if (OperatingSystem.IsLinux())
            {
                var stage2 = new PipeWireGrabber(new ScreenCastPortal(), new Hidercap(), new RestoreTokenStore(StoragePaths.RestoreToken), () => _stripHost.Screens);
                var stage1 = new PortalScreenshotGrabber(() => _stripHost.Screen);
                return new StripCoordinator(new FallbackGrabber(stage2, stage1), _stripHost, () => _stripHost.Areas, null, () => _stripHost.Screen);
            }
            return new UnavailableCapture();
        }

        [SupportedOSPlatform("linux")]
        private static Task<bool> ProbeTray()
        {
            return StatusNotifierProbe.HasWatcherAsync();
        }

        private static IAreaWindowHost CreateHost(AreaWindow _window)
        {
            if (OperatingSystem.IsWindows())
            {
                return new Win32WindowHost(_window);
            }
            if (OperatingSystem.IsLinux())
            {
                return new X11WindowHost(_window);
            }
            throw new PlatformNotSupportedException("Hider runs on Windows and Linux (win-x64, linux-x64, linux-arm64)");
        }

        /// <summary>Same as <see cref="Stop"/> (the lifetime's Exit normally calls it).</summary>
        public void Dispose()
        {
            Stop();
        }

        internal void Stop()
        {
            sigterm?.Dispose();
            tray?.Dispose();
            areas?.Dispose();
            capture?.Dispose();
            screenProbe?.Close();
            tray = null;
            areas = null;
            capture = null;
        }
    }
}
