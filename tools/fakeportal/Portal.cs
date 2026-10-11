using System.Diagnostics;
using System.Net.Sockets;
using Tmds.DBus.Protocol;

namespace Hider.Tools.FakePortal
{
    /// <summary>org.freedesktop.portal.Screenshot, ScreenCast (with a node id), Request.Close and Session.Close.</summary>
    internal sealed class Portal(DBusConnection _conn, string _dir, string _mode, uint? _node) : IPathMethodHandler
    {
        private int shots;
        private int starts;

        public string Path => "/org/freedesktop/portal/desktop";
        public bool HandlesChildPaths => true;

        public ValueTask HandleMethodAsync(MethodContext _context)
        {
            var request = _context.Request;
            string member = request.MemberAsString!;
            switch (request.InterfaceAsString)
            {
                case "org.freedesktop.portal.Screenshot" when member == "Screenshot":
                    Screenshot(_context);
                    break;
                case "org.freedesktop.portal.ScreenCast" when _node is not null:
                    ScreenCast(_context, member);
                    break;
                case "org.freedesktop.portal.Request" when member == "Close":
                    Console.WriteLine($"request closed {request.PathAsString}");
                    _context.Reply(_context.CreateReplyWriter(null).CreateMessage());
                    break;
                case "org.freedesktop.portal.Session" when member == "Close":
                    Console.WriteLine($"session closed {request.PathAsString}");
                    _context.Reply(_context.CreateReplyWriter(null).CreateMessage());
                    break;
                default:
                    _context.ReplyUnknownMethodError();
                    break;
            }
            return ValueTask.CompletedTask;
        }

        public bool RunMethodHandlerSynchronously(Message _message) => true;

        private void Screenshot(MethodContext _context)
        {
            var reader = _context.Request.GetBodyReader();
            reader.ReadString();
            string path = ReplyRequest(_context, ReadOptions(ref reader));
            int i = Interlocked.Increment(ref shots);
            string sender = _context.Request.SenderAsString!;
            _ = Task.Run(() =>
            {
                string file = System.IO.Path.Combine(_dir, $"Screenshot-{i}.png");
                var sw = Stopwatch.StartNew();
                uint code = _mode == "deny" ? 1u : 0u;
                if (code == 0)
                {
                    Shoot(file);
                }
                Console.WriteLine($"shot {i} {sw.ElapsedMilliseconds} ms code={code}");
                Respond(sender, path, code, (ref MessageWriter _w) =>
                {
                    if (code == 0)
                    {
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("uri");
                        _w.WriteVariantString("file://" + file);
                    }
                });
            });
        }

        /// <summary>ImageMagick's import of the root window; its diagnostics go to our standard error.</summary>
        private static void Shoot(string _file)
        {
            var start = new ProcessStartInfo("import") { ArgumentList = { "-window", "root", _file } };
            using var process = Process.Start(start)!;
            process.WaitForExit();
        }

        private void ScreenCast(MethodContext _context, string _member)
        {
            var reader = _context.Request.GetBodyReader();
            string sender = _context.Request.SenderAsString!;
            switch (_member)
            {
                case "CreateSession":
                {
                    var options = ReadOptions(ref reader);
                    string path = ReplyRequest(_context, options);
                    string session = $"/org/freedesktop/portal/desktop/session/{Mangle(sender)}/{options.GetValueOrDefault("session_handle_token", "s")}";
                    Respond(sender, path, 0, (ref MessageWriter _w) =>
                    {
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("session_handle");
                        _w.WriteVariantString(session);
                    });
                    break;
                }
                case "SelectSources":
                {
                    reader.ReadObjectPath();
                    var options = ReadOptions(ref reader);
                    Console.WriteLine($"select sources, restore token: {options.GetValueOrDefault("restore_token", "(none)")}");
                    Respond(sender, ReplyRequest(_context, options), _mode == "deny" ? 1u : 0u, (ref MessageWriter _) => { });
                    break;
                }
                case "Start":
                {
                    reader.ReadObjectPath();
                    reader.ReadString();
                    string path = ReplyRequest(_context, ReadOptions(ref reader));
                    int n = Interlocked.Increment(ref starts);
                    Console.WriteLine($"screencast start {n}");
                    if (_mode == "hang")
                    {
                        break; // the dialog stays open: no Response until the client closes the request
                    }
                    Respond(sender, path, 0, (ref MessageWriter _w) =>
                    {
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("streams");
                        _w.WriteSignature("a(ua{sv})");
                        var streams = _w.WriteArrayStart(DBusType.Struct);
                        _w.WriteStructureStart();
                        _w.WriteUInt32(_node!.Value);
                        var props = _w.WriteDictionaryStart();
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("position");
                        _w.WriteSignature("(ii)");
                        _w.WriteStructureStart();
                        _w.WriteInt32(0);
                        _w.WriteInt32(0);
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("size");
                        _w.WriteSignature("(ii)");
                        _w.WriteStructureStart();
                        _w.WriteInt32(ScreenSize.Width);
                        _w.WriteInt32(ScreenSize.Height);
                        _w.WriteDictionaryEnd(props);
                        _w.WriteArrayEnd(streams);
                        _w.WriteDictionaryEntryStart();
                        _w.WriteString("restore_token");
                        _w.WriteVariantString($"token-{n}");
                    });
                    break;
                }
                case "OpenPipeWireRemote":
                {
                    using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    socket.Connect(new UnixDomainSocketEndPoint(System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, "pipewire-0")));
                    using var w = _context.CreateReplyWriter("h");
                    w.WriteHandle(socket.SafeHandle);
                    _context.Reply(w.CreateMessage());
                    Console.WriteLine("pipewire remote opened");
                    break;
                }
                default:
                    _context.ReplyUnknownMethodError();
                    break;
            }
        }

        private static class ScreenSize
        {
            public static int Width => int.Parse(Environment.GetEnvironmentVariable("FAKEPORTAL_WIDTH") ?? "1280", System.Globalization.CultureInfo.InvariantCulture);
            public static int Height => int.Parse(Environment.GetEnvironmentVariable("FAKEPORTAL_HEIGHT") ?? "720", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static Dictionary<string, string> ReadOptions(ref Reader _reader)
        {
            var options = new Dictionary<string, string>();
            var end = _reader.ReadDictionaryStart();
            while (_reader.HasNext(end))
            {
                string key = _reader.ReadString();
                var value = _reader.ReadVariantValue();
                if (value.Type == VariantValueType.String)
                {
                    options[key] = value.GetString();
                }
            }
            return options;
        }

        private static string Mangle(string _sender) => _sender.TrimStart(':').Replace('.', '_');

        /// <summary>Replies with the Request object path built from handle_token, as the real portal does.</summary>
        private static string ReplyRequest(MethodContext _context, Dictionary<string, string> _options)
        {
            string path = $"/org/freedesktop/portal/desktop/request/{Mangle(_context.Request.SenderAsString!)}/{_options.GetValueOrDefault("handle_token", "t")}";
            using var w = _context.CreateReplyWriter("o");
            w.WriteObjectPath(path);
            _context.Reply(w.CreateMessage());
            return path;
        }

        private delegate void ResultsWriter(ref MessageWriter _writer);

        private void Respond(string _sender, string _path, uint _code, ResultsWriter _results)
        {
            var w = _conn.GetMessageWriter();
            try
            {
                w.WriteSignalHeader(_sender, _path, "org.freedesktop.portal.Request", "Response", "ua{sv}");
                w.WriteUInt32(_code);
                var d = w.WriteDictionaryStart();
                _results(ref w);
                w.WriteDictionaryEnd(d);
                _conn.TrySendMessage(w.CreateMessage());
            }
            finally
            {
                w.Dispose();
            }
        }
    }
}
