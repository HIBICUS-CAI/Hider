using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Hider.App.Tests.App
{
    /// <summary>
    /// Runs Hider.dll (the build next to the tests) in a child process with `dotnet exec`, its home directory (where .hider
    /// lives) pointed at a temporary one and no display, and collects the exit code and both streams.
    /// </summary>
    internal static class HiderProcess
    {
        public sealed record Result(int ExitCode, string Output, string Error);

        public static string TestDirectory => Path.GetDirectoryName(typeof(HiderApp).Assembly.Location)!;

        /// <summary>The dotnet host running these tests (the SDK sets DOTNET_HOST_PATH; else the runtime's install root).</summary>
        public static string Dotnet
        {
            get
            {
                string? host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
                if (!string.IsNullOrEmpty(host) && File.Exists(host))
                {
                    return host;
                }
                string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));
                return Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            }
        }

        /// <param name="_home">The home directory (HOME / USERPROFILE) the child sees.</param>
        /// <param name="_appDirectory">Where Hider.dll is run from (default: the test output directory).</param>
        /// <param name="_args">The command line.</param>
        public static async Task<Result> RunAsync(string _home, string? _appDirectory, params string[] _args)
        {
            var start = new ProcessStartInfo(Dotnet)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
            };
            start.ArgumentList.Add("exec");
            start.ArgumentList.Add(Path.Combine(_appDirectory ?? TestDirectory, "Hider.dll"));
            foreach (string arg in _args)
            {
                start.ArgumentList.Add(arg);
            }
            start.Environment[OperatingSystem.IsWindows() ? "USERPROFILE" : "HOME"] = _home;
            // Never a real desktop session: the resident app must not start on the machine running the tests.
            start.Environment.Remove("DISPLAY");
            start.Environment.Remove("WAYLAND_DISPLAY");
            start.Environment["DBUS_SESSION_BUS_ADDRESS"] = "unix:path=" + Path.Combine(_home, "no-bus");

            using var process = Process.Start(start)!;
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(true);
                throw new TimeoutException($"Hider {string.Join(' ', _args)} did not exit within 2 minutes");
            }
            return new Result(process.ExitCode, await output, await error);
        }

        /// <summary>
        /// A copy of the app directory whose Hider.dll is a real file (so AppContext.BaseDirectory is the copy) and whose other
        /// files are links to the test output: a place to put a libhidercap.so of the test's choosing. Linux only.
        /// </summary>
        public static string LinkedAppDirectory(string _parent)
        {
            string dir = Path.Combine(_parent, "app");
            Directory.CreateDirectory(dir);
            foreach (string entry in Directory.EnumerateFileSystemEntries(TestDirectory))
            {
                string name = Path.GetFileName(entry);
                string target = Path.Combine(dir, name);
                if (name is "Hider.dll" or "Hider.deps.json" or "Hider.runtimeconfig.json")
                {
                    File.Copy(entry, target);
                }
                else if (Directory.Exists(entry))
                {
                    Directory.CreateSymbolicLink(target, entry);
                }
                else
                {
                    File.CreateSymbolicLink(target, entry);
                }
            }
            return dir;
        }
    }
}
