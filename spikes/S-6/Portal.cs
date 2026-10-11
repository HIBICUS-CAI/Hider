using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace Spike
{
    /// <summary>org.freedesktop.portal.ScreenCast: one monitor stream with persist_mode 2 (prototype of Step 8's session).</summary>
    internal sealed class ScreenCastPortal : IDisposable
    {
        private const string Destination = "org.freedesktop.portal.Desktop";
        private const string ObjectPath = "/org/freedesktop/portal/desktop";
        private const string Interface = "org.freedesktop.portal.ScreenCast";

        private readonly DBusConnection connection;
        private string sender = "";
        private int tokenCounter;

        public ScreenCastPortal()
        {
            connection = new DBusConnection(DBusAddress.Session!);
        }

        public sealed record Stream(uint NodeId, int X, int Y, int Width, int Height);

        public sealed record Session(string Handle, Stream Stream, string? RestoreToken, SafeFileHandle PipeWireFd);

        public async Task<Session> StartAsync(string? _restoreToken)
        {
            await connection.ConnectAsync();
            sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');

            var created = await RequestAsync("CreateSession", "a{sv}", (ref MessageWriter w, string token) =>
            {
                var d = w.WriteDictionaryStart();
                Entry(ref w, "handle_token", token);
                Entry(ref w, "session_handle_token", $"hiders6{Environment.ProcessId}");
                w.WriteDictionaryEnd(d);
            });
            string session = created["session_handle"].GetString();

            await RequestAsync("SelectSources", "oa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                var d = w.WriteDictionaryStart();
                Entry(ref w, "handle_token", token);
                w.WriteDictionaryEntryStart(); w.WriteString("types"); w.WriteVariantUInt32(1);        // MONITOR
                w.WriteDictionaryEntryStart(); w.WriteString("multiple"); w.WriteVariantBool(false);
                w.WriteDictionaryEntryStart(); w.WriteString("cursor_mode"); w.WriteVariantUInt32(1);  // hidden
                w.WriteDictionaryEntryStart(); w.WriteString("persist_mode"); w.WriteVariantUInt32(2); // until revoked
                if (_restoreToken is not null)
                {
                    Entry(ref w, "restore_token", _restoreToken);
                }
                w.WriteDictionaryEnd(d);
            });

            var started = await RequestAsync("Start", "osa{sv}", (ref MessageWriter w, string token) =>
            {
                w.WriteObjectPath(session);
                w.WriteString("");
                var d = w.WriteDictionaryStart();
                Entry(ref w, "handle_token", token);
                w.WriteDictionaryEnd(d);
            });
            var stream = ParseFirstStream(started["streams"]);
            string? restore = started.TryGetValue("restore_token", out var t) ? t.GetString() : null;

            MessageBuffer open;
            using (var w = connection.GetMessageWriter())
            {
                w.WriteMethodCallHeader(Destination, ObjectPath, Interface, "OpenPipeWireRemote", "oa{sv}");
                w.WriteObjectPath(session);
                var d = w.WriteDictionaryStart();
                w.WriteDictionaryEnd(d);
                open = w.CreateMessage();
            }
            var fd = await connection.CallMethodAsync(open, (Message m, object? _) => m.GetBodyReader().ReadHandle<SafeFileHandle>()!);
            return new Session(session, stream, restore, fd);
        }

        private delegate void BodyWriter(ref MessageWriter _writer, string _token);

        private async Task<Dictionary<string, VariantValue>> RequestAsync(string _method, string _signature, BodyWriter _body)
        {
            string token = $"hiders6_{Environment.ProcessId}_{++tokenCounter}";
            string requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
            var response = new TaskCompletionSource<(uint, Dictionary<string, VariantValue>)>();
            using var match = await connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = requestPath },
                (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    uint code = r.ReadUInt32();
                    var results = new Dictionary<string, VariantValue>();
                    var end = r.ReadDictionaryStart();
                    while (r.HasNext(end))
                    {
                        string key = r.ReadString();
                        results[key] = r.ReadVariantValue();
                    }
                    return (code, results);
                },
                (Notification<(uint, Dictionary<string, VariantValue>)> n) =>
                {
                    if (n.Type == NotificationType.Value) response.TrySetResult(n.Value);
                    else if (!n.IsCompletion) response.TrySetException(n.Exception);
                },
                emitOnCapturedContext: false);

            MessageBuffer call;
            var w = connection.GetMessageWriter();
            try
            {
                w.WriteMethodCallHeader(Destination, ObjectPath, Interface, _method, _signature);
                _body(ref w, token);
                call = w.CreateMessage();
            }
            finally
            {
                w.Dispose();
            }
            await connection.CallMethodAsync(call);
            var (code, results) = await response.Task.WaitAsync(TimeSpan.FromMinutes(2));
            if (code != 0)
            {
                throw new InvalidOperationException($"{_method}: response {code} (1 = cancelled / denied)");
            }
            return results;
        }

        private static void Entry(ref MessageWriter _w, string _key, string _value)
        {
            _w.WriteDictionaryEntryStart();
            _w.WriteString(_key);
            _w.WriteVariantString(_value);
        }

        private static Stream ParseFirstStream(VariantValue _streams)
        {
            // a(ua{sv}): node id + properties (position (ii), size (ii), source_type u)
            var first = _streams.GetItem(0);
            uint node = first.GetItem(0).GetUInt32();
            var props = first.GetItem(1);
            int x = 0, y = 0, w = 0, h = 0;
            for (int i = 0; i < props.Count; i++)
            {
                var entry = props.GetDictionaryEntry(i);
                string key = entry.Key.GetString();
                if (key == "position")
                {
                    x = entry.Value.GetItem(0).GetInt32();
                    y = entry.Value.GetItem(1).GetInt32();
                }
                else if (key == "size")
                {
                    w = entry.Value.GetItem(0).GetInt32();
                    h = entry.Value.GetItem(1).GetInt32();
                }
            }
            return new Stream(node, x, y, w, h);
        }

        public void Dispose()
        {
            connection.Dispose();
        }
    }
}
