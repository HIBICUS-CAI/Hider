namespace Hider.App.Tests.App
{
    /// <summary>
    /// The platform part of --selftest, in this process (the test output directory ships no libhidercap.so: the native
    /// helper is built for publishing only). The variants with a shipped library run in a child process (ProgramTests).
    /// </summary>
    public class PlatformSelfTestTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        public static bool IsLinux => OperatingSystem.IsLinux();

        [Fact(Skip = "Linux 専用（libhidercap の確認）", SkipUnless = nameof(IsLinux))]
        public void Linux_NoHelperShipped_SkipsAndPasses()
        {
            Assert.False(File.Exists(Path.Combine(AppContext.BaseDirectory, "libhidercap.so")));
            var output = new StringWriter();

            int result = PlatformSelfTest.Run(output);

            Assert.Equal(0, result);
            Assert.Equal("[skip] libhidercap.so not shipped: the Screenshot portal is used (D-6)" + Environment.NewLine, output.ToString());
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
