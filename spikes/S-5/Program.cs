using System.Diagnostics;
using Tmds.DBus.Protocol;

namespace Spike
{
    /// <summary>
    /// S-5 (design §10): call org.freedesktop.portal.Screenshot.Screenshot (interactive=false) N times and report the
    /// response code, latency and image size. The first call may show a permission dialog; later calls should not.
    ///   S5.PortalScreenshot [count=3]
    /// The portal writes the screenshot to a file and returns its URI; the spike reads the PNG size and deletes the file
    /// (Hider must never keep snapshots on disk, FR-53).
    /// </summary>
    internal static class Program
    {
        public static async Task<int> Main(string[] _args)
        {
            int count = _args.Length > 0 && int.TryParse(_args[0], out int n) ? n : 3;
            using var connection = new DBusConnection(DBusAddress.Session!);
            await connection.ConnectAsync();
            string sender = connection.UniqueName!.TrimStart(':').Replace('.', '_');
            Console.WriteLine($"session bus as {connection.UniqueName}");

            for (int i = 1; i <= count; i++)
            {
                string token = $"hider{Environment.ProcessId}_{i}";
                string requestPath = $"/org/freedesktop/portal/desktop/request/{sender}/{token}";
                var response = new TaskCompletionSource<(uint Code, string? Uri)>();
                // Subscribe before calling so a fast response is not missed.
                using var match = await connection.AddMatchAsync(
                    new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = requestPath },
                    (Message m, object? _) => ReadResponse(m),
                    (Notification<(uint, string?)> notification) =>
                    {
                        if (notification.Type == NotificationType.Value)
                        {
                            response.TrySetResult(notification.Value);
                        }
                        else if (!notification.IsCompletion)
                        {
                            response.TrySetException(notification.Exception);
                        }
                    },
                    emitOnCapturedContext: false);

                var sw = Stopwatch.StartNew();
                MessageBuffer call;
                using (var writer = connection.GetMessageWriter())
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
                string handle = await connection.CallMethodAsync(call, (Message m, object? _) => m.GetBodyReader().ReadObjectPath().ToString());
                var (code, uri) = await response.Task.WaitAsync(TimeSpan.FromSeconds(60));
                double ms = sw.Elapsed.TotalMilliseconds;
                string size = "-";
                if (uri is not null && uri.StartsWith("file://"))
                {
                    string path = Uri.UnescapeDataString(new Uri(uri).AbsolutePath);
                    size = PngSize(path);
                    File.Delete(path);
                }
                Console.WriteLine($"#{i}: response={code} ({(code == 0 ? "ok" : code == 1 ? "cancelled/denied" : "other")}) {ms:F0} ms image={size} handle-match={handle == requestPath}");
            }
            return 0;
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

        private static string PngSize(string _path)
        {
            using var f = File.OpenRead(_path);
            var header = new byte[24];
            f.ReadExactly(header);
            int w = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int h = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];
            return $"{w}x{h}";
        }
    }
}
