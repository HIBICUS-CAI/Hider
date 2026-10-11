using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// xdg-desktop-portal calls (org.freedesktop.portal.Desktop on the session bus). A portal method returns a Request
    /// object at once and answers later with its Response signal (code 0 = ok, 1 = cancelled by the user, 2 = other). The
    /// connection is made on first use and dropped by <see cref="Reset"/> after an error.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class PortalClient : IDisposable
    {
        public const string Destination = "org.freedesktop.portal.Desktop";
        public const string ObjectPath = "/org/freedesktop/portal/desktop";

        /// <summary>Long enough for a first-time permission dialog.</summary>
        public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(120);

        private readonly string? address;
        private readonly SemaphoreSlim connectLock = new(1, 1);
        private DBusConnection? connection;
        private int tokens;

        /// <param name="_address">D-Bus address; null for the session bus.</param>
        public PortalClient(string? _address = null)
        {
            address = _address ?? DBusAddress.Session;
        }

        public delegate void BodyWriter(ref MessageWriter _writer, string _handleToken);

        public sealed record Response(uint Code, Dictionary<string, VariantValue> Results);

        /// <summary>The connection, connecting first if needed; null when there is no session bus.</summary>
        public async Task<DBusConnection?> ConnectAsync(CancellationToken _ct)
        {
            if (address is null)
            {
                return null;
            }
            await connectLock.WaitAsync(_ct).ConfigureAwait(false);
            try
            {
                if (connection is null)
                {
                    var bus = new DBusConnection(address);
                    try
                    {
                        await bus.ConnectAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        bus.Dispose();
                        throw;
                    }
                    connection = bus;
                }
                return connection;
            }
            finally
            {
                connectLock.Release();
            }
        }

        /// <summary>
        /// Calls <paramref name="_interface"/>.<paramref name="_method"/> and waits for its Response. The body writer gets
        /// the handle_token to put into the options dictionary.
        /// </summary>
        public async Task<Response> RequestAsync(DBusConnection _bus, string _interface, string _method, string _signature, BodyWriter _body, CancellationToken _ct)
        {
            string token = $"hider{Environment.ProcessId}_{Interlocked.Increment(ref tokens)}";
            string sender = _bus.UniqueName!.TrimStart(':').Replace('.', '_');
            string requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
            var response = new TaskCompletionSource<Response>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Subscribe before calling, so a fast response is not missed.
            using var match = await _bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = requestPath },
                (Message _m, object? _) => ReadResponse(_m),
                (Notification<Response> _n) =>
                {
                    if (_n.Type == NotificationType.Value)
                    {
                        response.TrySetResult(_n.Value);
                    }
                    else if (!_n.IsCompletion)
                    {
                        response.TrySetException(_n.Exception);
                    }
                },
                emitOnCapturedContext: false).ConfigureAwait(false);

            MessageBuffer call;
            var writer = _bus.GetMessageWriter();
            try
            {
                writer.WriteMethodCallHeader(Destination, ObjectPath, _interface, _method, _signature);
                _body(ref writer, token);
                call = writer.CreateMessage();
            }
            finally
            {
                writer.Dispose();
            }
            await _bus.CallMethodAsync(call).ConfigureAwait(false);
            try
            {
                return await response.Task.WaitAsync(ResponseTimeout, _ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is OperationCanceledException or TimeoutException)
            {
                // Nobody waits any more: dismiss the dialog, or the user could still start a screen share no one stops.
                await CloseObjectAsync(_bus, requestPath, "org.freedesktop.portal.Request").ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Calls Close on a portal Request or Session object; failures are only logged.</summary>
        public static async Task CloseObjectAsync(DBusConnection _bus, string _path, string _interface)
        {
            try
            {
                MessageBuffer close;
                using (var writer = _bus.GetMessageWriter())
                {
                    writer.WriteMethodCallHeader(Destination, _path, _interface, "Close", null);
                    close = writer.CreateMessage();
                }
                await _bus.CallMethodAsync(close).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Hider.Core.Log.Verbose($"portal: closing {_path} failed: {e.Message}");
            }
        }

        /// <summary>Writes a string entry of an a{sv} options dictionary.</summary>
        public static void Entry(ref MessageWriter _writer, string _key, string _value)
        {
            _writer.WriteDictionaryEntryStart();
            _writer.WriteString(_key);
            _writer.WriteVariantString(_value);
        }

        private static Response ReadResponse(Message _message)
        {
            var reader = _message.GetBodyReader();
            uint code = reader.ReadUInt32();
            var results = new Dictionary<string, VariantValue>();
            var end = reader.ReadDictionaryStart();
            while (reader.HasNext(end))
            {
                string key = reader.ReadString();
                results[key] = reader.ReadVariantValue();
            }
            return new Response(code, results);
        }

        /// <summary>Drops the connection after an error; the next call connects again.</summary>
        public void Reset()
        {
            Interlocked.Exchange(ref connection, null)?.Dispose();
        }

        public void Dispose()
        {
            Reset();
            connectLock.Dispose();
        }
    }
}
