using Tmds.DBus.Protocol;

namespace Hider.Tools.FakePortal
{
    /// <summary>
    /// Fake xdg-desktop-portal for Xvfb (scripts/xvfb-strip-smoke.sh):
    ///   FakePortal &lt;screenshot dir&gt; [ok|deny|hang] [screencast node id]   (hang: ScreenCast Start never answers)
    /// Screenshot shoots the root window with ImageMagick `import`. With a node id, ScreenCast is offered too: Start hands
    /// out that PipeWire node (e.g. a GStreamer ximagesrc ! pipewiresink) and OpenPipeWireRemote a connection to
    /// $XDG_RUNTIME_DIR.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] _args)
        {
            using var conn = new DBusConnection(DBusAddress.Session!);
            await conn.ConnectAsync();
            string dir = _args.Length > 0 ? _args[0] : "/tmp";
            string mode = _args.Length > 1 ? _args[1] : "ok";
            uint? node = _args.Length > 2 ? uint.Parse(_args[2], System.Globalization.CultureInfo.InvariantCulture) : null;
            conn.AddMethodHandler(new Portal(conn, dir, mode, node));
            await conn.RequestNameAsync("org.freedesktop.portal.Desktop");
            Console.WriteLine("fake portal ready");
            await Task.Delay(Timeout.Infinite);
        }
    }
}
