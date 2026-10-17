using Hider.Core.Persistence;

namespace Hider.App.Tests.App
{
    /// <summary>
    /// The command line (Program.Main) in a child process: --version, --selftest, --verbose, unknown switches and the single
    /// instance guard (FR-08). The child's home directory is a temporary one, so .hider of the machine is never touched.
    /// </summary>
    public class ProgramTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        public static bool IsLinux => OperatingSystem.IsLinux();

        private static readonly string nl = Environment.NewLine;

        private sealed class Home : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hider-cli-" + Guid.NewGuid().ToString("N"));

            public Home()
            {
                Directory.CreateDirectory(Path);
            }

            public string DataDirectory => System.IO.Path.Combine(Path, ".hider");

            public string LockPath => System.IO.Path.Combine(DataDirectory, "instance.lock");

            public void Dispose()
            {
                Directory.Delete(Path, true);
            }
        }

        private static string[] Lines(string _text) => _text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(_l => _l.TrimEnd('\r')).ToArray();

        [Theory]
        [InlineData("--version")]
        [InlineData("--selftest", "--version")] // --version wins wherever it is
        [InlineData("--verbose", "--version", "--bogus")]
        public async Task Version_PrintsTheVersion_AndTouchesNothing(params string[] _args)
        {
            using var home = new Home();

            var result = await HiderProcess.RunAsync(home.Path, null, _args);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"Hider {AppInfo.Version}{nl}", result.Output);
            Assert.Equal("", result.Error);
            Assert.False(Directory.Exists(home.DataDirectory)); // no lock, no areas.json
        }

        [Fact]
        public async Task Selftest_RunsCoreAndPlatformChecks_AndPasses()
        {
            using var home = new Home();

            var result = await HiderProcess.RunAsync(home.Path, null, "--selftest");

            Assert.Equal(0, result.ExitCode);
            var lines = Lines(result.Output);
            Assert.Contains("[ok]   json round trip", lines);
            Assert.Contains("[ok]   sksl compile", lines);
            Assert.Contains("core checks passed", lines);
            if (OperatingSystem.IsWindows())
            {
                Assert.Contains(lines, _l => _l.StartsWith("[ok]   win32 capture: ", StringComparison.Ordinal));
            }
            else
            {
                Assert.Contains("[skip] libhidercap.so not shipped: the Screenshot portal is used (D-6)", lines);
            }
            Assert.Equal("selftest passed", lines[^1]);
            Assert.Equal("", result.Error);
            Assert.False(Directory.Exists(home.DataDirectory));
        }

        [Fact(Skip = "Linux 専用（libhidercap.so）", SkipUnless = nameof(IsLinux))]
        public async Task Selftest_ShippedHelperThatDoesNotLoad_Fails()
        {
            // A libhidercap.so that is not a library (or whose libpipewire is missing) fails the packaged build.
            using var home = new Home();
            string app = HiderProcess.LinkedAppDirectory(home.Path);
            File.WriteAllText(Path.Combine(app, "libhidercap.so"), "not an ELF file");

            var result = await HiderProcess.RunAsync(home.Path, app, "--selftest");

            Assert.Equal(1, result.ExitCode);
            var lines = Lines(result.Output);
            Assert.Contains("core checks passed", lines);
            Assert.Contains("[fail] libhidercap.so is shipped but does not load (is libpipewire-0.3 installed?)", lines);
            Assert.Equal("selftest failed", lines[^1]);
        }

        [Fact(Skip = "Linux 専用（libhidercap.so）", SkipUnless = nameof(IsLinux))]
        public async Task Selftest_ShippedHelperWithoutTheApi_Fails()
        {
            // A library that loads but lacks hidercap_last_sequence (an old helper): the call fails, the self test reports it.
            using var home = new Home();
            string app = HiderProcess.LinkedAppDirectory(home.Path);
            string native = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "libSystem.Native.so");
            File.Copy(native, Path.Combine(app, "libhidercap.so"));

            var result = await HiderProcess.RunAsync(home.Path, app, "--selftest");

            Assert.Equal(1, result.ExitCode);
            var lines = Lines(result.Output);
            Assert.Contains(lines, _l => _l.StartsWith("[fail] libhidercap: EntryPointNotFoundException: ", StringComparison.Ordinal)
                && _l.EndsWith(" (an old libhidercap.so?)", StringComparison.Ordinal));
            Assert.Equal("selftest failed", lines[^1]);
        }

        [Theory]
        [InlineData]
        [InlineData("--bogus")]          // unknown switches are ignored
        [InlineData("--bogus", "file.txt")]
        public async Task AnotherInstanceRunning_ExitsZeroQuietly(params string[] _args)
        {
            using var home = new Home();
            using var held = SingleInstance.TryAcquire(home.LockPath, out string? error);
            Assert.NotNull(held);
            Assert.Null(error);

            var result = await HiderProcess.RunAsync(home.Path, null, _args);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
        }

        [Fact]
        public async Task AnotherInstanceRunning_Verbose_SaysSoOnStandardError()
        {
            using var home = new Home();
            using var held = SingleInstance.TryAcquire(home.LockPath, out _);
            Assert.NotNull(held);

            var result = await HiderProcess.RunAsync(home.Path, null, "--verbose");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.EndsWith(" another instance is running", Lines(result.Error).Single());
        }

        [Fact(Skip = "Linux 専用（Windows ではメッセージボックスが閉じられるまで終了しない）", SkipUnless = nameof(IsLinux))]
        public async Task LockCannotBeOpened_ExitsTwo_WithTheReason()
        {
            // The home directory is a file: .hider cannot be created. Not "already running" (exit 0): the user must see why.
            using var home = new Home();
            string file = Path.Combine(home.Path, "home-is-a-file");
            File.WriteAllText(file, "");
            string lockPath = Path.Combine(file, ".hider", "instance.lock");

            var result = await HiderProcess.RunAsync(file, null);

            Assert.Equal(2, result.ExitCode);
            Assert.Equal("", result.Output);
            string line = Lines(result.Error).Single();
            Assert.StartsWith($"hider: cannot open {lockPath}: ", line);
            Assert.True(line.Length > $"hider: cannot open {lockPath}: ".Length); // the system's reason follows
        }

        [Fact(Skip = "Linux 専用（表示なしの起動。Windows では常駐アプリが起動してしまう）", SkipUnless = nameof(IsLinux))]
        public async Task NoDisplay_TheAppStartFails_NotWithASilentSuccess_AndReleasesTheLock()
        {
            // Past the switches and the lock the resident app starts (X11): without a display that must fail visibly.
            using var home = new Home();

            var result = await HiderProcess.RunAsync(home.Path, null, "--verbose");

            Assert.NotEqual(0, result.ExitCode);
            Assert.NotEqual(2, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.NotEqual("", result.Error);
            Assert.True(File.Exists(home.LockPath));
            using var again = SingleInstance.TryAcquire(home.LockPath, out string? error);
            Assert.NotNull(again); // the guard went with the process
            Assert.Null(error);
        }

        [Fact]
        public void BuildAvaloniaApp_IsHiderOnTheDetectedDesktopPlatform_WithSkia()
        {
            // The windowing platform is picked when the app starts (UsePlatformDetect: Win32, or X11 on Linux, D-1).
            var builder = Program.BuildAvaloniaApp();

            Assert.Equal(typeof(HiderApp), builder.ApplicationType);
            Assert.NotNull(builder.WindowingSubsystemInitializer);
            Assert.Equal("Skia", builder.RenderingSubsystemName);
            Assert.Equal("StandardRuntimePlatform", builder.RuntimePlatformServicesName);
            Assert.NotSame(builder, Program.BuildAvaloniaApp());
        }
    }
}
