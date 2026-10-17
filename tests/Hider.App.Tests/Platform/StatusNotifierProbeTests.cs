using System.Runtime.Versioning;
using Hider.Core;
using Hider.Platform.Linux;

namespace Hider.App.Tests.Platform
{
    // Whether a StatusNotifierWatcher (a tray) exists, asked of a private dbus-daemon.
    [SupportedOSPlatform("linux")]
    public sealed class StatusNotifierProbeTests
    {
        [Fact]
        public void WatcherName_IsKdesStatusNotifierWatcher()
        {
            Assert.Equal("org.kde.StatusNotifierWatcher", StatusNotifierProbe.WatcherName);
        }

        [DbusFact]
        public async Task HasWatcherAsync_SomeoneOwnsTheName_IsTrue()
        {
            using var bus = PrivateBus.Start();
            using var watcher = await FakeDesktopPortal.OwnNameAsync(bus.Address, StatusNotifierProbe.WatcherName);
            Assert.True(await StatusNotifierProbe.HasWatcherAsync(bus.Address));
        }

        [DbusFact]
        public async Task HasWatcherAsync_NobodyOwnsTheName_IsFalse()
        {
            using var bus = PrivateBus.Start();
            // Another name does not count (GNOME without the AppIndicator extension).
            using var other = await FakeDesktopPortal.OwnNameAsync(bus.Address, "org.kde.StatusNotifierHost-1");
            Assert.False(await StatusNotifierProbe.HasWatcherAsync(bus.Address));
        }

        [DbusFact]
        public async Task HasWatcherAsync_WatcherGoneAgain_IsFalse()
        {
            using var bus = PrivateBus.Start();
            using var watcher = await FakeDesktopPortal.OwnNameAsync(bus.Address, StatusNotifierProbe.WatcherName);
            Assert.True(await StatusNotifierProbe.HasWatcherAsync(bus.Address));
            await watcher.ReleaseNameAsync(StatusNotifierProbe.WatcherName);
            Assert.False(await StatusNotifierProbe.HasWatcherAsync(bus.Address));
        }

        [Fact]
        public async Task HasWatcherAsync_NoBus_IsFalse_AndLogged()
        {
            using var log = new StringWriter();
            using (Log.CaptureTo(log))
            {
                Assert.False(await StatusNotifierProbe.HasWatcherAsync("unix:path=/nonexistent/hider-test-bus"));
            }
            Assert.StartsWith("tray probe: ", log.ToString(), StringComparison.Ordinal);
        }
    }
}
