using System.Reflection;
using System.Runtime.Versioning;
using Hider.Core.Model;
using Hider.Platform.Linux;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    /// <summary>Tests that change the process-wide session bus address: run alone, after the parallel ones.</summary>
    [CollectionDefinition(nameof(SessionBusAddressGroup), DisableParallelization = true)]
    public sealed class SessionBusAddressGroup
    {
    }

    // A null address means the session bus (DBusAddress.Session); without one there is nothing to connect to, and every
    // user of the bus gives up quietly (CI, minimal sessions). Tmds.DBus.Protocol reads the address once (from
    // DBUS_SESSION_BUS_ADDRESS, or the X11 root window when DISPLAY is set) and keeps it in DBusAddress._sessionAddress:
    // the tests set that cache, with both variables cleared, so a developer's real session bus and portal are never used.
    [Collection(nameof(SessionBusAddressGroup))]
    [SupportedOSPlatform("linux")]
    public sealed class SessionBusAddressTests : IDisposable
    {
        private static readonly PhysicalRect needed = new(0, 0, 10, 10);
        private static readonly FieldInfo cache = typeof(DBusAddress).GetField("_sessionAddress", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Tmds.DBus.Protocol no longer caches the session bus address in DBusAddress._sessionAddress: update these tests");
        private readonly string? savedAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        private readonly string? savedDisplay = Environment.GetEnvironmentVariable("DISPLAY");
        private readonly object? savedCache = cache.GetValue(null);

        public SessionBusAddressTests()
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", null);
            Environment.SetEnvironmentVariable("DISPLAY", null);
        }

        public void Dispose()
        {
            cache.SetValue(null, savedCache);
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", savedAddress);
            Environment.SetEnvironmentVariable("DISPLAY", savedDisplay);
        }

        private static CancellationToken Ct => TestContext.Current.CancellationToken;

        /// <summary>The session bus as the process sees it from now on; null: none.</summary>
        private static void SessionBus(string? _address)
        {
            cache.SetValue(null, _address);
            Assert.Equal(_address, DBusAddress.Session);
        }

        [DbusFact]
        public async Task NoSessionBus_ClientGivesNoConnection()
        {
            SessionBus(null);
            using var client = new PortalClient();
            Assert.Null(await client.ConnectAsync(Ct));
            Assert.Null(await client.ConnectAsync(Ct));
        }

        [DbusFact]
        public async Task SessionBus_ClientConnectsToIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var client = new PortalClient();
            var bus = await client.ConnectAsync(Ct);
            Assert.NotNull(bus);
            Assert.StartsWith(":", bus!.UniqueName, StringComparison.Ordinal);
        }

        [DbusFact]
        public async Task NoSessionBus_NoTray()
        {
            SessionBus(null);
            Assert.False(await StatusNotifierProbe.HasWatcherAsync());
        }

        [DbusFact]
        public async Task SessionBus_TrayProbeAsksIt()
        {
            using var bus = PrivateBus.Start();
            using var watcher = await FakeDesktopPortal.OwnNameAsync(bus.Address, StatusNotifierProbe.WatcherName);
            SessionBus(bus.Address);
            Assert.True(await StatusNotifierProbe.HasWatcherAsync());
        }

        [DbusFact]
        public async Task NoSessionBus_ScreenshotGrabberIsNotReady()
        {
            SessionBus(null);
            using var grabber = new PortalScreenshotGrabber(() => new PhysicalRect(0, 0, 100, 100));
            Assert.False(await grabber.PrepareAsync(needed, Ct));
            Assert.False(grabber.ReadyWithoutPrepare);
        }

        [DbusFact]
        public async Task SessionBus_ScreenshotGrabberUsesIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var grabber = new PortalScreenshotGrabber(() => new PhysicalRect(0, 0, 100, 100));
            Assert.False(await grabber.PrepareAsync(needed, Ct)); // the fake answers without a uri
            Assert.Single(env.Portal.Calls);
        }

        [DbusFact]
        public async Task NoSessionBus_ScreenCastIsNull_AndCloseOnlyDisposesTheFd()
        {
            SessionBus(null);
            using var portal = new ScreenCastPortal();
            Assert.Null(await portal.StartAsync(null, Ct));
            var session = new ScreenCastSession("/s", 1, default, null, File.OpenHandle("/dev/null"));
            await portal.CloseAsync(session);
            Assert.True(session.PipeWireFd.IsClosed);
        }

        [DbusFact]
        public async Task SessionBus_ScreenCastUsesIt()
        {
            using var env = await PortalEnv.StartAsync();
            SessionBus(env.Address);
            using var portal = new ScreenCastPortal();
            Assert.Null(await portal.StartAsync(null, Ct)); // the fake's CreateSession has no session handle
            Assert.Equal("CreateSession", Assert.Single(env.Portal.Calls).Member);
        }
    }
}
