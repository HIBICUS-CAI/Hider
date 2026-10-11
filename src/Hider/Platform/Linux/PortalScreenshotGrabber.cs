using Hider.Core;
using Hider.Core.Capture;
using SkiaSharp;
using Tmds.DBus.Protocol;

namespace Hider.Platform.Linux
{
    /// <summary>
    /// Linux stage 1 (D-6): org.freedesktop.portal.Screenshot with interactive=false. GNOME asks for permission once and
    /// remembers it (S-5). The portal writes a PNG and returns its URI; it is read and deleted at once, so no snapshot stays
    /// on disk (FR-53). Each call takes 0.1 .. 0.3 s (design §5.3), which is why stage 2 exists.
    /// </summary>
    public sealed class PortalScreenshotGrabber : IScreenGrabber
    {
        /// <summary>Long enough for the first-time permission dialog.</summary>
        public static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(60);

        private readonly string? address;
        private readonly SemaphoreSlim connectLock = new(1, 1);
        private DBusConnection? connection;
        private int requests;

        /// <param name="_address">D-Bus address; null for the session bus.</param>
        public PortalScreenshotGrabber(string? _address = null)
        {
            address = _address ?? DBusAddress.Session;
        }

        /// <summary>
        /// The portal shoots when called, and the coordinator calls after the windows have shown their transparent rows, so
        /// every result is newer than <paramref name="_notBeforeTimestamp"/>.
        /// </summary>
        public async Task<FrameBuffer?> GrabAfterAsync(long _notBeforeTimestamp, CancellationToken _ct)
        {
            string? path = null;
            try
            {
                var bus = await ConnectAsync(_ct).ConfigureAwait(false);
                if (bus is null)
                {
                    return null;
                }
                var (code, uri) = await ScreenshotAsync(bus, _ct).ConfigureAwait(false);
                if (uri is not null && uri.StartsWith("file://", StringComparison.Ordinal))
                {
                    path = Uri.UnescapeDataString(new Uri(uri).AbsolutePath);
                }
                switch (code)
                {
                    case 0:
                        return path is null ? null : ReadPng(path);
                    case 1:
                        throw new CaptureDeniedException("screenshot portal: cancelled by the user");
                    default:
                        Log.Verbose($"screenshot portal: response {code}");
                        return null;
                }
            }
            catch (Exception e) when (e is not (OperationCanceledException or CaptureDeniedException))
            {
                Log.Verbose($"screenshot portal: {e.GetType().Name}: {e.Message}");
                DropConnection();
                return null;
            }
            finally
            {
                if (path is not null)
                {
                    TryDelete(path);
                }
            }
        }

        private async Task<DBusConnection?> ConnectAsync(CancellationToken _ct)
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

        private void DropConnection()
        {
            var old = Interlocked.Exchange(ref connection, null);
            old?.Dispose();
        }

        private async Task<(uint Code, string? Uri)> ScreenshotAsync(DBusConnection _bus, CancellationToken _ct)
        {
            string token = $"hider{Environment.ProcessId}_{Interlocked.Increment(ref requests)}";
            string sender = _bus.UniqueName!.TrimStart(':').Replace('.', '_');
            string requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
            var response = new TaskCompletionSource<(uint, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Subscribe before calling, so a fast response is not missed.
            using var match = await _bus.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = requestPath },
                (Message m, object? _) => ReadResponse(m),
                (Notification<(uint, string?)> n) =>
                {
                    if (n.Type == NotificationType.Value)
                    {
                        response.TrySetResult(n.Value);
                    }
                    else if (!n.IsCompletion)
                    {
                        response.TrySetException(n.Exception);
                    }
                },
                emitOnCapturedContext: false).ConfigureAwait(false);

            MessageBuffer call;
            using (var writer = _bus.GetMessageWriter())
            {
                writer.WriteMethodCallHeader("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop", "org.freedesktop.portal.Screenshot", "Screenshot", "sa{sv}");
                writer.WriteString("");
                var dict = writer.WriteDictionaryStart();
                writer.WriteDictionaryEntryStart();
                writer.WriteString("handle_token");
                writer.WriteVariantString(token);
                writer.WriteDictionaryEntryStart();
                writer.WriteString("interactive");
                writer.WriteVariantBool(false);
                writer.WriteDictionaryEnd(dict);
                call = writer.CreateMessage();
            }
            await _bus.CallMethodAsync(call, (Message m, object? _) => m.GetBodyReader().ReadObjectPath().ToString()).ConfigureAwait(false);
            return await response.Task.WaitAsync(ResponseTimeout, _ct).ConfigureAwait(false);
        }

        private static (uint, string?) ReadResponse(Message _message)
        {
            var reader = _message.GetBodyReader();
            uint code = reader.ReadUInt32();
            string? uri = null;
            var end = reader.ReadDictionaryStart();
            while (reader.HasNext(end))
            {
                string key = reader.ReadString();
                var value = reader.ReadVariantValue();
                if (key == "uri")
                {
                    uri = value.GetString();
                }
            }
            return (code, uri);
        }

        /// <summary>Decodes the portal's PNG into a premultiplied BGRA frame (null if it cannot be read).</summary>
        internal static FrameBuffer? ReadPng(string _path)
        {
            using var codec = SKCodec.Create(_path);
            if (codec is null)
            {
                return null;
            }
            var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var frame = FrameBuffer.Allocate(info.Width, info.Height);
            unsafe
            {
                fixed (byte* pixels = frame.Bgra.Span)
                {
                    var result = codec.GetPixels(info, (nint)pixels);
                    return result is SKCodecResult.Success or SKCodecResult.IncompleteInput ? frame : null;
                }
            }
        }

        private static void TryDelete(string _path)
        {
            try
            {
                File.Delete(_path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Verbose($"could not delete the portal screenshot {_path}: {e.Message}");
            }
        }

        public void Dispose()
        {
            DropConnection();
            connectLock.Dispose();
        }
    }
}
