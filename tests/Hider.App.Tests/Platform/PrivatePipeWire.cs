using System.Diagnostics;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace Hider.App.Tests.Platform
{
    /// <summary>
    /// A private PipeWire (its own XDG_RUNTIME_DIR) with WirePlumber linking streams, and video sources made with GStreamer
    /// (videotestsrc ! pipewiresink), as scripts/xvfb-strip-smoke.sh does with the X screen. For the tests of libhidercap
    /// against real frames.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed partial class PrivatePipeWire : IDisposable
    {
        private static readonly TimeSpan startTimeout = TimeSpan.FromSeconds(30);
        private readonly List<Process> processes = new();
        private readonly List<Process> sources = new();
        private readonly PrivateBus bus;

        private PrivatePipeWire(string _dir, PrivateBus _bus)
        {
            RuntimeDir = _dir;
            bus = _bus;
        }

        public string RuntimeDir { get; }

        public string SocketPath => Path.Combine(RuntimeDir, "pipewire-0");

        public static async Task<PrivatePipeWire> StartAsync(CancellationToken _ct)
        {
            string dir = Path.Combine("/tmp", "hider-pw-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(dir);
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            // WirePlumber exits without a session bus.
            var pw = new PrivatePipeWire(dir, PrivateBus.Start());
            try
            {
                pw.processes.Add(pw.Run("pipewire"));
                await WaitForAsync(() => File.Exists(pw.SocketPath), "the pipewire socket", _ct);
                pw.processes.Add(pw.Run("wireplumber"));
                // WirePlumber is up once it shows as a client of the daemon.
                await WaitForAsync(() => pw.List("Client").Contains("\"WirePlumber\"", StringComparison.Ordinal), "wireplumber", _ct);
                return pw;
            }
            catch
            {
                pw.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Starts a solid-colour video source of <paramref name="_format"/> (BGRx, BGRA, RGBx, RGBA) and returns its node id.
        /// <paramref name="_argb"/> is videotestsrc's foreground-color (0xAARRGGBB).
        /// </summary>
        public Task<uint> AddSourceAsync(string _name, string _format, int _width, int _height, uint _argb, CancellationToken _ct)
        {
            return AddSourceAsync(_name, _format, _width, _height, _ct, "pattern=solid-color",
                "foreground-color=" + _argb.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Starts a source of videotestsrc's checkers-8 (8 x 8 px cells of two colours, cell (0, 0) at the top left) and
        /// returns its node id.
        /// </summary>
        public Task<uint> AddCheckersSourceAsync(string _name, string _format, int _width, int _height, CancellationToken _ct)
        {
            return AddSourceAsync(_name, _format, _width, _height, _ct, "pattern=checkers-8");
        }

        private async Task<uint> AddSourceAsync(string _name, string _format, int _width, int _height, CancellationToken _ct, params string[] _pattern)
        {
            var args = new List<string> { "-q", "videotestsrc", "is-live=true" };
            args.AddRange(_pattern);
            args.AddRange(new[]
            {
                "!", $"video/x-raw,format={_format},width={_width},height={_height},framerate=30/1", "!",
                "pipewiresink", "mode=provide", $"stream-properties=p,media.class=Video/Source,node.name={_name}",
            });
            var source = Run("gst-launch-1.0", args.ToArray());
            sources.Add(source);
            processes.Add(source);
            uint? node = null;
            await WaitForAsync(() => (node = FindNode(_name)) is not null, $"the PipeWire node {_name}", _ct);
            return node!.Value;
        }

        /// <summary>Stops the video sources (their nodes go away).</summary>
        public void StopSources()
        {
            foreach (var source in sources)
            {
                Kill(source);
            }
            sources.Clear();
        }

        /// <summary>A new connection to the daemon, as OpenPipeWireRemote hands one out.</summary>
        public Socket Connect()
        {
            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.Connect(new UnixDomainSocketEndPoint(SocketPath));
            return socket;
        }

        private uint? FindNode(string _name)
        {
            // pw-cli ls Node: "\tid 42, type PipeWire:Interface:Node/3" ... "\t\tnode.name = "<name>"".
            uint? id = null;
            foreach (string line in List("Node").Split('\n'))
            {
                var m = IdLine().Match(line);
                if (m.Success)
                {
                    id = uint.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
                else if (line.Contains($"node.name = \"{_name}\"", StringComparison.Ordinal))
                {
                    return id;
                }
            }
            return null;
        }

        [GeneratedRegex(@"^\s*id (\d+),")]
        private static partial Regex IdLine();

        private string List(string _type)
        {
            using var p = Run("pw-cli", "ls", _type);
            string output = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return output;
        }

        private Process Run(string _file, params string[] _args)
        {
            var start = new ProcessStartInfo(_file)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string a in _args)
            {
                start.ArgumentList.Add(a);
            }
            start.Environment["XDG_RUNTIME_DIR"] = RuntimeDir;
            start.Environment["DBUS_SESSION_BUS_ADDRESS"] = bus.Address;
            // Nothing of the session's own PipeWire: its remote, runtime directory or configuration.
            start.Environment.Remove("PIPEWIRE_REMOTE");
            start.Environment.Remove("PIPEWIRE_RUNTIME_DIR");
            start.Environment.Remove("PIPEWIRE_CONFIG_DIR");
            start.Environment.Remove("DISPLAY");
            var process = Process.Start(start)!;
            process.ErrorDataReceived += (_, _) => { };
            process.BeginErrorReadLine();
            if (_file != "pw-cli")
            {
                process.OutputDataReceived += (_, _) => { };
                process.BeginOutputReadLine();
            }
            return process;
        }

        /// <summary>Polls a readiness condition of the daemons (they start in their own time), failing after a generous bound.</summary>
        private static async Task WaitForAsync(Func<bool> _ready, string _what, CancellationToken _ct)
        {
            var sw = Stopwatch.StartNew();
            while (!_ready())
            {
                if (sw.Elapsed > startTimeout)
                {
                    throw new TimeoutException($"{_what} did not come up within {startTimeout.TotalSeconds} s");
                }
                await Task.Delay(50, _ct);
            }
        }

        private static void Kill(Process _process)
        {
            try
            {
                _process.Kill(true);
                _process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        public void Dispose()
        {
            for (int i = processes.Count - 1; i >= 0; i--)
            {
                Kill(processes[i]);
                processes[i].Dispose();
            }
            processes.Clear();
            bus.Dispose();
            Directory.Delete(RuntimeDir, true);
        }
    }
}
