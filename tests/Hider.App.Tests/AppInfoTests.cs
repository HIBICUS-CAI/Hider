namespace Hider.App.Tests
{
    public class AppInfoTests
    {
        [Fact]
        public void AppInfo_Version_IsMajorMinorPatch()
        {
            // The attribute survives and the "+<revision>" suffix is cut, as --version and the tray's version row show it.
            Assert.Matches(@"^\d+\.\d+\.\d+$", AppInfo.Version);
        }

        [Fact]
        public void GT134_AppInfo_Runtime_IsJitInTheTestProcess()
        {
            // The tests run on the JIT; only the published AOT build reports "aot" (CI checks it on --selftest).
            Assert.Equal("jit", AppInfo.Runtime);
        }
    }
}
