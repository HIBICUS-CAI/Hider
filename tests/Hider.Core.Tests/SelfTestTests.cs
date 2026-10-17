namespace Hider.Core.Tests
{
    [Collection(ProcessWideState.Name)]
    public class SelfTestTests
    {
        private static readonly string[] passingLines = { "[ok]   json round trip", "[ok]   sksl compile", "core checks passed" };

        private static string TempPath => Path.Combine(Path.GetTempPath(), $"hider-selftest-{Environment.ProcessId}.json");

        [Fact]
        public void CT93_SelfTest_Passes()
        {
            using var output = new StringWriter();
            int code = SelfTest.Run(output);
            Assert.True(code == 0, output.ToString());
        }

        [Fact]
        public void CT93_SelfTest_ReportsEachCheck()
        {
            using var output = new StringWriter();
            Assert.Equal(0, SelfTest.Run(output));
            string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(passingLines, lines);
        }

        [Fact]
        public void CT93_SelfTest_LeavesNoFileBehind()
        {
            using var output = new StringWriter();
            SelfTest.Run(output);
            Assert.False(File.Exists(TempPath));
            Assert.False(File.Exists(TempPath + ".tmp"));
        }

        [Fact]
        public void CT93_SelfTest_FailingCheck_ReportedWithItsError_ExitCodeOne()
        {
            // A folder where the round trip's file goes: Save throws, the exception becomes the check's error and the
            // remaining checks still run.
            Directory.CreateDirectory(TempPath);
            try
            {
                using var output = new StringWriter();
                int code = SelfTest.Run(output);
                string text = output.ToString();
                Assert.Equal(1, code);
                string[] lines = text.Split(Environment.NewLine);
                Assert.StartsWith("[fail] json round trip: System.", lines[0], StringComparison.Ordinal);
                Assert.Contains("[ok]   sksl compile", lines);
                Assert.Equal("core checks failed (1)", text.TrimEnd().Split(Environment.NewLine)[^1]);
                Assert.False(File.Exists(TempPath + ".tmp"));
            }
            finally
            {
                Directory.Delete(TempPath, true);
            }
        }
    }
}
