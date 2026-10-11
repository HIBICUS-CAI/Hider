using System.Runtime.Versioning;
using Hider.Core;
using Tmds.DBus.Protocol;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Whether a tray exists on Linux: Avalonia's TrayIcon is a StatusNotifierItem, shown only when something owns
    /// org.kde.StatusNotifierWatcher (GNOME: the AppIndicator extension). Without it Hider shows the D-9 window.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public static class StatusNotifierProbe
    {
        public const string WatcherName = "org.kde.StatusNotifierWatcher";

        /// <param name="_address">D-Bus address; null for the session bus.</param>
        public static async Task<bool> HasWatcherAsync(string? _address = null)
        {
            string? address = _address ?? DBusAddress.Session;
            if (address is null)
            {
                return false;
            }
            try
            {
                using var bus = new DBusConnection(address);
                await bus.ConnectAsync().ConfigureAwait(false);
                MessageBuffer call;
                using (var writer = bus.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader("org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s");
                    writer.WriteString(WatcherName);
                    call = writer.CreateMessage();
                }
                return await bus.CallMethodAsync(call, (Message m, object? _) => m.GetBodyReader().ReadBool()).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Log.Verbose($"tray probe: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }
    }
}
