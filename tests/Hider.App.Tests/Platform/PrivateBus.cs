using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace Hider.App.Tests.Platform
{
    /// <summary>
    /// A private dbus-daemon for one test: its own socket and a configuration without service directories, so nothing of
    /// the machine (a real portal, a tray watcher) can be activated on it.
    /// </summary>
    [SupportedOSPlatform("linux")]
    public sealed class PrivateBus : IDisposable
    {
        private static readonly TimeSpan startTimeout = TimeSpan.FromSeconds(30);
        private readonly Process process;
        private readonly string dir;

        private PrivateBus(Process _process, string _dir, string _address)
        {
            process = _process;
            dir = _dir;
            Address = _address;
        }

        /// <summary>The bus address as dbus-daemon printed it (unix:path=...,guid=...).</summary>
        public string Address { get; }

        /// <summary>The socket path (the directory is removed with the bus).</summary>
        public string SocketPath => Path.Combine(dir, "bus");

        /// <summary>A directory for a bus (short: a unix socket path is limited to 107 bytes); not created yet.</summary>
        public static string NewDirectory() => Path.Combine("/tmp", "hider-bus-" + Guid.NewGuid().ToString("N")[..12]);

        /// <summary>The address a bus started in <paramref name="_dir"/> will listen on.</summary>
        public static string AddressIn(string _dir) => "unix:path=" + Path.Combine(_dir, "bus");

        public static PrivateBus Start(string? _dir = null)
        {
            string dir = _dir ?? NewDirectory();
            Directory.CreateDirectory(dir);
            string config = Path.Combine(dir, "bus.conf");
            File.WriteAllText(config, $"""
                <!DOCTYPE busconfig PUBLIC "-//freedesktop//DTD D-Bus Bus Configuration 1.0//EN" "http://www.freedesktop.org/standards/dbus/1.0/busconfig.dtd">
                <busconfig>
                  <type>session</type>
                  <listen>unix:path={Path.Combine(dir, "bus")}</listen>
                  <auth>EXTERNAL</auth>
                  <policy context="default">
                    <allow send_destination="*" eavesdrop="true"/>
                    <allow eavesdrop="true"/>
                    <allow own="*"/>
                  </policy>
                </busconfig>
                """);
            var start = new ProcessStartInfo("dbus-daemon")
            {
                ArgumentList = { "--config-file=" + config, "--nofork", "--print-address=1" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            var process = Process.Start(start)!;
            // dbus-daemon prints the address once it listens. Its standard error (an fd limit it may not raise) is kept for
            // the message should it not come up.
            var errors = new StringBuilder();
            process.ErrorDataReceived += (_, _e) =>
            {
                if (_e.Data is not null)
                {
                    lock (errors)
                    {
                        errors.AppendLine(_e.Data);
                    }
                }
            };
            process.BeginErrorReadLine();
            var line = process.StandardOutput.ReadLineAsync();
            string? address = line.Wait(startTimeout) ? line.Result : null;
            if (string.IsNullOrEmpty(address))
            {
                string what = line.IsCompleted ? "exited without printing its address" : $"did not print its address within {startTimeout.TotalSeconds} s";
                try
                {
                    process.Kill();
                }
                catch (InvalidOperationException)
                {
                    // Already gone.
                }
                process.WaitForExit(); // the rest of its standard error
                process.Dispose();
                Directory.Delete(dir, true);
                string stderr;
                lock (errors)
                {
                    stderr = errors.ToString().TrimEnd();
                }
                throw new InvalidOperationException($"dbus-daemon {what}; standard error: {(stderr.Length == 0 ? "(empty)" : stderr)}");
            }
            return new PrivateBus(process, dir, address);
        }

        public void Dispose()
        {
            try
            {
                process.Kill();
                process.WaitForExit();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
            process.Dispose();
            Directory.Delete(dir, true);
        }
    }
}
