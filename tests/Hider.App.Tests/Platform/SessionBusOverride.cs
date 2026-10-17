using System.Reflection;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    /// <summary>Tests that change the process-wide session bus address: run alone, after the parallel ones.</summary>
    [CollectionDefinition(nameof(SessionBusAddressGroup), DisableParallelization = true)]
    public sealed class SessionBusAddressGroup
    {
    }

    /// <summary>
    /// The session bus as the process sees it, for one test (use only in <see cref="SessionBusAddressGroup"/>). A null
    /// address means the session bus (DBusAddress.Session) to every user of it in Hider. Tmds.DBus.Protocol resolves that
    /// address once per process (from DBUS_SESSION_BUS_ADDRESS, or the X11 root window when DISPLAY is set) and keeps it in
    /// DBusAddress._sessionAddress, so changing the variables later does nothing. This sets that cache (with both variables
    /// cleared), so a developer's real session bus, tray and portal are never used, and puts the cache and the variables
    /// back on Dispose.
    /// </summary>
    public sealed class SessionBusOverride : IDisposable
    {
        private static readonly FieldInfo cache = typeof(DBusAddress).GetField("_sessionAddress", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Tmds.DBus.Protocol no longer caches the session bus address in DBusAddress._sessionAddress: update SessionBusOverride");
        private readonly string? savedAddress = Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS");
        private readonly string? savedDisplay = Environment.GetEnvironmentVariable("DISPLAY");
        private readonly object? savedCache = cache.GetValue(null);
        private bool disposed;

        /// <param name="_address">The session bus from now on; null: none (what an unset variable resolves to).</param>
        public SessionBusOverride(string? _address = null)
        {
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", null);
            Environment.SetEnvironmentVariable("DISPLAY", null);
            Set(_address);
        }

        /// <summary>The session bus from now on; null: none.</summary>
        public void Set(string? _address)
        {
            ObjectDisposedException.ThrowIf(disposed, this); // the cache was put back: setting it now would leak
            cache.SetValue(null, _address);
            Assert.Equal(_address, DBusAddress.Session);
        }

        public void Dispose()
        {
            disposed = true;
            cache.SetValue(null, savedCache);
            Environment.SetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS", savedAddress);
            Environment.SetEnvironmentVariable("DISPLAY", savedDisplay);
        }
    }
}
