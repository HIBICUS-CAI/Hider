using System.Reflection;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Avalonia;
using Hider.App.Tests.Platform.Windows;
using Hider.Core;
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
        public async Task GT41_Version_PrintsTheVersion_AndTouchesNothing(params string[] _args)
        {
            using var home = new Home();

            var result = await HiderProcess.RunAsync(home.Path, null, _args);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"Hider {AppInfo.Version}{nl}", result.Output);
            Assert.Equal("", result.Error);
            Assert.False(Directory.Exists(home.DataDirectory)); // no lock, no areas.json
        }

        [Fact]
        public async Task GT49_Selftest_RunsCoreAndPlatformChecks_AndPasses()
        {
            // Linux: from a copy that ships no libhidercap.so (the test output may have the real one, see below).
            using var home = new Home();
            string? app = OperatingSystem.IsLinux() ? HiderProcess.LinkedAppDirectory(home.Path) : null;

            var result = await HiderProcess.RunAsync(home.Path, app, "--selftest");

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

        [Fact]
        public async Task GT134_Selftest_PrintsTheRuntimeFirst()
        {
            // Before the checks: CI reads it on each published build (aot / jit); the tests run Hider.dll on the JIT.
            using var home = new Home();
            string? app = OperatingSystem.IsLinux() ? HiderProcess.LinkedAppDirectory(home.Path) : null;

            var result = await HiderProcess.RunAsync(home.Path, app, "--selftest");

            Assert.Equal(0, result.ExitCode);
            var lines = Lines(result.Output);
            Assert.Equal("runtime: jit", lines[0]);
            Assert.Equal(1, lines.Count(_l => _l.StartsWith("runtime: ", StringComparison.Ordinal)));
            Assert.Equal("selftest passed", lines[^1]);
        }

        [Hider.App.Tests.Platform.HidercapFact]
        public async Task Selftest_ShippedRealHelper_LoadsAndPasses()
        {
            // The libhidercap.so built by make (copied next to the tests) loads and answers, as in the packaged build.
            using var home = new Home();
            string app = HiderProcess.LinkedAppDirectory(home.Path);
            File.Copy(Hider.App.Tests.Platform.LinuxTestEnvironment.HidercapPath, Path.Combine(app, "libhidercap.so"));

            var result = await HiderProcess.RunAsync(home.Path, app, "--selftest");

            Assert.Equal(0, result.ExitCode);
            var lines = Lines(result.Output);
            Assert.Contains("core checks passed", lines);
            Assert.Contains("[ok]   libhidercap: loaded, last sequence 0", lines);
            Assert.Equal("selftest passed", lines[^1]);
        }

        [Fact(Skip = OsOnly.ShippedHelper, SkipUnless = nameof(IsLinux))]
        public async Task GT49_Selftest_ShippedHelperThatDoesNotLoad_Fails()
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

        [Fact(Skip = OsOnly.ShippedHelper, SkipUnless = nameof(IsLinux))]
        public async Task GT49_Selftest_ShippedHelperWithoutTheApi_Fails()
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
        public async Task GT50_AnotherInstanceRunning_ExitsZeroQuietly(params string[] _args)
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
        public async Task GT50_AnotherInstanceRunning_Verbose_SaysSoOnStandardError()
        {
            using var home = new Home();
            using var held = SingleInstance.TryAcquire(home.LockPath, out _);
            Assert.NotNull(held);

            var result = await HiderProcess.RunAsync(home.Path, null, "--verbose");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.EndsWith(" another instance is running", Lines(result.Error).Single());
        }

        [Fact(Skip = OsOnly.LockErrorExit, SkipUnless = nameof(IsLinux))]
        public async Task GT51_LockCannotBeOpened_ExitsTwo_WithTheReason()
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

        [Fact(Skip = OsOnly.MessageBox, SkipUnless = nameof(IsWindows))]
        [SupportedOSPlatform("windows")]
        public async Task GT51_LockCannotBeOpened_Windows_ShowsTheReasonInAMessageBox_ThenExitsTwo()
        {
            // Started from Explorer there is no console: the reason reaches the user only through the message box
            // (Win32Dialog.ShowError). The test closes it as the × button does; the process then ends with 2.
            using var home = new Home();
            string file = Path.Combine(home.Path, "home-is-a-file");
            File.WriteAllText(file, "");
            string lockPath = Path.Combine(file, ".hider", "instance.lock");
            string? shown = null;

            var result = await HiderProcess.RunAsync(file, null, async (_process, _ct) =>
            {
                nint box = await MessageBoxes.WaitForAsync(_process, Strings.AppName, _ct);
                shown = MessageBoxes.Text(box);
                Assert.False(_process.HasExited); // waiting for the user
                MessageBoxes.Close(box);
            });

            Assert.Equal(2, result.ExitCode);
            Assert.Equal("", result.Output);
            string prefix = $"hider: cannot open {lockPath}: ";
            string line = Lines(result.Error).Single();
            Assert.StartsWith(prefix, line);
            string reason = line[prefix.Length..];
            Assert.NotEqual("", reason);
            Assert.Equal(Strings.StartupLockFailed(lockPath, reason), shown); // the same reason, in Japanese (FR-70)
        }

        [Fact(Skip = OsOnly.NoDisplay, SkipUnless = nameof(IsLinux))]
        public async Task GT51_NoDisplay_TheAppStartFails_NotWithASilentSuccess_AndReleasesTheLock()
        {
            // Past the switches and the lock the resident app starts (X11): without a display that must fail visibly, at the
            // X11 step. Avalonia throws from XOpenDisplay and nothing catches it: the runtime prints the unhandled exception
            // and aborts (exit 134 = 128 + SIGABRT), which is neither 0 ("already running") nor 2 (the lock).
            using var home = new Home();

            var result = await HiderProcess.RunAsync(home.Path, null, "--verbose");

            Assert.Equal(134, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Contains("Unhandled exception. System.Exception: XOpenDisplay failed", result.Error);
            Assert.Contains("at Hider.Program.Main", result.Error);
            Assert.True(File.Exists(home.LockPath));
            using var again = SingleInstance.TryAcquire(home.LockPath, out string? error);
            Assert.NotNull(again); // the guard went with the process
            Assert.Null(error);
        }

        [Fact]
        public void GT52_BuildAvaloniaApp_IsHiderOnTheDetectedDesktopPlatform_WithSkia()
        {
            // The windowing platform is picked when the app starts (UsePlatformDetect: Win32, or X11 on Linux, D-1).
            var builder = Program.BuildAvaloniaApp();

            Assert.Equal(typeof(HiderApp), builder.ApplicationType);
            Assert.NotNull(builder.WindowingSubsystemInitializer);
            Assert.Equal("Skia", builder.RenderingSubsystemName);
            Assert.Equal("StandardRuntimePlatform", builder.RuntimePlatformServicesName);
            Assert.NotSame(builder, Program.BuildAvaloniaApp());
        }

        [Fact]
        public void GT52_BuildAvaloniaApp_SetsTheX11WmClass_TheInstallersWriteAsStartupWMClass()
        {
            // The one Hider-specific option (D-1): the running window groups with the .desktop entry only while WM_CLASS and
            // StartupWMClass agree. AppBuilder.With<T> keeps each option in a closure added to the private _optionsInitializers
            // (checked on Avalonia 12.1.4) that binds it into the locator at setup; it is read from there instead of running
            // the setup, which would change the locator of the headless application these tests share.
            var builder = Program.BuildAvaloniaApp();
            var initializers = (Action?)typeof(AppBuilder).GetField("_optionsInitializers", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(builder);
            Assert.NotNull(initializers); // Avalonia renamed it: find where With<T> keeps its options now

            var options = initializers!.GetInvocationList()
                .Select(_d => _d.Target)
                .SelectMany(_t => _t?.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Select(_f => _f.GetValue(_t)) ?? [])
                .OfType<X11PlatformOptions>()
                .ToArray();

            string wmClass = Assert.Single(options).WmClass!;
            Assert.Equal("Hider", wmClass);
            string root = Hider.Core.Tests.Imaging.Golden.RepoRoot;
            string Entry(string _path) => Assert.Single(Regex.Matches(File.ReadAllText(Path.Combine(root, _path)), @"StartupWMClass=(\w+)")).Groups[1].Value;
            Assert.Equal(wmClass, Entry(Path.Combine("scripts", "install.py")));
            Assert.Equal(wmClass, Entry(Path.Combine("scripts", "release", "install.sh.in")));
        }
    }
}
