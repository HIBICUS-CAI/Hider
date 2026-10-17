using Tmds.DBus.Protocol;

namespace Hider.Tools.FakePortal
{
    /// <summary>
    /// Fake xdg-desktop-portal for Xvfb (scripts/xvfb-strip-smoke.sh):
    ///   FakePortal &lt;screenshot dir&gt; [ok|deny] [screencast node id]
    /// Screenshot shoots the root window with ImageMagick `import`. With a node id, ScreenCast is offered too: Start hands
    /// out that PipeWire node (e.g. a GStreamer ximagesrc ! pipewiresink) and OpenPipeWireRemote a connection to
    /// $XDG_RUNTIME_DIR. deny answers Screenshot and SelectSources with response 1 (the user cancelled). Methods it does
    /// not offer get the unknown-method error and an "unknown method" line. A dialog that never answers is not offered:
    /// tests/Hider.App.Tests (FakeDesktopPortal, Answer.Silent) covers that.
    /// </summary>
    internal static class Program
    {
        private static async Task Main(string[] _args)
        {
            string dir = _args.Length > 0 ? _args[0] : "/tmp";
            string mode = _args.Length > 1 ? _args[1] : "ok";
            if (mode is not ("ok" or "deny"))
            {
                Console.Error.WriteLine($"unknown mode {mode} (ok or deny)");
                Environment.Exit(2);
            }
            using var conn = new DBusConnection(DBusAddress.Session!);
            await conn.ConnectAsync();
            uint? node = _args.Length > 2 ? uint.Parse(_args[2], System.Globalization.CultureInfo.InvariantCulture) : null;
            conn.AddMethodHandler(new Portal(conn, dir, mode, node));
            await conn.RequestNameAsync("org.freedesktop.portal.Desktop");
            Console.WriteLine("fake portal ready");
            await Task.Delay(Timeout.Infinite);
        }
    }
}
