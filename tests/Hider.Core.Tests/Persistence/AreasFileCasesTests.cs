using System.Text.Json;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    /// <summary>AreasFile: every load outcome, every repair of Normalize, FillDefaults and the failure paths of Save.</summary>
    public sealed class AreasFileCasesTests : IDisposable
    {
        private static readonly string[] olderBroken = { "broken 0", "broken 1" };
        private static readonly string[] mosaicMethodIds = { "blur", "hexcell", "pixelate" };
        private static readonly string[] generatedMethodIds = { "block", "noise" };
        private static readonly string[] blockKeys = { "cellSize", "gap", "variance" };
        private static readonly string[] noiseKeys = { "levels", "scale" };

        private readonly string dir = Path.Combine(Path.GetTempPath(), "hider-tests-" + Guid.NewGuid().ToString("N"));
        private string FilePath => Path.Combine(dir, "areas.json");

        public AreasFileCasesTests()
        {
            Directory.CreateDirectory(dir);
        }

        public void Dispose()
        {
            Directory.Delete(dir, true);
        }

        private AreaState LoadOne(string _areaJson)
        {
            File.WriteAllText(FilePath, $$"""{ "version": 1, "areas": [ {{_areaJson}} ] }""");
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            return Assert.Single(state.Areas);
        }

        private static AppState StateWith(AreaState _area)
        {
            return new AppState { Areas = { _area } };
        }

        [Fact]
        public void LoadStatus_Values()
        {
            Assert.Equal(new[] { LoadStatus.Loaded, LoadStatus.Missing, LoadStatus.Broken, LoadStatus.Unreadable }, Enum.GetValues<LoadStatus>());
        }

        [Fact]
        public void Load_EmptyAreas_IsLoaded()
        {
            File.WriteAllText(FilePath, """{ "version": 1, "areas": [] }""");
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Empty(state.Areas);
            Assert.Equal(1, state.Version);
            Assert.True(File.Exists(FilePath)); // a good file stays where it is
        }

        [Fact]
        public void Load_MissingAreasKey_IsLoadedEmpty()
        {
            File.WriteAllText(FilePath, """{ "version": 1 }""");
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Empty(state.Areas);
        }

        [Fact]
        public void Load_MissingVersion_IsVersionOne()
        {
            // The default of a missing key is the current version.
            File.WriteAllText(FilePath, """{ "areas": [] }""");
            AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
        }

        [Fact]
        public void Load_UnknownKeys_AreIgnored()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "editMode": true, "future": { "x": 1 } }""");
            Assert.Equal("0f3b8d2e", a.Id);
        }

        [Fact]
        public void Load_CaseOfKeys_MustMatch()
        {
            // camelCase only: "ID" is an unknown key, so the area gets a fresh id instead of "0f3b8d2e".
            var a = LoadOne("""{ "ID": "0f3b8d2e" }""");
            Assert.NotEqual("0f3b8d2e", a.Id);
            Assert.True(AreaId.IsValid(a.Id));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("{ \"version\": 1, \"areas\": null }")]
        [InlineData("{ \"version\": 1, \"areas\": {} }")]
        [InlineData("{ \"version\": \"1\", \"areas\": [] }")]
        [InlineData("{ \"version\": 1, \"areas\": [] } // comment")]
        [InlineData("{ \"version\": 1, \"areas\": [], }")]
        [InlineData("{ \"version\": 1, \"areas\": [ { \"id\": \"0f3b8d2e\", \"x\": null } ] }")]
        [InlineData("{ \"version\": 1, \"areas\": [ { \"id\": \"0f3b8d2e\", \"opacity\": \"high\" } ] }")]
        [InlineData("{ \"version\": 1, \"areas\": [ 5 ] }")]
        public void Load_NotAVersionOneState_IsBroken(string _text)
        {
            File.WriteAllText(FilePath, _text);
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Broken, status);
            Assert.Empty(state.Areas);
            Assert.False(File.Exists(FilePath));
            Assert.Equal(_text, File.ReadAllText(FilePath + ".broken"));
        }

        [Fact]
        public void Load_PathIsADirectory_IsMissing()
        {
            Directory.CreateDirectory(FilePath);
            var state = AreasFile.Load(FilePath, out var status);
            Assert.Equal(LoadStatus.Missing, status);
            Assert.Empty(state.Areas);
        }

        [Fact]
        public void Load_BrokenFileThatCannotBeMovedAside_IsUnreadable_AndLeftAlone()
        {
            // A folder named areas.json.broken is in the way: the broken file must not be overwritten by a later save.
            File.WriteAllText(FilePath, "not json");
            Directory.CreateDirectory(Path.Combine(FilePath + ".broken", "inside"));
            using var log = new StringWriter();
            AppState state;
            LoadStatus status;
            using (Log.CaptureTo(log))
            {
                state = AreasFile.Load(FilePath, out status);
            }
            Assert.Equal(LoadStatus.Unreadable, status);
            Assert.Empty(state.Areas);
            Assert.Equal("not json", File.ReadAllText(FilePath));
            Assert.Contains("areas.json could not be moved aside", log.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void Load_LockedFile_LogsTheReason()
        {
            File.WriteAllText(FilePath, """{ "version": 1, "areas": [] }""");
            using var log = new StringWriter();
            using (new FileStream(FilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            using (Log.CaptureTo(log))
            {
                AreasFile.Load(FilePath, out var status);
                Assert.Equal(LoadStatus.Unreadable, status);
            }
            Assert.Contains("areas.json could not be read", log.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public void Load_ThirdBrokenFile_KeepsBothOlderOnes()
        {
            for (int i = 0; i < 3; i++)
            {
                File.WriteAllText(FilePath, $"broken {i}");
                AreasFile.Load(FilePath, out var status);
                Assert.Equal(LoadStatus.Broken, status);
            }
            Assert.Equal("broken 2", File.ReadAllText(FilePath + ".broken"));
            var older = Directory.GetFiles(dir, "areas.json.broken-*").Select(File.ReadAllText).Order().ToArray();
            Assert.Equal(olderBroken, older);
        }

        [Fact]
        public void Load_NullSettings_ReplacedWithDefaults()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "fill": null, "mosaic": null, "generated": null }""");
            Assert.Equal(AreaDefaults.FillColor, a.Fill.Color);
            Assert.Equal(AreaDefaults.MosaicMethod, a.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, a.Generated.Method);
            Assert.Equal(AreaDefaults.MosaicLevels, a.Mosaic.Common.Levels);
            Assert.Equal(AreaDefaults.Seed, a.Generated.Common.Seed);
            Assert.Equal(12, a.Mosaic.Params["pixelate"]["block"]);
            Assert.Equal(12, a.Generated.Params["block"]["cellSize"]);
        }

        [Fact]
        public void Load_NullCommonAndParams_ReplacedWithDefaults()
        {
            var a = LoadOne("""
                { "id": "0f3b8d2e", "fill": { "color": null },
                  "mosaic": { "method": null, "common": null, "params": null },
                  "generated": { "method": null, "common": null, "params": null } }
                """);
            Assert.Equal(AreaDefaults.FillColor, a.Fill.Color);
            Assert.Equal(AreaDefaults.MosaicMethod, a.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, a.Generated.Method);
            Assert.Equal(AreaDefaults.StripPx, a.Mosaic.Common.StripPx);
            Assert.Equal(AreaDefaults.FillColor, a.Generated.Common.BaseColor);
            Assert.Equal(mosaicMethodIds, a.Mosaic.Params.Keys.Order());
            Assert.Equal(generatedMethodIds, a.Generated.Params.Keys.Order());
        }

        [Fact]
        public void Load_NullParamsOfAMethod_FilledWithDefaults()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "mosaic": { "method": "pixelate", "params": { "pixelate": null, "hexcell": { "radius": 30 } } } }""");
            Assert.Equal(12, a.Mosaic.Params["pixelate"]["block"]);
            Assert.Equal(30, a.Mosaic.Params["hexcell"]["radius"]);
        }

        [Fact]
        public void Load_EmptyMethodIds_BecomeTheDefaults()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "mosaic": { "method": "" }, "generated": { "method": "" } }""");
            Assert.Equal(AreaDefaults.MosaicMethod, a.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, a.Generated.Method);
        }

        [Fact]
        public void Load_KnownMethodsOfTheRightKind_AreKept()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "mosaic": { "method": "blur" }, "generated": { "method": "noise" } }""");
            Assert.Equal("blur", a.Mosaic.Method);
            Assert.Equal("noise", a.Generated.Method);
        }

        [Fact]
        public void Load_UnknownMosaicMethod_IsKept()
        {
            var a = LoadOne("""{ "id": "0f3b8d2e", "mosaic": { "method": "from-the-future" } }""");
            Assert.Equal("from-the-future", a.Mosaic.Method);
        }

        [Theory]
        [InlineData("\"fill\"", PatternKind.Fill)]
        [InlineData("\"mosaic\"", PatternKind.Mosaic)]
        [InlineData("\"pattern\"", PatternKind.Generated)]
        [InlineData("2", PatternKind.Generated)]
        [InlineData("-1", PatternKind.Mosaic)]
        [InlineData("3", PatternKind.Mosaic)]
        public void Load_Pattern_KnownKeptUnknownBecomesMosaic(string _value, PatternKind _expected)
        {
            Assert.Equal(_expected, LoadOne($$"""{ "id": "0f3b8d2e", "pattern": {{_value}} }""").Pattern);
        }

        [Fact]
        public void Normalize_Ids_BadOrDuplicateReplaced_FirstOfADuplicateKept()
        {
            var state = new AppState
            {
                Areas =
                {
                    new AreaState { Id = "" },
                    new AreaState { Id = "1a2b3c4d" },
                    new AreaState { Id = "1A2B3C4D" },
                    new AreaState { Id = "1a2b3c4d" },
                    new AreaState { Id = "deadbeef" },
                },
            };
            var random = new ScriptedRandom(0x0000_0001, 0x0000_0002, 0x0000_0003);
            AreasFile.Normalize(state, random);
            Assert.Equal("00000001 1a2b3c4d 00000002 00000003 deadbeef", string.Join(' ', state.Areas.Select(_a => _a.Id)));
            Assert.Equal(0, random.Left);
        }

        [Fact]
        public void Normalize_ReplacementTakenByTheIdsBefore_IsDrawnAgain()
        {
            // The first draw for the third area is the second area's id: drawn again.
            var state = new AppState { Areas = { new AreaState { Id = "0f3b8d2e" }, new AreaState { Id = "1a2b3c4d" }, new AreaState { Id = "bad" } } };
            var random = new ScriptedRandom(0x1a2b3c4d, 0x0f3b8d2e, 0x7777_7777);
            AreasFile.Normalize(state, random);
            Assert.Equal("0f3b8d2e 1a2b3c4d 77777777", string.Join(' ', state.Areas.Select(_a => _a.Id)));
            Assert.Equal(0, random.Left);
        }

        [Fact]
        public void Normalize_ReplacedIdAndALaterValidId_StayDistinct()
        {
            // The replacement of "bad" is unique among the ids seen so far, and here equals the later valid id. That one is
            // checked when its turn comes and replaced as a duplicate; the third, distinct id stays.
            var state = new AppState
            {
                Areas = { new AreaState { Id = "bad" }, new AreaState { Id = "0f3b8d2e" }, new AreaState { Id = "deadbeef" } },
            };
            var random = new ScriptedRandom(0x0f3b8d2e, 0x1a2b3c4d);
            AreasFile.Normalize(state, random);
            Assert.Equal("0f3b8d2e 1a2b3c4d deadbeef", string.Join(' ', state.Areas.Select(_a => _a.Id)));
            Assert.Equal(0, random.Left);
        }

        [Fact]
        public void Normalize_WithoutARandom_DrawsFromTheShared()
        {
            var state = new AppState { Areas = { new AreaState { Id = "bad" }, new AreaState { Id = "bad" } } };
            AreasFile.Normalize(state, null);
            Assert.All(state.Areas, _a => Assert.True(AreaId.IsValid(_a.Id), _a.Id));
            Assert.NotEqual(state.Areas[0].Id, state.Areas[1].Id);
        }

        /// <summary>Returns the given values in order from NextInt64 and fails when they run out.</summary>
        private sealed class ScriptedRandom(params long[] _values) : Random
        {
            private readonly Queue<long> values = new(_values);

            public int Left => values.Count;

            public override long NextInt64(long _minValue, long _maxValue)
            {
                Assert.Equal((0L, 1L << 32), (_minValue, _maxValue));
                Assert.True(values.Count > 0, "more ids drawn than scripted");
                return values.Dequeue();
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(-5, -5)]
        [InlineData(AreasFile.MaxCoordinate, AreasFile.MaxCoordinate)]
        [InlineData(AreasFile.MaxCoordinate + 1, AreasFile.MaxCoordinate)]
        [InlineData(-AreasFile.MaxCoordinate - 1, -AreasFile.MaxCoordinate)]
        [InlineData(int.MaxValue, AreasFile.MaxCoordinate)]
        [InlineData(int.MinValue, -AreasFile.MaxCoordinate)]
        public void Normalize_Position_ClampedToMaxCoordinate(int _value, int _expected)
        {
            var area = new AreaState { Id = "0f3b8d2e", X = _value, Y = _value };
            AreasFile.Normalize(StateWith(area));
            Assert.Equal((_expected, _expected), (area.X, area.Y));
        }

        [Fact]
        public void MaxCoordinate_IsTwoToTheTwentieth()
        {
            Assert.Equal(1_048_576, AreasFile.MaxCoordinate);
        }

        [Theory]
        [InlineData(0.5, 0.5)]
        [InlineData(0.0, 0.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(-0.5, 0.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(double.NaN, 1.0)]
        [InlineData(double.PositiveInfinity, 1.0)]
        [InlineData(double.NegativeInfinity, 0.0)]
        public void Normalize_Opacity_Clamped_NaNIsOpaque(double _value, double _expected)
        {
            var area = new AreaState { Id = "0f3b8d2e", Opacity = _value };
            AreasFile.Normalize(StateWith(area));
            Assert.Equal(_expected, area.Opacity);
        }

        [Theory]
        [InlineData(100.0, 100.0)]
        [InlineData(24.0, 24.0)]
        [InlineData(23.99, 24.0)]
        [InlineData(0.0, 24.0)]
        [InlineData(-50.0, 24.0)]
        [InlineData(16384.0, 16384.0)]
        [InlineData(16384.5, 16384.0)]
        public void Normalize_Size_ClampedToMinAndMax(double _value, double _expected)
        {
            var area = new AreaState { Id = "0f3b8d2e", Width = _value, Height = _value };
            AreasFile.Normalize(StateWith(area));
            Assert.Equal((_expected, _expected), (area.Width, area.Height));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void Normalize_SizeThatIsNoNumber_IsTheNewAreaSize(double _value)
        {
            var area = new AreaState { Id = "0f3b8d2e", Width = _value, Height = _value };
            AreasFile.Normalize(StateWith(area));
            Assert.Equal((AreaDefaults.NewAreaWidthDip, AreaDefaults.NewAreaHeightDip), (area.Width, area.Height));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1234, 1234)]
        [InlineData(65535, 65535)]
        [InlineData(65536, 0)]
        [InlineData(65537, 1)]
        [InlineData(-1, 65535)]
        [InlineData(-65536, 0)]
        [InlineData(int.MaxValue, 65535)]
        [InlineData(int.MinValue, 0)]
        public void Normalize_Seed_IntoTheHashRange(int _seed, int _expected)
        {
            var area = new AreaState { Id = "0f3b8d2e" };
            area.Generated.Common.Seed = _seed;
            AreasFile.Normalize(StateWith(area));
            Assert.Equal(_expected, area.Generated.Common.Seed);
        }

        [Theory]
        [InlineData("#abcdef", "#ABCDEF")]
        [InlineData("#ABCDEF", "#ABCDEF")]
        [InlineData("abcdef", AreaDefaults.FillColor)]
        public void Normalize_Colors_UpperCaseOrDefault(string _color, string _expected)
        {
            var area = new AreaState { Id = "0f3b8d2e" };
            area.Fill.Color = _color;
            area.Generated.Common.BaseColor = _color;
            AreasFile.Normalize(StateWith(area));
            Assert.Equal(_expected, area.Fill.Color);
            Assert.Equal(_expected, area.Generated.Common.BaseColor);
        }

        [Fact]
        public void Normalize_MosaicCommon_EachValueClampedToItsRange()
        {
            var low = new AreaState { Id = "0f3b8d2e" };
            low.Mosaic.Common.Blur = -1;
            low.Mosaic.Common.Levels = 1;
            low.Mosaic.Common.IntervalMs = -100;
            low.Mosaic.Common.StripPx = -3;
            var high = new AreaState { Id = "1a2b3c4d" };
            high.Mosaic.Common.Blur = 1000;
            high.Mosaic.Common.Levels = 1000;
            high.Mosaic.Common.IntervalMs = int.MaxValue;
            high.Mosaic.Common.StripPx = 5000;
            var nan = new AreaState { Id = "2b3c4d5e" };
            nan.Mosaic.Common.Blur = double.NaN;
            AreasFile.Normalize(new AppState { Areas = { low, high, nan } });
            Assert.Equal((0.0, 2, 0, 0), (low.Mosaic.Common.Blur, low.Mosaic.Common.Levels, low.Mosaic.Common.IntervalMs, low.Mosaic.Common.StripPx));
            Assert.Equal((128.0, 256, 3600000, 4096), (high.Mosaic.Common.Blur, high.Mosaic.Common.Levels, high.Mosaic.Common.IntervalMs, high.Mosaic.Common.StripPx));
            Assert.Equal(AreaDefaults.MosaicBlur, nan.Mosaic.Common.Blur);
        }

        [Fact]
        public void Normalize_MosaicCommon_InRangeValuesKept()
        {
            var area = new AreaState { Id = "0f3b8d2e" };
            area.Mosaic.Common.Blur = 2.5;
            area.Mosaic.Common.Levels = 16;
            area.Mosaic.Common.IntervalMs = 250;
            area.Mosaic.Common.StripPx = 3;
            AreasFile.Normalize(StateWith(area));
            Assert.Equal((2.5, 16, 250, 3), (area.Mosaic.Common.Blur, area.Mosaic.Common.Levels, area.Mosaic.Common.IntervalMs, area.Mosaic.Common.StripPx));
        }

        [Fact]
        public void Normalize_EveryRegisteredMethodGetsItsParameters()
        {
            var area = new AreaState { Id = "0f3b8d2e" };
            AreasFile.Normalize(StateWith(area));
            foreach (var (settings, kind) in new[] { (area.Mosaic.Params, MethodKind.Mosaic), (area.Generated.Params, MethodKind.Generated) })
            {
                foreach (var method in MethodRegistry.OfKind(kind))
                {
                    var values = settings[method.Id];
                    Assert.Equal(method.Params.Select(_p => _p.Key).Order(), values.Keys.Order());
                    Assert.All(method.Params, _p => Assert.Equal(_p.Default, values[_p.Key]));
                }
            }
        }

        [Fact]
        public void Normalize_EmptyState_DoesNothing()
        {
            var state = new AppState();
            AreasFile.Normalize(state);
            Assert.Empty(state.Areas);
        }

        [Fact]
        public void FillDefaults_MissingDefault_PresentClamped_NaNDefault_ExtraKeysKept()
        {
            var values = new Dictionary<string, Dictionary<string, double>>
            {
                ["pixelate"] = new() { ["block"] = 1000, ["extra"] = 5 },
                ["hexcell"] = new() { ["radius"] = double.NaN },
                ["blur"] = new() { ["radius"] = 3 },
            };
            AreasFile.FillDefaults(values, MethodKind.Mosaic);
            Assert.Equal(256, values["pixelate"]["block"]);
            Assert.Equal(5, values["pixelate"]["extra"]);
            Assert.Equal(10, values["hexcell"]["radius"]);
            Assert.Equal(3, values["blur"]["radius"]);
            Assert.Equal(3, values.Count);
        }

        [Fact]
        public void FillDefaults_OnlyTheGivenKind()
        {
            var mosaic = new Dictionary<string, Dictionary<string, double>>();
            AreasFile.FillDefaults(mosaic, MethodKind.Mosaic);
            Assert.Equal(mosaicMethodIds, mosaic.Keys.Order());
            var generated = new Dictionary<string, Dictionary<string, double>>();
            AreasFile.FillDefaults(generated, MethodKind.Generated);
            Assert.Equal(generatedMethodIds, generated.Keys.Order());
            Assert.Equal(blockKeys, generated["block"].Keys.Order());
            Assert.Equal(noiseKeys, generated["noise"].Keys.Order());
        }

        [Fact]
        public void FillDefaults_NullEntry_Replaced_ExistingEntryKeptAsTheSameInstance()
        {
            var existing = new Dictionary<string, double> { ["block"] = 20 };
            var values = new Dictionary<string, Dictionary<string, double>> { ["pixelate"] = existing, ["hexcell"] = null! };
            AreasFile.FillDefaults(values, MethodKind.Mosaic);
            Assert.Same(existing, values["pixelate"]);
            Assert.Equal(20, existing["block"]);
            Assert.Equal(10, values["hexcell"]["radius"]);
        }

        [Fact]
        public void Save_CreatesTheFolder()
        {
            string path = Path.Combine(dir, "a", "b", "areas.json");
            AreasFile.Save(path, new AppState());
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            var loaded = AreasFile.Load(path, out var status);
            Assert.Equal(LoadStatus.Loaded, status);
            Assert.Empty(loaded.Areas);
        }

        [Fact]
        public void Save_ReplacesTheWholeFile()
        {
            File.WriteAllText(FilePath, new string('x', 100_000));
            AreasFile.Save(FilePath, new AppState());
            Assert.Equal(AreasFile.ToJson(new AppState()), File.ReadAllText(FilePath));
        }

        [Fact]
        public void Save_WritesUtf8WithoutByteOrderMark()
        {
            AreasFile.Save(FilePath, new AppState());
            byte[] bytes = File.ReadAllBytes(FilePath);
            Assert.Equal((byte)'{', bytes[0]);
        }

        [Fact]
        public void Save_TempPathIsAFolder_ThrowsAndLeavesTheFile()
        {
            // The temp file cannot be created, nor deleted (a folder): the original error comes out, the file is untouched.
            File.WriteAllText(FilePath, "before");
            Directory.CreateDirectory(Path.Combine(FilePath + ".tmp", "inside"));
            Assert.ThrowsAny<UnauthorizedAccessException>(() => AreasFile.Save(FilePath, new AppState()));
            Assert.Equal("before", File.ReadAllText(FilePath));
            Assert.True(Directory.Exists(FilePath + ".tmp"));
        }

        [Fact]
        public void Save_TempNameTooLong_ThrowsAndLeavesNothing()
        {
            // A file name that fits only without ".tmp": creating (and deleting) the temp file fails with an IOException.
            string path = Path.Combine(dir, new string('a', 253));
            Assert.ThrowsAny<IOException>(() => AreasFile.Save(path, new AppState()));
            Assert.Empty(Directory.GetFileSystemEntries(dir));
        }

        [Fact]
        public void Save_TargetIsAFolder_ThrowsAndRemovesTheTemp()
        {
            Directory.CreateDirectory(Path.Combine(FilePath, "inside"));
            Assert.ThrowsAny<Exception>(() => AreasFile.Save(FilePath, new AppState()));
            Assert.False(File.Exists(FilePath + ".tmp"));
            Assert.True(Directory.Exists(FilePath));
        }

        [Fact]
        public void ToJson_IndentedCamelCase()
        {
            string json = AreasFile.ToJson(new AppState());
            Assert.Equal("{\n  \"version\": 1,\n  \"areas\": []\n}".ReplaceLineEndings(Environment.NewLine), json.ReplaceLineEndings(Environment.NewLine));
        }

        [Fact]
        public void FromJson_NullLiteral_IsNull()
        {
            Assert.Null(AreasFile.FromJson("null"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("{ \"version\": true }")]
        public void FromJson_Invalid_Throws(string _json)
        {
            Assert.ThrowsAny<JsonException>(() => AreasFile.FromJson(_json));
        }

        [Fact]
        public void FromJson_DoesNotNormalize()
        {
            // Repairs are Load's (Normalize): FromJson gives the values as written.
            var state = AreasFile.FromJson("""{ "version": 2, "areas": [ { "id": "BAD", "opacity": 5, "width": 1 } ] }""")!;
            Assert.Equal(2, state.Version);
            var a = Assert.Single(state.Areas);
            Assert.Equal(("BAD", 5.0, 1.0), (a.Id, a.Opacity, a.Width));
        }
    }
}
