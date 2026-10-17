using Avalonia.Headless.XUnit;
using Hider.Areas;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core.Tests.Fakes;
using Hider.Platform;

namespace Hider.App.Tests.Areas
{
    /// <summary>AreaStripHost (the area windows as the strip capture's IStripHost), AreaServices and AreaMode.</summary>
    public class AreaStripHostServicesTests
    {
        private static AreaStripHost Inline() => new(_f => _f());

        [Fact]
        public void BeforeAttach_NoAreasNoScreen()
        {
            var host = Inline();
            Assert.Empty(host.Areas);
            Assert.Equal(default, host.Screen);
            Assert.Empty(host.Screens);
        }

        [AvaloniaFact]
        public void AfterAttach_ReadsTheManagersStripState()
        {
            using var f = new AreaHarness(_isWindows: false);
            var host = Inline();
            host.Attach(f.Manager);
            Assert.Empty(host.Areas);
            var area = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            f.Manager.CaptureStarting();
            Assert.Equal(new StripArea(area.Model.Id, area.Model.Rect, PatternKind.Mosaic), Assert.Single(host.Areas));
            Assert.Equal(new PhysicalRect(0, 0, 1920, 1080), host.Screen);
            Assert.Equal(f.Screens.Screens, host.Screens);
        }

        [AvaloniaFact]
        public async Task Calls_BeforeAttach_CompleteWithoutAWindow()
        {
            var host = Inline();
            await host.SetTransparentRowsAsync("0f3b8d2e", 0, 8, TestContext.Current.CancellationToken);
            await host.RestoreAsync("0f3b8d2e");
        }

        [AvaloniaFact]
        public async Task Calls_ForAnAreaThatIsGone_TouchNoWindow()
        {
            using var f = new AreaHarness();
            var host = Inline();
            host.Attach(f.Manager);
            var area = f.Add(new PhysicalRect(100, 200, 120, 80));
            await host.SetTransparentRowsAsync("ffffffff", 0, 8, TestContext.Current.CancellationToken);
            Assert.Null(area.Window.TransparentRows);
            await host.RestoreAsync("ffffffff");
        }

        [AvaloniaFact]
        public async Task Calls_ReachTheAreasWindow_ByItsId()
        {
            using var f = new AreaHarness();
            var host = Inline();
            host.Attach(f.Manager);
            var a = f.Add(new PhysicalRect(100, 200, 120, 80));
            var b = f.Add(new PhysicalRect(400, 200, 120, 80));
            var strip = host.SetTransparentRowsAsync(b.Model.Id, 8, 16, TestContext.Current.CancellationToken);
            Assert.Equal((8, 16), b.Window.TransparentRows);
            Assert.Null(a.Window.TransparentRows);
            f.Time.Advance(AreaWindow.StripFrameTimeout); // no frame is rendered here: the wait gives up
            await WaitAsync(strip);
            await host.RestoreAsync(b.Model.Id);
            Assert.Null(b.Window.TransparentRows);
        }

        private static async Task WaitAsync(Task _task)
        {
            var until = DateTime.UtcNow.AddSeconds(5);
            while (!_task.IsCompleted && DateTime.UtcNow < until)
            {
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            }
            await _task;
        }

        [AvaloniaFact]
        public async Task CaptureCancelled_StopsWaitingForTheUiThread()
        {
            var never = new TaskCompletionSource();
            var host = new AreaStripHost(_ => never.Task);
            using var cts = new CancellationTokenSource();
            var task = host.SetTransparentRowsAsync("0f3b8d2e", 0, 8, cts.Token);
            Assert.False(task.IsCompleted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }

        [AvaloniaFact]
        public async Task DispatcherDroppingTheJob_CancelsTheCall()
        {
            var host = new AreaStripHost(_ => Task.FromCanceled(new CancellationToken(true)));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.RestoreAsync("0f3b8d2e"));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => host.SetTransparentRowsAsync("0f3b8d2e", 0, 8, CancellationToken.None));
        }

        [Fact]
        public async Task OnThreadPool_RunsTheCaptureOnAPoolThread_AndReturnsItsResult()
        {
            bool pool = false;
            var expected = CaptureResult.Failed(CaptureFailure.Denied);
            var result = await AreaServices.OnThreadPool(() =>
            {
                pool = Thread.CurrentThread.IsThreadPoolThread;
                return Task.FromResult(expected);
            });
            Assert.Same(expected, result);
            Assert.True(pool);
        }

        [Fact]
        public async Task OnThreadPool_PassesFailuresOn()
        {
            await Assert.ThrowsAsync<IOException>(() => AreaServices.OnThreadPool(() => throw new IOException("gone")));
        }

        [Fact]
        public void Services_KeepWhatTheyAreGiven()
        {
            var screens = new FakeScreenService();
            var time = new FakeTimeProvider();
            var capture = new FakeUnderlayCapture();
            Func<AreaWindow, IAreaWindowHost> hosts = _w => new FakeAreaWindowHost(_w, 1.0);
            Action<Action> post = _a => _a();
            Func<Func<Task<CaptureResult>>, Task<CaptureResult>> run = AreaServices.OnThreadPool;
            var services = new AreaServices(screens, hosts, "/x/areas.json", time, post, false, capture, run);
            Assert.Same(screens, services.Screens);
            Assert.Same(hosts, services.HostFactory);
            Assert.Equal("/x/areas.json", services.AreasPath);
            Assert.Same(time, services.Time);
            Assert.Same(post, services.UiPost);
            Assert.False(services.IsWindows);
            Assert.Same(capture, services.Capture);
            Assert.Same(run, services.RunCapture);
            var windows = services with { IsWindows = true };
            Assert.True(windows.IsWindows);
            Assert.NotEqual(services, windows);
            Assert.Equal(services, services with { });
        }

        [Fact]
        public void AreaMode_HiddenIsTheDefault()
        {
            Assert.Equal(AreaMode.Hidden, default(AreaMode));
            var values = Enum.GetValues<AreaMode>();
            Assert.Equal(2, values.Length);
            Assert.Equal((AreaMode.Hidden, AreaMode.Editing), (values[0], values[1]));
        }
    }
}
