using System.Runtime.Versioning;
using Hider.Core.Capture;
using Hider.Core.Model;
using Hider.Core;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Hider.Platform.Linux
{
    /// <summary>A started ScreenCast session: one monitor stream (position and size in the portal's coordinates).</summary>
    public sealed record ScreenCastSession(string Handle, uint NodeId, PhysicalRect StreamRect, string? RestoreToken, SafeFileHandle PipeWireFd);

    /// <summary>org.freedesktop.portal.ScreenCast, behind an interface so the grabber can be tested without a portal.</summary>
    public interface IScreenCastPortal : IDisposable
    {
        /// <summary>
        /// CreateSession → SelectSources (one monitor, cursor hidden, persist_mode 2, the restore token if any) → Start →
        /// OpenPipeWireRemote. Null when the portal is missing or fails; throws <see cref="CaptureDeniedException"/> when
        /// the user cancels the dialog.
        /// </summary>
        Task<ScreenCastSession?> StartAsync(string? _restoreToken, CancellationToken _ct);

        /// <summary>Ends the session (GNOME's screen sharing indicator goes away).</summary>
        Task CloseAsync(ScreenCastSession _session);
    }

    [SupportedOSPlatform("linux")]
    public sealed class ScreenCastPortal : IScreenCastPortal
    {
        private const string Interface = "org.freedesktop.portal.ScreenCast";

        private readonly PortalClient portal;
        private int sessions;

        public ScreenCastPortal(string? _address = null)
        {
            portal = new PortalClient(_address);
        }

        public async Task<ScreenCastSession?> StartAsync(string? _restoreToken, CancellationToken _ct)
        {
            DBusConnection? bus = null;
            string? session = null;
            try
            {
                bus = await portal.ConnectAsync(_ct).ConfigureAwait(false);
                if (bus is null)
                {
                    return null;
                }
                string sessionToken = $"hider{Environment.ProcessId}s{Interlocked.Increment(ref sessions)}";
                var created = Check("CreateSession", await portal.RequestAsync(bus, Interface, "CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
                {
                    var d = w.WriteDictionaryStart();
                    PortalClient.Entry(ref w, "handle_token", token);
                    PortalClient.Entry(ref w, "session_handle_token", sessionToken);
                    w.WriteDictionaryEnd(d);
                }, _ct).ConfigureAwait(false));
                session = created["session_handle"].GetString();

                Check("SelectSources", await portal.RequestAsync(bus, Interface, "SelectSources", "oa{sv}", (ref MessageWriter w, string token) =>
                {
                    w.WriteObjectPath(session);
                    var d = w.WriteDictionaryStart();
                    PortalClient.Entry(ref w, "handle_token", token);
                    w.WriteDictionaryEntryStart(); w.WriteString("types"); w.WriteVariantUInt32(1);        // monitor
                    w.WriteDictionaryEntryStart(); w.WriteString("multiple"); w.WriteVariantBool(false);
                    w.WriteDictionaryEntryStart(); w.WriteString("cursor_mode"); w.WriteVariantUInt32(1);  // hidden
                    w.WriteDictionaryEntryStart(); w.WriteString("persist_mode"); w.WriteVariantUInt32(2); // until revoked
                    if (_restoreToken is not null)
                    {
                        PortalClient.Entry(ref w, "restore_token", _restoreToken);
                    }
                    w.WriteDictionaryEnd(d);
                }, _ct).ConfigureAwait(false));

                var started = Check("Start", await portal.RequestAsync(bus, Interface, "Start", "osa{sv}", (ref MessageWriter w, string token) =>
                {
                    w.WriteObjectPath(session);
                    w.WriteString("");
                    var d = w.WriteDictionaryStart();
                    PortalClient.Entry(ref w, "handle_token", token);
                    w.WriteDictionaryEnd(d);
                }, _ct).ConfigureAwait(false));
                var (node, rect) = FirstStream(started["streams"]);
                string? restore = started.TryGetValue("restore_token", out var t) ? t.GetString() : null;

                MessageBuffer open;
                using (var w = bus.GetMessageWriter())
                {
                    w.WriteMethodCallHeader(PortalClient.Destination, PortalClient.ObjectPath, Interface, "OpenPipeWireRemote", "oa{sv}");
                    w.WriteObjectPath(session);
                    var d = w.WriteDictionaryStart();
                    w.WriteDictionaryEnd(d);
                    open = w.CreateMessage();
                }
                var fd = await bus.CallMethodAsync(open, (Message m, object? _) => m.GetBodyReader().ReadHandle<SafeFileHandle>()!).ConfigureAwait(false);
                Log.Verbose($"screencast: node {node}, stream {rect}, new restore token: {restore is not null}");
                return new ScreenCastSession(session, node, rect, restore, fd);
            }
            catch (Exception e)
            {
                // Whatever went wrong after CreateSession (cancelled, refused, failed), the half-made session must not stay
                // around: otherwise a dialog answered later starts a share that runs until Hider exits.
                if (bus is not null && session is not null)
                {
                    await PortalClient.CloseObjectAsync(bus, session, "org.freedesktop.portal.Session").ConfigureAwait(false);
                }
                if (e is OperationCanceledException or CaptureDeniedException)
                {
                    throw;
                }
                Log.Verbose($"screencast portal: {e.GetType().Name}: {e.Message}");
                portal.Reset();
                return null;
            }
        }

        public async Task CloseAsync(ScreenCastSession _session)
        {
            _session.PipeWireFd.Dispose();
            try
            {
                var bus = await portal.ConnectAsync(CancellationToken.None).ConfigureAwait(false);
                if (bus is not null)
                {
                    await PortalClient.CloseObjectAsync(bus, _session.Handle, "org.freedesktop.portal.Session").ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                Log.Verbose($"screencast close: {e.GetType().Name}: {e.Message}");
            }
        }

        private static Dictionary<string, VariantValue> Check(string _step, PortalClient.Response _response)
        {
            return _response.Code switch
            {
                0 => _response.Results,
                1 => throw new CaptureDeniedException($"screencast portal: {_step} cancelled by the user"),
                _ => throw new InvalidOperationException($"screencast portal: {_step} response {_response.Code}"),
            };
        }

        /// <summary>streams: a(ua{sv}) — node id and properties: position (ii), size (ii).</summary>
        private static (uint Node, PhysicalRect Rect) FirstStream(VariantValue _streams)
        {
            var first = _streams.GetItem(0);
            uint node = first.GetItem(0).GetUInt32();
            var props = first.GetItem(1);
            int x = 0, y = 0, w = 0, h = 0;
            for (int i = 0; i < props.Count; i++)
            {
                var entry = props.GetDictionaryEntry(i);
                switch (entry.Key.GetString())
                {
                    case "position":
                        x = entry.Value.GetItem(0).GetInt32();
                        y = entry.Value.GetItem(1).GetInt32();
                        break;
                    case "size":
                        w = entry.Value.GetItem(0).GetInt32();
                        h = entry.Value.GetItem(1).GetInt32();
                        break;
                }
            }
            return (node, new PhysicalRect(x, y, w, h));
        }

        public void Dispose()
        {
            portal.Dispose();
        }
    }
}
