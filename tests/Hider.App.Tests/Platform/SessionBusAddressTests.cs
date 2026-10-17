using System.Runtime.Versioning;
using Hider.Core.Model;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    // A null address means the session bus (DBusAddress.Session); without one there is nothing to connect to, and every
    // user of the bus gives up quietly (CI, minimal sessions). SessionBusOverride sets Tmds.DBus.Protocol's cached address,
    // so a developer's real session bus and portal are never used.
    [Collection(nameof(SessionBusAddressGroup))]
    [SupportedOSPlatform("linux")]
    public sealed class SessionBusAddressTests : IDisposable
    {
        private static readonly PhysicalRect needed = new(0, 0, 10, 10);
        private readonly SessionBusOverride sessionBus = new();

        public void Dispose()
        {
            sessionBus.Dispose();
        }

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        /// <summary>The session bus as the process sees it from now on; null: none.</summary>
        private void SessionBus(string? _address)
        {
            sessionBus.Set(_address);
        }

        [DbusFact]
        public async Task GT109_NoSessionBus_ClientGivesNoConnection()
        {
            SessionBus(null);
            using var client = new PortalClient();
            Assert.Null(await client.ConnectAsync(Ct));
            Assert.Null(await client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task GT109_SessionBus_ClientConnectsToIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var client = new PortalClient();
            var bus = await client.ConnectAsync(Ct);
            Assert.NotNull(bus);
            Assert.StartsWith(":", bus!.UniqueName, StringComparison.Ordinal);
        }

        [DbusFact]
        public async Task GT109_NoSessionBus_NoTray()
        {
            SessionBus(null);
            Assert.False(await StatusNotifierProbe.HasWatcherAsync());
        }

        [DbusFact]
        public async Task GT109_SessionBus_TrayProbeAsksIt()
        {
            using var bus = PrivateBus.Start();
            using var watcher = await FakeDesktopPortal.OwnNameAsync(bus.Address, StatusNotifierProbe.WatcherName);
            SessionBus(bus.Address);
            Assert.True(await StatusNotifierProbe.HasWatcherAsync());
        }

        [DbusFact]
        public async Task GT109_NoSessionBus_ScreenshotGrabberIsNotReady()
        {
            SessionBus(null);
            using var grabber = new PortalScreenshotGrabber(() => new PhysicalRect(0, 0, 100, 100));
            Assert.False(await grabber.PrepareAsync(needed, Ct));
            Assert.False(grabber.ReadyWithoutPrepare);
        }

        [DbusFact]
        public async Task GT109_SessionBus_ScreenshotGrabberUsesIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var grabber = new PortalScreenshotGrabber(() => new PhysicalRect(0, 0, 100, 100));
            Assert.False(await grabber.PrepareAsync(needed, Ct)); // the fake answers without a uri
            Assert.Single(env.Portal.Calls);
        }

        [DbusFact]
        public async Task GT109_NoSessionBus_ScreenCastIsNull_AndCloseOnlyDisposesTheFd()
        {
            SessionBus(null);
            using var portal = new ScreenCastPortal();
            Assert.Null(await portal.StartAsync(null, Ct));
            var session = new ScreenCastSession("/s", 1, default, null, File.OpenHandle("/dev/null"));
            await portal.CloseAsync(session);
            Assert.True(session.PipeWireFd.IsClosed);
        }

        [DbusFact]
        public async Task GT109_SessionBus_ScreenCastUsesIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var portal = new ScreenCastPortal();
            Assert.Null(await portal.StartAsync(null, Ct)); // the fake's CreateSession has no session handle
            Assert.Equal("CreateSession", Assert.Single(env.Portal.Calls).Member);
        }
    }
}
