using System.Diagnostics;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Persistence;
using Hider.Core.Tests.Fakes;
using Hider.Platform;
using Hider.Tray;

namespace Hider.App.Tests.App
{
    /// <summary>
    /// <see cref="HiderApp"/> start-up and shutdown on a fresh application object and a fake desktop lifetime, so the shared
    /// headless application and its dispatcher are left alone. The real start changes the process-wide home directory and
    /// session bus variables for a moment, so it runs alone with the other tests that change them.
    /// </summary>
    [Collection(nameof(Hider.App.Tests.Platform.SessionBusAddressGroup))]
    public class HiderAppTests
    {
        public static bool IsLinux => OperatingSystem.IsLinux();

        /// <summary>
        /// The desktop lifetime HiderApp needs: records Shutdown and lets the test raise its events. Avalonia's own lifetime
        /// ends the dispatcher on Shutdown (fatal for the other headless tests) and the interface is not implementable in C#,
        /// so this is a runtime proxy of the interface.
        /// </summary>
        public class FakeLifetime : DispatchProxy
        {
            private EventHandler<ShutdownRequestedEventArgs>? shutdownRequested;
            private EventHandler<ControlledApplicationLifetimeExitEventArgs>? exit;
            private EventHandler<ControlledApplicationLifetimeStartupEventArgs>? startup;

            public static (IClassicDesktopStyleApplicationLifetime Lifetime, FakeLifetime Fake) Create()
            {
                var lifetime = Create<IClassicDesktopStyleApplicationLifetime, FakeLifetime>();
                return (lifetime, (FakeLifetime)(object)lifetime);
            }

            public int Shutdowns { get; private set; }

            public TaskCompletionSource ShutdownCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void RaiseShutdownRequested() => shutdownRequested?.Invoke(this, new ShutdownRequestedEventArgs());

            public void RaiseExit() => exit?.Invoke(this, new ControlledApplicationLifetimeExitEventArgs(0));

            public void RaiseStartup() => startup?.Invoke(this, new ControlledApplicationLifetimeStartupEventArgs(Array.Empty<string>()));

            protected override object? Invoke(MethodInfo? _method, object?[]? _args)
            {
                object? argument = _args is { Length: > 0 } ? _args[0] : null;
                switch (_method!.Name)
                {
                    case "add_ShutdownRequested":
                        shutdownRequested += (EventHandler<ShutdownRequestedEventArgs>?)argument;
                        return null;
                    case "remove_ShutdownRequested":
                        shutdownRequested -= (EventHandler<ShutdownRequestedEventArgs>?)argument;
                        return null;
                    case "add_Exit":
                        exit += (EventHandler<ControlledApplicationLifetimeExitEventArgs>?)argument;
                        return null;
                    case "remove_Exit":
                        exit -= (EventHandler<ControlledApplicationLifetimeExitEventArgs>?)argument;
                        return null;
                    case "add_Startup":
                        startup += (EventHandler<ControlledApplicationLifetimeStartupEventArgs>?)argument;
                        return null;
                    case "remove_Startup":
                        startup -= (EventHandler<ControlledApplicationLifetimeStartupEventArgs>?)argument;
                        return null;
                    case "Shutdown":
                    case "TryShutdown":
                        Shutdowns++;
                        ShutdownCalled.TrySetResult();
                        return _method.ReturnType == typeof(bool) ? true : null;
                    case "get_Args":
                        return Array.Empty<string>();
                    case "get_Windows":
                        return Array.Empty<Window>();
                    default:
                        return _method.ReturnType.IsValueType && _method.ReturnType != typeof(void) ? Activator.CreateInstance(_method.ReturnType) : null;
                }
            }
        }

        /// <summary>A capture that never captures, recording whether it was disposed (HiderApp owns the capture).</summary>
        private sealed class OwnedCapture : IUnderlayCapture
        {
            private readonly FakeUnderlayCapture inner = new();

            public bool Disposed { get; private set; }

            public CaptureTraits Traits => inner.Traits;

            public Task<CaptureResult> CaptureAsync(CaptureRequest _request, CancellationToken _ct) => inner.CaptureAsync(_request, _ct);

            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hider-app-" + Guid.NewGuid().ToString("N"));

            public TempDir()
            {
                Directory.CreateDirectory(Path);
            }

            public string AreasPath => System.IO.Path.Combine(Path, "areas.json");

            public void Dispose()
            {
                Directory.Delete(Path, true);
            }
        }

        private static (HiderApp, FakeLifetime) Start(TempDir _dir, IUnderlayCapture? _capture, bool _handleSignals = false, Func<Task<bool>>? _probe = null)
        {
            var app = new HiderApp();
            var (lifetime, fake) = FakeLifetime.Create();
            app.Start(lifetime, _dir.AreasPath, _w => new FakeAreaWindowHost(_w, 1.0), _handleSignals, _capture, _probe);
            return (app, fake);
        }

        [AvaloniaFact]
        public void GT44_Start_GivesTheAreasThePlatformServices()
        {
            using var dir = new TempDir();
            var capture = new FakeUnderlayCapture();
            var (app, _) = Start(dir, capture);
            try
            {
                var services = app.Areas!.Services;
                Assert.Equal(dir.AreasPath, services.AreasPath);
                Assert.Same(capture, services.Capture);
                Assert.Same(TimeProvider.System, services.Time);
                Assert.Equal(OperatingSystem.IsWindows(), services.IsWindows);
                Assert.IsType<AvaloniaScreenService>(services.Screens);
                Assert.NotEmpty(services.Screens.Screens); // through the never-shown probe window
                Assert.Equal(AreaServices.OnThreadPool, services.RunCapture);
                Assert.Empty(app.Areas.Areas);
                Assert.NotNull(app.Tray);
                Assert.Null(app.Tray!.FallbackWindow);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT44_Start_UiPost_RunsOnTheDispatcherLater()
        {
            using var dir = new TempDir();
            var (app, _) = Start(dir, new FakeUnderlayCapture());
            try
            {
                bool ran = false;
                app.Areas!.Services.UiPost(() => ran = true);
                Assert.False(ran);
                Dispatcher.UIThread.RunJobs();
                Assert.True(ran);
            }
            finally
            {
                app.Stop();
            }
        }

        /// <summary>Start with the platform's capture, its restore token under <paramref name="_dir"/> (HOME, for the start).</summary>
        private static HiderApp StartWithThePlatformCapture(TempDir _dir)
        {
            string? home = Environment.GetEnvironmentVariable("HOME");
            try
            {
                Environment.SetEnvironmentVariable("HOME", _dir.Path);
                return Start(_dir, null).Item1;
            }
            finally
            {
                Environment.SetEnvironmentVariable("HOME", home);
            }
        }

        /// <summary>
        /// Runs the UI thread's jobs, rendering frames, until the area's first capture has ended (a frame, or the fallback).
        /// The rendered frames are what the strip host waits for before each strip is taken. The capture's bus round trips and
        /// image decoding run off the UI thread and slow down on a loaded runner, hence the larger limit (unloaded: about
        /// 1.5 s).
        /// </summary>
        private static void FirstCapture(AreaController _area)
        {
            Assert.True(Hider.App.Tests.Areas.UiJobs.RunUntil(() => _area.Frames.Version > 0 || _area.CaptureFailed, _renderFrames: true,
                _limit: TimeSpan.FromSeconds(60), _workOffThread: true), "the first capture did not end");
        }

        [AvaloniaFact(Skip = Hider.App.Tests.Platform.LinuxTestEnvironment.DbusReason, SkipType = typeof(Hider.App.Tests.Platform.LinuxTestEnvironment),
            SkipUnless = nameof(Hider.App.Tests.Platform.LinuxTestEnvironment.HasDbus))]
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        public async Task GT45_Start_LinuxPlatformCapture_TakesTheStripsThroughTheScreenshotPortal()
        {
            // The Linux capture built by Start, end to end on a private bus with the scriptable portal (stage 1: its
            // ScreenCast answers without a session). A new mosaic area's first capture goes through the strip host (the
            // areas, the screen, transparent strips on the UI thread): one shot to prepare, then one per strip, each a PNG of
            // the whole screen that is deleted once read (FR-53). The pixels are checked by xvfb-strip-smoke.sh.
            using var dir = new TempDir();
            using var env = await Hider.App.Tests.Platform.PortalEnv.StartAsync();
            using var bus = new Hider.App.Tests.Platform.SessionBusOverride(env.Address);
            string shots = Path.Combine(dir.Path, "shots");
            Directory.CreateDirectory(shots);
            using var app = StartWithThePlatformCapture(dir);
            var screen = app.Areas!.Services.Screens.Screens.Single(_s => _s.IsPrimary).Bounds;
            int shot = 0;
            // One encoded screen, copied for each request: encoding a whole-screen PNG each time would be most of the run.
            string template = Path.Combine(dir.Path, "screen.png");
            Hider.App.Tests.Platform.PortalScreenshotGrabberTests.WritePng(template, screen.Width, screen.Height);
            env.Portal.OnRequest = _call =>
            {
                if (_call.Member != "Screenshot")
                {
                    return Hider.App.Tests.Platform.FakeDesktopPortal.Answer.Ok();
                }
                string path = Path.Combine(shots, $"Screenshot-{Interlocked.Increment(ref shot)}.png");
                File.Copy(template, path);
                return new Hider.App.Tests.Platform.FakeDesktopPortal.Answer.Respond(0,
                    (ref Tmds.DBus.Protocol.MessageWriter _w) => Hider.App.Tests.Platform.FakeDesktopPortal.Entry(ref _w, "uri", new Uri(path).AbsoluteUri));
            };

            var area = app.Areas.AddNew();
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            FirstCapture(area);

            Assert.False(area.CaptureFailed);
            Assert.Equal(1, area.Frames.Version);
            Assert.Equal(PatternKind.Mosaic, area.Model.Pattern);
            int strips = (area.Model.Rect.Height + area.Model.Mosaic.Common.StripPx - 1) / area.Model.Mosaic.Common.StripPx;
            Assert.Equal(1 + strips, shot); // one shot to prepare, one per strip
            Assert.Null(area.Window.TransparentRows); // every strip given back
            Assert.Equal(0, area.Window.StripFrameTimeouts); // each strip taken once its transparent rows were rendered
            Assert.Empty(Directory.GetFiles(shots));
        }

        [AvaloniaFact(Skip = Hider.App.Tests.Platform.LinuxTestEnvironment.DbusReason, SkipType = typeof(Hider.App.Tests.Platform.LinuxTestEnvironment),
            SkipUnless = nameof(Hider.App.Tests.Platform.LinuxTestEnvironment.HasDbus))]
        [System.Runtime.Versioning.SupportedOSPlatform("linux")]
        public async Task GT45_Start_LinuxPlatformCapture_PortalWithoutAnImage_FallsBackToTheGeneratedPattern()
        {
            // The portal answers every request without a result: no stage can deliver, the area shows a generated pattern
            // (FR-52) and no strip is left transparent.
            using var dir = new TempDir();
            using var env = await Hider.App.Tests.Platform.PortalEnv.StartAsync();
            using var bus = new Hider.App.Tests.Platform.SessionBusOverride(env.Address);
            using var app = StartWithThePlatformCapture(dir);

            var area = app.Areas!.AddNew();
            FirstCapture(area);

            Assert.True(area.CaptureFailed);
            // The last resort, the Screenshot portal, is always asked; with the helper ScreenCast (stage 2) is asked first.
            var members = env.Portal.Calls.Select(_c => _c.Member).ToList();
            Assert.Contains("Screenshot", members);
            if (Hider.App.Tests.Platform.LinuxTestEnvironment.HasHidercap)
            {
                int session = members.IndexOf("CreateSession");
                Assert.True(session >= 0, "ScreenCast was not asked: " + string.Join(", ", members));
                Assert.True(session < members.IndexOf("Screenshot"), "Screenshot was asked before ScreenCast: " + string.Join(", ", members));
            }
            Assert.Equal(PatternKind.Generated, area.Model.Pattern);
            Assert.Null(area.Window.TransparentRows);
        }

        [AvaloniaFact]
        public void GT44_Start_WithoutACapture_UsesThePlatformCapture()
        {
            // Windows: window capture. Linux: strips through ScreenCast / Screenshot (nothing connects until a capture runs).
            using var dir = new TempDir();
            var (app, _) = Start(dir, null);
            try
            {
                var capture = app.Areas!.Services.Capture;
                Assert.IsType(OperatingSystem.IsWindows() ? typeof(Hider.Platform.Windows.Win32UnderlayCapture) : typeof(StripCoordinator), capture);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT46_Start_RestoresTheSavedAreas_Hidden()
        {
            using var dir = new TempDir();
            AreasFile.Save(dir.AreasPath, new AppState
            {
                Areas =
                {
                    new AreaState { Id = "0f3b8d2e", X = 100, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Fill },
                    new AreaState { Id = "1a2b3c4d", X = 500, Y = 100, Width = 300, Height = 200, Pattern = PatternKind.Generated },
                },
            });

            var (app, _) = Start(dir, new FakeUnderlayCapture());
            try
            {
                Assert.Equal(2, app.Areas!.Areas.Count);
                Assert.All(app.Areas.Areas, _a => Assert.Equal(AreaMode.Hidden, _a.Mode));
                Assert.Equal("Hider — エリア 2 個", app.Tray!.ToolTip);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT47_ShutdownRequested_SavesNow()
        {
            // FR-06 / FR-61: a pending (debounced) change reaches areas.json when the session ends.
            using var dir = new TempDir();
            var (app, lifetime) = Start(dir, new FakeUnderlayCapture());
            try
            {
                var area = app.Areas!.AddNew();
                area.ExitEdit();
                area.Model.Opacity = 0.42;
                area.SaveSettings(false);
                int saves = app.Areas.SaveCount;

                lifetime.RaiseShutdownRequested();

                Assert.Equal(saves + 1, app.Areas.SaveCount);
                Assert.Equal(0.42, AreasFile.Load(dir.AreasPath, out _).Areas.Single().Opacity, 6);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT47_Exit_StopsEverything()
        {
            using var dir = new TempDir();
            var capture = new OwnedCapture();
            var (app, lifetime) = Start(dir, capture);
            using var stopAnyway = app; // a failed assert must not leave the tray icon behind
            var tray = app.Tray!;
            var areas = app.Areas!;
            areas.AddNew();
            tray.ShowPopup();
            var popup = tray.PopupWindow!;
            bool probeClosed = false;
            app.ScreenProbe!.Closed += (_, _) => probeClosed = true;

            lifetime.RaiseExit();

            Assert.Null(app.Areas);
            Assert.Null(app.Tray);
            Assert.Empty(areas.Areas);
            Assert.False(popup.IsVisible);
            Assert.True(capture.Disposed);
            Assert.True(probeClosed);
        }

        [AvaloniaFact]
        public async Task GT47_TrayExit_ShutsTheLifetimeDown()
        {
            using var dir = new TempDir();
            var (app, lifetime) = Start(dir, new FakeUnderlayCapture());
            try
            {
                await app.Tray!.ExecuteAsync(TrayCommand.Exit);

                Assert.Equal(1, lifetime.Shutdowns);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT44_Startup_IsNotNeeded()
        {
            // Everything is built in Start: the lifetime's Startup event carries nothing HiderApp waits for.
            using var dir = new TempDir();
            var (app, lifetime) = Start(dir, new FakeUnderlayCapture());
            try
            {
                lifetime.RaiseStartup();
                Assert.NotNull(app.Areas);
                Assert.Equal(0, lifetime.Shutdowns);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT47_Stop_Twice_AndDispose_AreHarmless()
        {
            using var dir = new TempDir();
            var (app, _) = Start(dir, new FakeUnderlayCapture());
            using var stopAnyway = app;

            app.Stop();
            app.Stop();
            app.Dispose();

            Assert.Null(app.Areas);
            Assert.Null(app.Tray);
        }

        [AvaloniaFact]
        public void GT47_Dispose_StopsLikeExit()
        {
            using var dir = new TempDir();
            var capture = new OwnedCapture();
            var (app, _) = Start(dir, capture);
            using var stopAnyway = app;

            app.Dispose();

            Assert.Null(app.Areas);
            Assert.Null(app.Tray);
            Assert.True(capture.Disposed);
        }

        [AvaloniaFact]
        public void GT47_Stop_BeforeStart_DoesNothing()
        {
            var app = new HiderApp();

            var error = Record.Exception(app.Stop);

            Assert.Null(error);
            Assert.Null(app.Areas);
        }

        [AvaloniaFact]
        public void GT48_Start_HandleSignals_RegistersSigtermOffWindowsOnly()
        {
            using var dir = new TempDir();
            var (app, _) = Start(dir, new FakeUnderlayCapture(), true);
            try
            {
                Assert.Equal(!OperatingSystem.IsWindows(), app.HandlesSignals);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT48_Start_WithoutSignalHandling_RegistersNothing()
        {
            using var dir = new TempDir();
            var (app, _) = Start(dir, new FakeUnderlayCapture(), false);
            try
            {
                Assert.False(app.HandlesSignals);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact(Skip = OsOnly.Sigterm, SkipUnless = nameof(IsLinux))]
        public async Task GT48_Sigterm_ShutsDownCleanly_InsteadOfKillingTheProcess()
        {
            // A logout or `kill` sends SIGTERM: the areas must be saved by the normal shutdown, not lost.
            using var dir = new TempDir();
            var (app, lifetime) = Start(dir, new FakeUnderlayCapture(), true);
            try
            {
                // Guard: without the registration the signal would end the test process itself.
                Assert.True(app.HandlesSignals);
                using (var kill = Process.Start(new ProcessStartInfo("kill", ["-TERM", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture)]) { UseShellExecute = false })!)
                {
                    await kill.WaitForExitAsync();
                    Assert.Equal(0, kill.ExitCode);
                }

                await lifetime.ShutdownCalled.Task.WaitAsync(TimeSpan.FromSeconds(30));

                Assert.Equal(1, lifetime.Shutdowns);
            }
            finally
            {
                app.Stop();
            }
        }

        [AvaloniaFact]
        public void GT44_FrameworkInitialized_WithoutADesktopLifetime_StartsNothing()
        {
            // Headless tests (and the previewer) have no desktop lifetime: nothing is built.
            var app = new HiderApp();

            app.OnFrameworkInitializationCompleted();

            Assert.Null(app.Areas);
            Assert.Null(app.Tray);
        }

        [AvaloniaFact]
        public async Task GT44_FrameworkInitialized_WithADesktopLifetime_StartsWithThePlatformParts()
        {
            // The real start: areas.json under the home directory, the platform's window host and capture, SIGTERM and (Linux)
            // the tray probe. The home directory and the session bus are pointed at temporary places for the duration of the
            // synchronous start, which reads them.
            using var dir = new TempDir();
            var app = new HiderApp();
            var (desktop, lifetime) = FakeLifetime.Create();
            app.ApplicationLifetime = desktop;
            string homeVariable = OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME";
            string? home = Environment.GetEnvironmentVariable(homeVariable);
            // Tmds.DBus.Protocol resolves the session bus once per process: a variable alone would not reach the probe when
            // another test resolved it first (and a real tray on the machine would then show no menu window).
            using var noBus = new Hider.App.Tests.Platform.SessionBusOverride("unix:path=" + Path.Combine(dir.Path, "no-bus"));
            try
            {
                Environment.SetEnvironmentVariable(homeVariable, dir.Path);
                app.OnFrameworkInitializationCompleted();
            }
            finally
            {
                Environment.SetEnvironmentVariable(homeVariable, home);
            }
            try
            {
                var services = app.Areas!.Services;
                Assert.Equal(Path.Combine(dir.Path, ".hider", "areas.json"), services.AreasPath);
                Assert.IsType(OperatingSystem.IsWindows() ? typeof(Hider.Platform.Windows.Win32UnderlayCapture) : typeof(StripCoordinator), services.Capture);
                var area = app.Areas.AddNew();
                Assert.IsType(OperatingSystem.IsWindows() ? typeof(Hider.Platform.Windows.Win32WindowHost) : typeof(Hider.Platform.Linux.X11WindowHost), area.Host);
                Assert.Equal(!OperatingSystem.IsWindows(), app.HandlesSignals);

                await Task.Yield();
                for (int i = 0; i < 500 && OperatingSystem.IsLinux() && app.Tray!.FallbackWindow is null; i++)
                {
                    // The probe fails to connect to the missing bus on the thread pool and resumes on the UI thread.
                    await Task.Delay(20);
                    Dispatcher.UIThread.RunJobs();
                }
                // Linux without a tray (here: without a session bus) gets the menu window (D-9); Windows never probes.
                Assert.Equal(OperatingSystem.IsLinux(), app.Tray!.FallbackWindow is not null);
                Assert.True(File.Exists(services.AreasPath)); // AddNew saved into the home directory given at start
            }
            finally
            {
                lifetime.RaiseExit();
            }
            Assert.Null(app.Areas);
        }
    }
}
