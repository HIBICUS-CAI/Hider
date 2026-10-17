using System.Runtime.Versioning;

namespace Hider.App.Tests.Platform
{
    // The private dbus-daemon the D-Bus tests run on: it comes up on its own socket, goes away with its directory, and a
    // daemon that cannot start fails with its own standard error instead of a bare message.
    [SupportedOSPlatform("linux")]
    public sealed class PrivateBusTests
    {
        [DbusFact]
        public void Start_ListensOnItsOwnSocket_AndDisposeRemovesTheDirectory()
        {
            string dir = PrivateBus.NewDirectory();
            var bus = PrivateBus.Start(dir);
            try
            {
                Assert.Equal(Path.Combine(dir, "bus"), bus.SocketPath);
                Assert.StartsWith(PrivateBus.AddressIn(dir) + ",", bus.Address, StringComparison.Ordinal);
                Assert.Contains(",guid=", bus.Address, StringComparison.Ordinal);
                Assert.True(File.Exists(bus.SocketPath));
            }
            finally
            {
                bus.Dispose();
            }
            Assert.False(Directory.Exists(dir));
        }

        [DbusFact]
        public void Start_DaemonCannotListen_ThrowsWithItsStandardError_AndRemovesTheDirectory()
        {
            // A unix socket path is limited to 107 bytes: dbus-daemon exits at once, printing no address.
            string dir = PrivateBus.NewDirectory() + new string('x', 120);
            var e = Assert.Throws<InvalidOperationException>(() => PrivateBus.Start(dir));
            Assert.StartsWith("dbus-daemon exited without printing its address; standard error: ", e.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("(empty)", e.Message, StringComparison.Ordinal);
            Assert.False(Directory.Exists(dir));
        }
    }
}
