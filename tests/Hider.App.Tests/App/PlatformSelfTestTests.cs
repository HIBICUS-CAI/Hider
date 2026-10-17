namespace Hider.App.Tests.App
{
    /// <summary>
    /// The platform part of --selftest, in this process. On Linux the test output has the real libhidercap.so (make before
    /// the build); the variants without one or with a broken one run in a child process (ProgramTests). The real helper is
    /// loaded here, so these run in the group of the other tests of the real library.
    /// </summary>
    [Collection(nameof(Hider.App.Tests.Platform.HidercapGroup))]
    public class PlatformSelfTestTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        [Hider.App.Tests.Platform.HidercapFact]
        public void Linux_ShippedHelper_LoadsAndPasses()
        {
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "libhidercap.so")));
            var output = new StringWriter();

            int result = PlatformSelfTest.Run(output);

            Assert.Equal(0, result);
            Assert.Matches(@"^\[ok\]   libhidercap: loaded, last sequence \d+\r?\n$", output.ToString());
        }

        [Fact(Skip = "Windows 専用（Win32 のウィンドウ列挙と PrintWindow）", SkipUnless = nameof(IsWindows))]
        public void Windows_EnumeratesAndComposesTheDesktopCorner()
        {
            var output = new StringWriter();

            int result = PlatformSelfTest.Run(output);

            Assert.Equal(0, result);
            Assert.Matches(@"^\[ok\]   win32 capture: \d+ window\(s\), result \w+\r?\n$", output.ToString());
        }
    }
}
