using System.Diagnostics;
using System.Net.Sockets;
using Tmds.DBus.Protocol;

// Fake xdg-desktop-portal for Xvfb (scripts/xvfb-strip-smoke.sh):
//   FakePortal <screenshot dir> [ok|deny|hang] [screencast node id]   (hang: ScreenCast Start never answers)
// Screenshot shoots the root window with ImageMagick `import`. With a node id, ScreenCast is offered too: Start hands out
// that PipeWire node (e.g. a GStreamer ximagesrc ! pipewiresink) and OpenPipeWireRemote a connection to $XDG_RUNTIME_DIR.
var conn = new DBusConnection(DBusAddress.Session!);
await conn.ConnectAsync();
string dir = args.Length > 0 ? args[0] : "/tmp";
string mode = args.Length > 1 ? args[1] : "ok";
uint? node = args.Length > 2 ? uint.Parse(args[2]) : null;
conn.AddMethodHandler(new Portal(conn, dir, mode, node));
await conn.RequestNameAsync("org.freedesktop.portal.Desktop");
Console.WriteLine("fake portal ready");
await Task.Delay(Timeout.Infinite);

sealed class Portal(DBusConnection conn, string dir, string mode, uint? node) : IPathMethodHandler
{
    private int shots;
    private int starts;

    public string Path => "/org/freedesktop/portal/desktop";
    public bool HandlesChildPaths => true;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var req = context.Request;
        string member = req.MemberAsString!;
        switch (req.InterfaceAsString)
        {
            case "org.freedesktop.portal.Screenshot" when member == "Screenshot":
                Screenshot(context);
                break;
            case "org.freedesktop.portal.ScreenCast" when node is not null:
                ScreenCast(context, member);
                break;
            case "org.freedesktop.portal.Request" when member == "Close":
                Console.WriteLine($"request closed {req.PathAsString}");
                context.Reply(context.CreateReplyWriter(null).CreateMessage());
                break;
            case "org.freedesktop.portal.Session" when member == "Close":
                Console.WriteLine($"session closed {req.PathAsString}");
                context.Reply(context.CreateReplyWriter(null).CreateMessage());
                break;
            default:
                context.ReplyUnknownMethodError();
                break;
        }
        return ValueTask.CompletedTask;
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;

    private void Screenshot(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        reader.ReadString();
        string path = ReplyRequest(context, ReadOptions(ref reader));
        int i = Interlocked.Increment(ref shots);
        string sender = context.Request.SenderAsString!;
        _ = Task.Run(() =>
        {
            string file = System.IO.Path.Combine(dir, $"Screenshot-{i}.png");
            var sw = Stopwatch.StartNew();
            uint code = mode == "deny" ? 1u : 0u;
            if (code == 0)
            {
                var p = Process.Start(new ProcessStartInfo("import", $"-window root {file}") { RedirectStandardError = true })!;
                p.WaitForExit();
            }
            Console.WriteLine($"shot {i} {sw.ElapsedMilliseconds} ms code={code}");
            Respond(sender, path, code, (ref MessageWriter w) =>
            {
                if (code == 0)
                {
                    w.WriteDictionaryEntryStart();
                    w.WriteString("uri");
                    w.WriteVariantString("file://" + file);
                }
            });
        });
    }

    private void ScreenCast(MethodContext context, string member)
    {
        var reader = context.Request.GetBodyReader();
        string sender = context.Request.SenderAsString!;
        switch (member)
        {
            case "CreateSession":
            {
                var options = ReadOptions(ref reader);
                string path = ReplyRequest(context, options);
                string session = $"/org/freedesktop/portal/desktop/session/{Mangle(sender)}/{options.GetValueOrDefault("session_handle_token", "s")}";
                Respond(sender, path, 0, (ref MessageWriter w) =>
                {
                    w.WriteDictionaryEntryStart();
                    w.WriteString("session_handle");
                    w.WriteVariantString(session);
                });
                break;
            }
            case "SelectSources":
            {
                reader.ReadObjectPath();
                var options = ReadOptions(ref reader);
                Console.WriteLine($"select sources, restore token: {options.GetValueOrDefault("restore_token", "(none)")}");
                Respond(sender, ReplyRequest(context, options), mode == "deny" ? 1u : 0u, (ref MessageWriter _) => { });
                break;
            }
            case "Start":
            {
                reader.ReadObjectPath();
                reader.ReadString();
                string path = ReplyRequest(context, ReadOptions(ref reader));
                int n = Interlocked.Increment(ref starts);
                Console.WriteLine($"screencast start {n}");
                if (mode == "hang")
                {
                    break; // the dialog stays open: no Response until the client closes the request
                }
                Respond(sender, path, 0, (ref MessageWriter w) =>
                {
                    w.WriteDictionaryEntryStart();
                    w.WriteString("streams");
                    w.WriteSignature("a(ua{sv})");
                    var streams = w.WriteArrayStart(DBusType.Struct);
                    w.WriteStructureStart();
                    w.WriteUInt32(node!.Value);
                    var props = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("position");
                    w.WriteSignature("(ii)");
                    w.WriteStructureStart();
                    w.WriteInt32(0);
                    w.WriteInt32(0);
                    w.WriteDictionaryEntryStart();
                    w.WriteString("size");
                    w.WriteSignature("(ii)");
                    w.WriteStructureStart();
                    w.WriteInt32(ScreenSize.Width);
                    w.WriteInt32(ScreenSize.Height);
                    w.WriteDictionaryEnd(props);
                    w.WriteArrayEnd(streams);
                    w.WriteDictionaryEntryStart();
                    w.WriteString("restore_token");
                    w.WriteVariantString($"token-{n}");
                });
                break;
            }
            case "OpenPipeWireRemote":
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                socket.Connect(new UnixDomainSocketEndPoint(System.IO.Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")!, "pipewire-0")));
                using var w = context.CreateReplyWriter("h");
                w.WriteHandle(socket.SafeHandle);
                context.Reply(w.CreateMessage());
                socket.Dispose();
                Console.WriteLine("pipewire remote opened");
                break;
            }
            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    private static class ScreenSize
    {
        public static int Width => int.Parse(Environment.GetEnvironmentVariable("FAKEPORTAL_WIDTH") ?? "1280");
        public static int Height => int.Parse(Environment.GetEnvironmentVariable("FAKEPORTAL_HEIGHT") ?? "720");
    }

    private static Dictionary<string, string> ReadOptions(ref Reader reader)
    {
        var options = new Dictionary<string, string>();
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            string key = reader.ReadString();
            var value = reader.ReadVariantValue();
            if (value.Type == VariantValueType.String)
            {
                options[key] = value.GetString();
            }
        }
        return options;
    }

    private static string Mangle(string sender) => sender.TrimStart(':').Replace('.', '_');

    /// <summary>Replies with the Request object path built from handle_token, as the real portal does.</summary>
    private static string ReplyRequest(MethodContext context, Dictionary<string, string> options)
    {
        string path = $"/org/freedesktop/portal/desktop/request/{Mangle(context.Request.SenderAsString!)}/{options.GetValueOrDefault("handle_token", "t")}";
        using var w = context.CreateReplyWriter("o");
        w.WriteObjectPath(path);
        context.Reply(w.CreateMessage());
        return path;
    }

    private delegate void ResultsWriter(ref MessageWriter writer);

    private void Respond(string sender, string path, uint code, ResultsWriter results)
    {
        var w = conn.GetMessageWriter();
        try
        {
            w.WriteSignalHeader(sender, path, "org.freedesktop.portal.Request", "Response", "ua{sv}");
            w.WriteUInt32(code);
            var d = w.WriteDictionaryStart();
            results(ref w);
            w.WriteDictionaryEnd(d);
            conn.TrySendMessage(w.CreateMessage());
        }
        finally
        {
            w.Dispose();
        }
    }
}
