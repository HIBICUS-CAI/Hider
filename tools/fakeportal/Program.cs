using System.Diagnostics;
using Tmds.DBus.Protocol;

// Fake org.freedesktop.portal.Screenshot for Xvfb: shoots the root window with ImageMagick `import`.
var conn = new DBusConnection(DBusAddress.Session!);
await conn.ConnectAsync();
string dir = args.Length > 0 ? args[0] : "/tmp";
string mode = args.Length > 1 ? args[1] : "ok";
conn.AddMethodHandler(new Portal(conn, dir, mode));
await conn.RequestNameAsync("org.freedesktop.portal.Desktop");
Console.WriteLine("fake portal ready");
await Task.Delay(Timeout.Infinite);

sealed class Portal(DBusConnection conn, string dir, string mode) : IPathMethodHandler
{
    int n;
    public string Path => "/org/freedesktop/portal/desktop";
    public bool HandlesChildPaths => false;

    public ValueTask HandleMethodAsync(MethodContext context)
    {
        var req = context.Request;
        if (req.InterfaceAsString != "org.freedesktop.portal.Screenshot" || req.MemberAsString != "Screenshot")
        {
            context.ReplyUnknownMethodError();
            return ValueTask.CompletedTask;
        }
        var reader = req.GetBodyReader();
        reader.ReadString();
        string token = "t";
        var end = reader.ReadDictionaryStart();
        while (reader.HasNext(end))
        {
            string key = reader.ReadString();
            var v = reader.ReadVariantValue();
            if (key == "handle_token") token = v.GetString();
        }
        string sender = req.SenderAsString!;
        string path = $"/org/freedesktop/portal/desktop/request/{sender.TrimStart(':').Replace('.', '_')}/{token}";
        using (var w = context.CreateReplyWriter("o"))
        {
            w.WriteObjectPath(path);
            context.Reply(w.CreateMessage());
        }
        int i = Interlocked.Increment(ref n);
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
            using var s = conn.GetMessageWriter();
            s.WriteSignalHeader(sender, path, "org.freedesktop.portal.Request", "Response", "ua{sv}");
            s.WriteUInt32(code);
            var d = s.WriteDictionaryStart();
            if (code == 0)
            {
                s.WriteDictionaryEntryStart();
                s.WriteString("uri");
                s.WriteVariantString("file://" + file);
            }
            s.WriteDictionaryEnd(d);
            conn.TrySendMessage(s.CreateMessage());
        });
        return ValueTask.CompletedTask;
    }

    public bool RunMethodHandlerSynchronously(Message message) => true;
}
