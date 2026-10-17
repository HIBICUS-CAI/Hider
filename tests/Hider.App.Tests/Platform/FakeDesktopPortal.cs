using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tmds.DBus.Protocol;

namespace Hider.App.Tests.Platform
{
    /// <summary>
    /// A scriptable org.freedesktop.portal.Desktop on a private bus (the protocol as tools/fakeportal and xdg-desktop-portal
    /// speak it): a portal method replies with its Request object path, built from handle_token, and answers later with the
    /// Request's Response signal. What each request answers is up to <see cref="OnRequest"/>; every call and every Close
    /// (Request or Session) is recorded.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class FakeDesktopPortal : IPathMethodHandler, IDisposable
    {
        public const string RequestInterface = "org.freedesktop.portal.Request";
        public const string SessionInterface = "org.freedesktop.portal.Session";

        private readonly DBusConnection connection;
        private readonly object sync = new();
        private readonly List<PortalCall> calls = new();
        private readonly List<(string Interface, string Path)> closed = new();

        private FakeDesktopPortal(DBusConnection _connection)
        {
            connection = _connection;
        }

        public delegate void ResultsWriter(ref MessageWriter _writer);

        /// <summary>A portal method call: its interface, member, the object path and string arguments before the options, the options.</summary>
        public sealed record PortalCall(string Interface, string Member, string Target, string Sender, string? RequestPath, IReadOnlyList<string> Arguments, IReadOnlyDictionary<string, VariantValue> Options)
        {
            public string? Option(string _key) => Options.TryGetValue(_key, out var v) && v.Type == VariantValueType.String ? v.GetString() : null;
        }

        /// <summary>What the portal does with a request.</summary>
        public abstract record Answer
        {
            /// <summary>Response signal with <paramref name="Code"/> and the results the writer adds (none when null).</summary>
            public sealed record Respond(uint Code, ResultsWriter? Results = null) : Answer;

            /// <summary>The dialog stays open: the method replies, the Response never comes.</summary>
            public sealed record Silent : Answer;

            /// <summary>A Response signal whose body is not ua{sv}.</summary>
            public sealed record Malformed : Answer;

            /// <summary>
            /// The Response signal sent before the method's reply (a portal that answers at once, without a dialog, may
            /// do so): only a client that subscribed before calling sees it.
            /// </summary>
            public sealed record RespondBeforeReply(uint Code, ResultsWriter? Results = null) : Answer;

            /// <summary>A Response sent to another request path first (to be ignored), then the real one.</summary>
            public sealed record ElsewhereFirst(uint Code, ResultsWriter? Results = null) : Answer;

            /// <summary>The method call itself fails with a D-Bus error.</summary>
            public sealed record Fail(string Name) : Answer;

            public static Answer Ok(ResultsWriter? _results = null) => new Respond(0, _results);
        }

        /// <summary>How each request is answered (default: code 0, no results).</summary>
        public Func<PortalCall, Answer> OnRequest { get; set; } = _ => Answer.Ok();

        /// <summary>The fd OpenPipeWireRemote hands out; an error reply when it returns null. Sent as a duplicate.</summary>
        public Func<SafeHandle?> OnOpenPipeWireRemote { get; set; } = () => null;

        /// <summary>Called after a request was replied to (and answered, if its answer says so).</summary>
        public Action<PortalCall>? Replied { get; set; }

        /// <summary>Close (Request or Session) fails with a D-Bus error (still recorded).</summary>
        public bool FailClose { get; set; }

        public string UniqueName => connection.UniqueName!;

        public IReadOnlyList<PortalCall> Calls
        {
            get
            {
                lock (sync)
                {
                    return calls.ToList();
                }
            }
        }

        public IReadOnlyList<(string Interface, string Path)> Closed
        {
            get
            {
                lock (sync)
                {
                    return closed.ToList();
                }
            }
        }

        public string Path => "/org/freedesktop/portal/desktop";

        public bool HandlesChildPaths => true;

        public static async Task<FakeDesktopPortal> StartAsync(string _address, string _name = "org.freedesktop.portal.Desktop")
        {
            var connection = new DBusConnection(_address);
            await connection.ConnectAsync();
            var portal = new FakeDesktopPortal(connection);
            connection.AddMethodHandler(portal);
            await connection.RequestNameAsync(_name);
            return portal;
        }

        /// <summary>A connection that owns <paramref name="_name"/> (another service on the bus, e.g. the tray watcher).</summary>
        public static async Task<DBusConnection> OwnNameAsync(string _address, string _name)
        {
            var connection = new DBusConnection(_address);
            await connection.ConnectAsync();
            await connection.RequestNameAsync(_name);
            return connection;
        }

        public ValueTask HandleMethodAsync(MethodContext _context)
        {
            var request = _context.Request;
            string iface = request.InterfaceAsString ?? "";
            string member = request.MemberAsString ?? "";
            if (member == "Close" && iface is RequestInterface or SessionInterface)
            {
                lock (sync)
                {
                    closed.Add((iface, request.PathAsString!));
                }
                if (FailClose)
                {
                    _context.ReplyError("org.freedesktop.DBus.Error.Failed", "close failed (test)");
                }
                else
                {
                    _context.Reply(_context.CreateReplyWriter(null).CreateMessage());
                }
                return ValueTask.CompletedTask;
            }
            if (iface == "org.freedesktop.portal.ScreenCast" && member == "OpenPipeWireRemote")
            {
                OpenPipeWireRemote(_context);
                return ValueTask.CompletedTask;
            }

            var reader = request.GetBodyReader();
            var arguments = new List<string>();
            string signature = request.SignatureAsString ?? "";
            foreach (char c in signature.Replace("a{sv}", "", StringComparison.Ordinal))
            {
                arguments.Add(c == 'o' ? reader.ReadObjectPathAsString() : reader.ReadString());
            }
            var options = signature.EndsWith("a{sv}", StringComparison.Ordinal) ? ReadOptions(ref reader) : new Dictionary<string, VariantValue>();
            string sender = request.SenderAsString!;
            string? token = options.TryGetValue("handle_token", out var t) ? t.GetString() : null;
            string? requestPath = token is null ? null : RequestPathFor(sender, token);
            var call = new PortalCall(iface, member, request.PathAsString!, sender, requestPath, arguments, options);
            lock (sync)
            {
                calls.Add(call);
            }

            var answer = OnRequest(call);
            if (answer is Answer.Fail error)
            {
                _context.ReplyError(error.Name, "request failed (test)");
                return ValueTask.CompletedTask;
            }
            if (answer is Answer.RespondBeforeReply early)
            {
                // Same connection, so the bus delivers the signal before the reply.
                Respond(sender, requestPath!, early.Code, early.Results);
            }
            using (var w = _context.CreateReplyWriter("o"))
            {
                w.WriteObjectPath(requestPath ?? "/org/freedesktop/portal/desktop/request/none");
                _context.Reply(w.CreateMessage());
            }
            switch (answer)
            {
                case Answer.Respond respond:
                    Respond(sender, requestPath!, respond.Code, respond.Results);
                    break;
                case Answer.ElsewhereFirst elsewhere:
                    Respond(sender, requestPath! + "_other", 1, null);
                    Respond(sender, requestPath!, elsewhere.Code, elsewhere.Results);
                    break;
                case Answer.Malformed:
                    SendMalformed(sender, requestPath!);
                    break;
            }
            Replied?.Invoke(call);
            return ValueTask.CompletedTask;
        }

        public static string RequestPathFor(string _sender, string _token)
        {
            return $"/org/freedesktop/portal/desktop/request/{_sender.TrimStart(':').Replace('.', '_')}/{_token}";
        }

        private void OpenPipeWireRemote(MethodContext _context)
        {
            var reader = _context.Request.GetBodyReader();
            string session = reader.ReadObjectPathAsString();
            var options = ReadOptions(ref reader);
            lock (sync)
            {
                calls.Add(new PortalCall("org.freedesktop.portal.ScreenCast", "OpenPipeWireRemote", _context.Request.PathAsString!, _context.Request.SenderAsString!, null, new[] { session }, options));
            }
            using var handle = OnOpenPipeWireRemote();
            if (handle is null)
            {
                _context.ReplyError("org.freedesktop.portal.Error.Failed", "no remote (test)");
                return;
            }
            using var w = _context.CreateReplyWriter("h");
            w.WriteHandle(handle);
            _context.Reply(w.CreateMessage());
        }

        private static Dictionary<string, VariantValue> ReadOptions(ref Reader _reader)
        {
            var options = new Dictionary<string, VariantValue>();
            var end = _reader.ReadDictionaryStart();
            while (_reader.HasNext(end))
            {
                string key = _reader.ReadString();
                options[key] = _reader.ReadVariantValue();
            }
            return options;
        }

        private void Respond(string _sender, string _path, uint _code, ResultsWriter? _results)
        {
            var w = connection.GetMessageWriter();
            try
            {
                w.WriteSignalHeader(_sender, _path, RequestInterface, "Response", "ua{sv}");
                w.WriteUInt32(_code);
                var d = w.WriteDictionaryStart();
                _results?.Invoke(ref w);
                w.WriteDictionaryEnd(d);
                connection.TrySendMessage(w.CreateMessage());
            }
            finally
            {
                w.Dispose();
            }
        }

        private void SendMalformed(string _sender, string _path)
        {
            using var w = connection.GetMessageWriter();
            w.WriteSignalHeader(_sender, _path, RequestInterface, "Response", "s");
            w.WriteString("not a response");
            connection.TrySendMessage(w.CreateMessage());
        }

        /// <summary>Writes a string entry of the results.</summary>
        public static void Entry(ref MessageWriter _w, string _key, string _value)
        {
            _w.WriteDictionaryEntryStart();
            _w.WriteString(_key);
            _w.WriteVariantString(_value);
        }

        /// <summary>Writes a uint32 entry of the results.</summary>
        public static void Entry(ref MessageWriter _w, string _key, uint _value)
        {
            _w.WriteDictionaryEntryStart();
            _w.WriteString(_key);
            _w.WriteVariantUInt32(_value);
        }

        /// <summary>
        /// Writes "streams" a(ua{sv}): one stream with the node id and, when given, position and size (ii), plus a string
        /// property the client does not know; and a second stream after it when <paramref name="_second"/> is given.
        /// </summary>
        public static void Streams(ref MessageWriter _w, uint _node, (int X, int Y)? _position, (int W, int H)? _size,
            (uint Node, (int X, int Y) Position, (int W, int H) Size)? _second = null)
        {
            _w.WriteDictionaryEntryStart();
            _w.WriteString("streams");
            _w.WriteSignature("a(ua{sv})");
            var streams = _w.WriteArrayStart(DBusType.Struct);
            Stream(ref _w, _node, "monitor-0", _position, _size);
            if (_second is { } second)
            {
                Stream(ref _w, second.Node, "monitor-1", second.Position, second.Size);
            }
            _w.WriteArrayEnd(streams);
        }

        private static void Stream(ref MessageWriter _w, uint _node, string _id, (int X, int Y)? _position, (int W, int H)? _size)
        {
            _w.WriteStructureStart();
            _w.WriteUInt32(_node);
            var props = _w.WriteDictionaryStart();
            Entry(ref _w, "id", _id);
            if (_position is { } p)
            {
                Pair(ref _w, "position", p.X, p.Y);
            }
            if (_size is { } s)
            {
                Pair(ref _w, "size", s.W, s.H);
            }
            Entry(ref _w, "source_type", 1u);
            _w.WriteDictionaryEnd(props);
        }

        private static void Pair(ref MessageWriter _w, string _key, int _a, int _b)
        {
            _w.WriteDictionaryEntryStart();
            _w.WriteString(_key);
            _w.WriteSignature("(ii)");
            _w.WriteStructureStart();
            _w.WriteInt32(_a);
            _w.WriteInt32(_b);
        }

        public void Dispose()
        {
            connection.Dispose();
        }
    }
}
