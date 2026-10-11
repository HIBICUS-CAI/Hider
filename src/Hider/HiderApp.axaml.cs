using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Persistence;
using Hider.Platform;
using Hider.Platform.Linux;
using Hider.Platform.Windows;
using Hider.Tray;

namespace Hider
{
    public partial class HiderApp : Application
    {
        private AreaManager? areas;
        private TrayController? tray;
        private Window? screenProbe;
        private PosixSignalRegistration? sigterm;

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Headless tests run without a desktop lifetime and build what they need themselves.
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                Start(desktop, StoragePaths.AreasFile, CreateHost, true);
            }
            base.OnFrameworkInitializationCompleted();
        }

        internal AreaManager? Areas => areas;
        internal TrayController? Tray => tray;

        /// <summary>No main window (FR-01): the tray icon and the restored areas (FR-02).</summary>
        internal void Start(IClassicDesktopStyleApplicationLifetime _desktop, string _areasPath, Func<AreaWindow, IAreaWindowHost> _hostFactory, bool _handleSignals)
        {
            // Screens need a top level; this window is never shown.
            screenProbe = new Window();
            var screens = new AvaloniaScreenService(() => screenProbe.Screens);
            areas = new AreaManager(screens, _hostFactory, _areasPath, TimeProvider.System,
                a => Dispatcher.UIThread.Post(a), OperatingSystem.IsWindows());
            areas.Restore();
            tray = new TrayController(this, areas, () => _desktop.Shutdown());

            // Save on exit and session end (FR-06, FR-61); every change is also saved as it happens.
            _desktop.ShutdownRequested += (_, _) => areas.SaveNow();
            _desktop.Exit += (_, _) => Stop();
            if (_handleSignals && !OperatingSystem.IsWindows())
            {
                sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, ctx =>
                {
                    ctx.Cancel = true;
                    Dispatcher.UIThread.Post(() => _desktop.Shutdown());
                });
            }
        }

        private static IAreaWindowHost CreateHost(AreaWindow _window)
        {
            return OperatingSystem.IsWindows() ? new Win32WindowHost(_window) : new X11WindowHost(_window);
        }

        internal void Stop()
        {
            sigterm?.Dispose();
            tray?.Dispose();
            areas?.Dispose();
            screenProbe?.Close();
            tray = null;
            areas = null;
        }
    }
}
