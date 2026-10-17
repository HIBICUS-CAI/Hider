using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;

namespace Hider.Core.Tests
{
    [Collection(ProcessWideState.Name)]
    public class SelfTestTests
    {
        private static readonly string[] missingShader = { "no-such-shader" };
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

        private static AppState OneArea(PhysicalRect _rect, double _scale)
        {
            return new AppState { Areas = { AreaState.FromArea(new Area { Id = "0f3b8d2e", Rect = _rect }, _scale) } };
        }

        [Fact]
        public void CompareRoundTrip_SameStateAndRect_Passes()
        {
            var rect = new PhysicalRect(-1200, 340, 1200, 675);
            Assert.Null(SelfTest.CompareRoundTrip(OneArea(rect, 1.5), OneArea(rect, 1.5), LoadStatus.Loaded, rect));
        }

        [Theory]
        [InlineData(LoadStatus.Missing)]
        [InlineData(LoadStatus.Broken)]
        [InlineData(LoadStatus.Unreadable)]
        public void CompareRoundTrip_NotLoaded_Fails(LoadStatus _status)
        {
            // The loaded state is empty then: the status is reported without looking at its areas.
            var rect = new PhysicalRect(0, 0, 300, 300);
            string? error = SelfTest.CompareRoundTrip(OneArea(rect, 1.5), new AppState(), _status, rect);
            Assert.NotNull(error);
            Assert.StartsWith($"status {_status}, expected ", error, StringComparison.Ordinal);
        }

        [Fact]
        public void CompareRoundTrip_DifferentJson_Fails()
        {
            var rect = new PhysicalRect(0, 0, 300, 300);
            var loaded = OneArea(rect, 1.5);
            loaded.Areas[0].Opacity = 0.5;
            string? error = SelfTest.CompareRoundTrip(OneArea(rect, 1.5), loaded, LoadStatus.Loaded, rect);
            Assert.NotNull(error);
            Assert.StartsWith("status Loaded, expected {", error, StringComparison.Ordinal);
            Assert.Contains("\"opacity\": 0.5", error, StringComparison.Ordinal);
        }

        [Fact]
        public void CompareRoundTrip_RectNotBackAtTheScale_Fails()
        {
            // The saved size (DIP) converted back at 150% is not the rectangle the check started from.
            var saved = OneArea(new PhysicalRect(0, 0, 300, 300), 1.5);
            var expected = new PhysicalRect(0, 0, 301, 300);
            Assert.Equal($"rect {new PhysicalRect(0, 0, 300, 300)} != {expected}", SelfTest.CompareRoundTrip(saved, saved, LoadStatus.Loaded, expected));
        }

        [Fact]
        public void CheckShaders_EveryEmbeddedShader_Compiles()
        {
            Assert.Null(SelfTest.CheckShaders(ShaderSource.All()));
        }

        [Fact]
        public void CheckShaders_NoShaders_Fails()
        {
            Assert.Equal("no embedded shaders", SelfTest.CheckShaders(Array.Empty<string>()));
        }

        [Fact]
        public void CheckShaders_MissingShader_Throws()
        {
            // Run turns the exception into the check's error.
            var e = Assert.Throws<InvalidOperationException>(() => SelfTest.CheckShaders(missingShader));
            Assert.Equal("Missing embedded shader no-such-shader.sksl", e.Message);
        }
    }
}
