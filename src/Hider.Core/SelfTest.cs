using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;

namespace Hider.Core
{
    /// <summary>
    /// Checks run by "Hider --selftest" without starting Avalonia. Used by CI on the trimmed binary to catch
    /// trimming and native library problems (JSON source generation, SkiaSharp natives, SkSL compilation).
    /// </summary>
    public static class SelfTest
    {
        /// <returns>0 when every check passed, 1 otherwise.</returns>
        public static int Run(TextWriter _output)
        {
            var checks = new (string Name, Func<string?> Check)[]
            {
                ("json round trip", CheckJson),
                ("sksl compile", CheckSksl),
            };

            int failures = 0;
            foreach (var (name, check) in checks)
            {
                string? error;
                try
                {
                    error = check();
                }
                catch (Exception e)
                {
                    error = e.ToString();
                }

                _output.WriteLine(error is null ? $"[ok]   {name}" : $"[fail] {name}: {error}");
                if (error is not null)
                {
                    failures++;
                }
            }

            _output.WriteLine(failures == 0 ? "core checks passed" : $"core checks failed ({failures})");
            return failures == 0 ? 0 : 1;
        }

        /// <summary>A state using every field and every registered method survives Save / Load through a file.</summary>
        private static string? CheckJson()
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(-1200, 340, 1200, 675), true);
            area.Pattern = PatternKind.Generated;
            area.Opacity = 0.5;
            area.Fill.Color = "#2B2F36";
            area.Mosaic.Params["unknown-method"] = new Dictionary<string, double> { ["x"] = 1.5 };
            AreasFile.FillDefaults(area.Mosaic.Params, MethodKind.Mosaic);
            AreasFile.FillDefaults(area.Generated.Params, MethodKind.Generated);
            var state = new AppState { Areas = { AreaState.FromArea(area, RoundTripScale) } };

            string path = Path.Combine(Path.GetTempPath(), $"hider-selftest-{Environment.ProcessId}.json");
            try
            {
                AreasFile.Save(path, state);
                var loaded = AreasFile.Load(path, out var status);
                return CompareRoundTrip(state, loaded, status, area.Rect);
            }
            finally
            {
                // Only the file Save wrote: deleting a folder in its place would throw and hide Save's own exception.
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        private const double RoundTripScale = 1.5;

        /// <summary>The verdict of <see cref="CheckJson"/> on what came back (internal for tests: a correct build never fails it).</summary>
        internal static string? CompareRoundTrip(AppState _saved, AppState _loaded, LoadStatus _status, PhysicalRect _rect)
        {
            string expected = AreasFile.ToJson(_saved);
            string actual = AreasFile.ToJson(_loaded);
            if (_status != LoadStatus.Loaded || expected != actual)
            {
                return $"status {_status}, expected {expected}, got {actual}";
            }
            var back = _loaded.Areas[0].ToArea(RoundTripScale);
            return back.Rect == _rect ? null : $"rect {back.Rect} != {_rect}";
        }

        /// <summary>Every embedded .sksl compiles (needs the SkiaSharp native library next to the executable).</summary>
        private static string? CheckSksl()
        {
            return CheckShaders(ShaderSource.All());
        }

        /// <summary>The check of <see cref="CheckSksl"/> on a list of shader ids (internal for tests).</summary>
        internal static string? CheckShaders(IReadOnlyList<string> _ids)
        {
            if (_ids.Count == 0)
            {
                return "no embedded shaders";
            }
            foreach (string id in _ids)
            {
                using var effect = ShaderSource.Compile(id);
            }
            return null;
        }
    }
}
