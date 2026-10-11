using System.Text.Json;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    public sealed class AreasFileTests : IDisposable
    {
        // Work instructions §3.4 (version 1, decided).
        private const string SchemaSample = """
            {
              "version": 1,
              "areas": [
                {
                  "id": "0f3b8d2e",
                  "x": 1200, "y": 340,
                  "width": 800, "height": 450,
                  "opacity": 1.0,
                  "pattern": "mosaic",
                  "fill": { "color": "#68768A" },
                  "mosaic": {
                    "method": "pixelate",
                    "common": { "blur": 0, "levels": 256, "intervalMs": 100, "stripPx": 8 },
                    "params": { "pixelate": { "block": 12 }, "hexcell": { "radius": 10 }, "blur": { "radius": 8 } }
                  },
                  "generated": {
                    "method": "block",
                    "common": { "baseColor": "#68768A", "seed": 1234 },
                    "params": { "block": { "cellSize": 12, "variance": 0.2, "gap": 0 }, "noise": { "scale": 4, "levels": 6 } }
                  }
                }
              ]
            }
            """;

        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(dir, "areas.json");

        public AreasFileTests()
        {
            Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            Directory.Delete(dir, true);
        }

        private static Area SampleArea()
        {
            var area = Area.CreateNew("0f3b8d2e", new PhysicalRect(-1200, 340, 1200, 675), true);
            area.Opacity = 0.75;
            area.Pattern = PatternKind.Generated;
            area.Fill.Color = "#2B2F36";
            area.Mosaic.Common.Levels = 16;
            area.Mosaic.ParamsFor("pixelate")["block"] = 20;
            area.Generated.Common.Seed = 99;
            area.Generated.ParamsFor("block")["variance"] = 0.5;
            AreasFile.FillDefaults(area.Mosaic.Params, MethodKind.Mosaic);
            AreasFile.FillDefaults(area.Generated.Params, MethodKind.Generated);
            return area;
        }

        [Fact]
        public void CT21_SaveLoad_RoundTrip()
        {
            var state = new AppState { Areas = { AreaState.FromArea(SampleArea(), 1.5) } };
            AreasFile.Save(FilePath, state);
            var loaded = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Equal(AreasFile.ToJson(state), AreasFile.ToJson(loaded));
            var back = loaded.Areas[0].ToArea(1.5);
            Assert.Equal(new PhysicalRect(-1200, 340, 1200, 675), back.Rect);
            Assert.Equal(800, loaded.Areas[0].Width);
            Assert.Equal(450, loaded.Areas[0].Height);
        }

        [Fact]
        public void CT22_Schema_MatchesInstructionSample()
        {
            var state = AreasFile.FromJson(SchemaSample)!;
            string output = AreasFile.ToJson(state);
            using var expected = JsonDocument.Parse(SchemaSample);
            using var actual = JsonDocument.Parse(output);
            AssertSameShape(expected.RootElement, actual.RootElement, "$");
        }

        [Fact]
        public void CT23_UnknownMethodParams_Preserved()
        {
            string json = SchemaSample.Replace("\"blur\": { \"radius\": 8 }", "\"blur\": { \"radius\": 8 }, \"future\": { \"k\": 3.5 }");
            File.WriteAllText(FilePath, json);
            var loaded = AreasFile.Load(FilePath, out _);
            AreasFile.Save(FilePath, loaded);
            var again = AreasFile.Load(FilePath, out _);
            Assert.Equal(3.5, again.Areas[0].Mosaic.Params["future"]["k"]);
            Assert.Equal(10, again.Areas[0].Mosaic.Params["hexcell"]["radius"]);
        }

        [Fact]
        public void CT24_MissingKeys_FilledWithDefaults()
        {
            File.WriteAllText(FilePath, """
                { "version": 1, "areas": [ { "id": "0f3b8d2e", "x": 1, "y": 2, "width": 100, "height": 50,
                  "mosaic": { "method": "pixelate", "common": { "blur": 3 }, "params": { "pixelate": { } } } } ] }
                """);
            var loaded = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            var a = loaded.Areas[0];
            Assert.Equal(12, a.Mosaic.Params["pixelate"]["block"]);
            Assert.Equal(3, a.Mosaic.Common.Blur);
            Assert.Equal(256, a.Mosaic.Common.Levels);
            Assert.Equal(8, a.Mosaic.Common.StripPx);
            Assert.Equal(AreaDefaults.IntervalMsForCurrentOs, a.Mosaic.Common.IntervalMs);
            Assert.Equal("block", a.Generated.Method);
            Assert.Equal(12, a.Generated.Params["block"]["cellSize"]);
            Assert.Equal(0.2, a.Generated.Params["block"]["variance"]);
            Assert.Equal(AreaDefaults.FillColor, a.Fill.Color);
            Assert.Equal(1.0, a.Opacity);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        public void CT25_OtherVersion_IsBroken(int _version)
        {
            File.WriteAllText(FilePath, $"{{ \"version\": {_version}, \"areas\": [] }}");
            AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Broken, status);
        }

        [Theory]
        [InlineData("[null]")]
        [InlineData("[{ \"id\": \"0f3b8d2e\" }, null]")]
        public void CT26_NullArea_IsBroken(string _areas)
        {
            File.WriteAllText(FilePath, $"{{ \"version\": 1, \"areas\": {_areas} }}");
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Broken, status);
            Assert.Empty(state.Areas);
            Assert.True(File.Exists(FilePath + ".broken"));
        }

        [Fact]
        public void Normalize_HugeSizeAndUnknownPattern()
        {
            File.WriteAllText(FilePath, """{ "version": 1, "areas": [ { "id": "0f3b8d2e", "width": 40000, "height": 1e300, "pattern": 7 } ] }""");
            var a = Assert.Single(AreasFile.Load(FilePath, out var status).Areas);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Equal((AreaDefaults.MaxSizeDip, AreaDefaults.MaxSizeDip), (a.Width, a.Height));
            Assert.Equal(PatternKind.Mosaic, a.Pattern);
        }

        [Fact]
        public void CT26_InvalidJson_RenamedAndEmpty()
        {
            File.WriteAllText(FilePath, "{ this is not json");
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Broken, status);
            Assert.Empty(state.Areas);
            Assert.False(File.Exists(FilePath));
            Assert.Equal("{ this is not json", File.ReadAllText(FilePath + ".broken"));
        }

        [Fact]
        public void CT26_SecondBrokenFile_KeepsTheFirst()
        {
            File.WriteAllText(FilePath, "first broken");
            AreasFile.Load(FilePath, out _);
            File.WriteAllText(FilePath, "second broken");
            AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Broken, status);
            Assert.Equal("second broken", File.ReadAllText(FilePath + ".broken"));
            var older = Directory.GetFiles(dir, "areas.json.broken-*");
            Assert.Single(older);
            Assert.Equal("first broken", File.ReadAllText(older[0]));
        }

        [Fact]
        public void LockedFile_IsUnreadable_AndLeftAlone()
        {
            // Another program holding the file exclusively (backup / sync tools on Windows) is not a broken file.
            File.WriteAllText(FilePath, "{ \"version\": 1, \"areas\": [] }");
            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var state = AreasFile.Load(FilePath, out var status);
                Assert.Equal(LoadStatus.Unreadable, status);
                Assert.Empty(state.Areas);
            }
            Assert.True(File.Exists(FilePath));
            Assert.False(File.Exists(FilePath + ".broken"));
        }

        [Fact]
        public void CT27_Save_IsAtomic()
        {
            var state = new AppState { Areas = { AreaState.FromArea(SampleArea(), 1.0) } };
            AreasFile.Save(FilePath, state);
            Assert.False(File.Exists(FilePath + ".tmp"));
            string before = File.ReadAllText(FilePath);

            // NaN cannot be written as JSON: serialization throws before the file is replaced.
            var bad = new AppState { Areas = { new AreaState { Id = "0f3b8d2e", Opacity = double.NaN } } };
            Assert.ThrowsAny<Exception>(() => AreasFile.Save(FilePath, bad));
            Assert.Equal(before, File.ReadAllText(FilePath));
            Assert.False(File.Exists(FilePath + ".tmp"));
        }

        [Fact]
        public void CT28_MissingFile_EmptyWithoutException()
        {
            var state = AreasFile.Load(Path.Combine(dir, "nope.json"), out var status);
            Assert.Equal(LoadStatus.Missing, status);
            Assert.Empty(state.Areas);
        }

        [Theory]
        [InlineData("red")]
        [InlineData("#12345")]
        [InlineData("#GGGGGG")]
        [InlineData("")]
        public void CT29_InvalidColor_FallsBackToDefault(string _color)
        {
            File.WriteAllText(FilePath, $$"""
                { "version": 1, "areas": [ { "id": "0f3b8d2e", "fill": { "color": "{{_color}}" },
                  "generated": { "method": "block", "common": { "baseColor": "{{_color}}", "seed": 1 } } } ] }
                """);
            var a = AreasFile.Load(FilePath, out _).Areas[0];
            Assert.Equal("#68768A", a.Fill.Color);
            Assert.Equal("#68768A", a.Generated.Common.BaseColor);
        }

        [Fact]
        public void CT36_EditModeIsNotStored()
        {
            string json = AreasFile.ToJson(new AppState { Areas = { AreaState.FromArea(SampleArea(), 1.0) } });
            using var doc = JsonDocument.Parse(json);
            var keys = doc.RootElement.GetProperty("areas")[0].EnumerateObject().Select(_p => _p.Name).ToArray();
            Assert.Equal(new[] { "id", "x", "y", "width", "height", "opacity", "pattern", "fill", "mosaic", "generated" }, keys);
        }

        [Fact]
        public void CT98_DuplicateOrBadIds_Replaced()
        {
            File.WriteAllText(FilePath, """{ "version": 1, "areas": [ { "id": "0f3b8d2e" }, { "id": "0f3b8d2e" }, { "id": "XYZ" } ] }""");
            var ids = AreasFile.Load(FilePath, out _).Areas.Select(_a => _a.Id).ToList();
            Assert.Equal(3, ids.Distinct().Count());
            Assert.Equal("0f3b8d2e", ids[0]);
            Assert.All(ids, _id => Assert.True(AreaId.IsValid(_id), _id));
        }

        private static void AssertSameShape(JsonElement _expected, JsonElement _actual, string _path)
        {
            Assert.True(_expected.ValueKind == _actual.ValueKind || IsNumber(_expected) && IsNumber(_actual), $"{_path}: {_expected.ValueKind} vs {_actual.ValueKind}");
            switch (_expected.ValueKind)
            {
                case JsonValueKind.Object:
                    var e = _expected.EnumerateObject().Select(_p => _p.Name).ToArray();
                    var a = _actual.EnumerateObject().Select(_p => _p.Name).ToArray();
                    Assert.True(e.SequenceEqual(a), $"{_path}: keys [{string.Join(",", e)}] vs [{string.Join(",", a)}]");
                    foreach (var name in e)
                    {
                        AssertSameShape(_expected.GetProperty(name), _actual.GetProperty(name), $"{_path}.{name}");
                    }
                    break;
                case JsonValueKind.Array:
                    Assert.Equal(_expected.GetArrayLength(), _actual.GetArrayLength());
                    for (int i = 0; i < _expected.GetArrayLength(); i++)
                    {
                        AssertSameShape(_expected[i], _actual[i], $"{_path}[{i}]");
                    }
                    break;
                case JsonValueKind.Number:
                    Assert.Equal(_expected.GetDouble(), _actual.GetDouble());
                    break;
                case JsonValueKind.String:
                    Assert.Equal(_expected.GetString(), _actual.GetString());
                    break;
            }
        }

        private static bool IsNumber(JsonElement _e) => _e.ValueKind == JsonValueKind.Number;
    }
}
