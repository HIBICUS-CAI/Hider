using System.Globalization;
using Hider.Core.Mosaic;

namespace Hider.Core.Tests
{
    public class StringsTests
    {
        /// <summary>Every UI text (FR-70). A change of wording must change this table too.</summary>
        private static readonly Dictionary<string, string> expectedTexts = new()
        {
            ["startup.lockFailed"] = "Hider を起動できません。データフォルダのロックファイル（{0}）を開けませんでした。\n\n{1}",
            ["tray.add"] = "新しいエリアを追加",
            ["tray.editAll"] = "すべてのエリアを編集",
            ["tray.endEdit"] = "編集を終了",
            ["tray.deleteAll"] = "すべてのエリアを削除…",
            ["tray.version"] = "バージョン {0}",
            ["tray.versionUnknown"] = "不明",
            ["tray.exit"] = "終了",
            ["tray.tooltip"] = "Hider — エリア {0} 個",
            ["confirm.deleteAll"] = "{0} 個のエリアをすべて削除します。",
            ["confirm.cannotUndo"] = "この操作は元に戻せません。",
            ["confirm.cancel"] = "キャンセル",
            ["confirm.delete"] = "削除",
            ["confirm.close"] = "閉じる",
            ["pattern.fill"] = "塗りつぶし",
            ["pattern.mosaic"] = "モザイク",
            ["pattern.generated"] = "パターン",
            ["toolbar.settings"] = "設定",
            ["toolbar.refresh"] = "今すぐ更新（下の内容を撮り直す）",
            ["toolbar.delete"] = "このエリアを削除",
            ["toolbar.done"] = "完了",
            ["toolbar.captureFailed"] = "取得できません",
            ["settings.opacity"] = "透明度",
            ["settings.method"] = "手法",
            ["settings.fillSection"] = "塗りつぶし",
            ["settings.mosaicSection"] = "モザイク（画面の内容を加工）",
            ["settings.generatedSection"] = "パターン（生成・下の内容に依存しない）",
            ["settings.color"] = "色",
            ["settings.presets"] = "プリセット",
            ["settings.baseColor"] = "ベース色",
            ["settings.seed"] = "シード",
            ["settings.reseed"] = "シードを振り直す",
            ["settings.pickColorWithValue"] = "色を選ぶ: {0}",
            ["method.pixelate"] = "ピクセル化",
            ["method.hexcell"] = "六角セル平均",
            ["method.blur"] = "ガウスぼかし",
            ["param.block"] = "ブロック",
            ["param.radius"] = "半径",
            ["param.blur"] = "ぼかし",
            ["param.levels"] = "色数",
            ["param.intervalMs"] = "更新間隔",
            ["param.stripPx"] = "帯の高さ",
            ["method.block"] = "ブロック",
            ["method.noise"] = "ノイズ",
            ["param.cellSize"] = "セルサイズ",
            ["param.variance"] = "色のばらつき",
            ["param.gap"] = "目地幅",
            ["param.noiseScale"] = "ノイズスケール",
            ["param.noiseLevels"] = "段階数",
            ["fallback.title"] = "Hider（トレイが使えないため窓で表示しています）",
        };

        [Fact]
        public void CT12_Strings_NonEmptyAndJapanese()
        {
            Assert.NotEmpty(Strings.All);
            foreach (var (key, text) in Strings.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(text), key);
                Assert.True(text.Any(IsJapanese), $"{key}: \"{text}\" has no Japanese");
            }
        }

        [Fact]
        public void CT12_EveryUnitHasAText()
        {
            foreach (var unit in Enum.GetValues<Hider.Core.Mosaic.ParamUnit>())
            {
                Assert.NotNull(Strings.Unit(unit));
            }
            Assert.Equal("%", Strings.Unit(Hider.Core.Mosaic.ParamUnit.Percent));
        }

        [Fact]
        public void CT97_Strings_FormattedTexts()
        {
            Assert.Equal("Hider — エリア 3 個", Strings.TrayTooltip(3));
            Assert.Equal("3 個のエリアをすべて削除します。", Strings.ConfirmDeleteAll(3));
            Assert.Equal("1920 × 1080 px", Strings.ToolbarSize(1920, 1080));
            Assert.Equal("Hider を起動できません。データフォルダのロックファイル（/x/instance.lock）を開けませんでした。\n\nAccess denied",
                Strings.StartupLockFailed("/x/instance.lock", "Access denied"));
            Assert.Equal("色を選ぶ: #68768A", Strings.SettingsPickColorWithValue("#68768A"));
            Assert.Equal("バージョン 2.1.3", Strings.TrayVersion("2.1.3"));
            Assert.Equal("バージョン 不明", Strings.TrayVersion(null));
        }

        [Fact]
        public void CT12_All_IsExactlyTheTable()
        {
            Assert.Equal(expectedTexts.Keys.Order(StringComparer.Ordinal), Strings.All.Keys.Order(StringComparer.Ordinal));
            foreach (var (key, text) in expectedTexts)
            {
                Assert.Equal(text, Strings.All[key]);
                Assert.Equal(text, Strings.Get(key));
                Assert.True(Strings.Contains(key), key);
            }
        }

        [Fact]
        public void CT12_Properties_AreTheirKeysTexts()
        {
            var properties = new (string Actual, string Key)[]
            {
                (Strings.TrayAdd, "tray.add"),
                (Strings.TrayEditAll, "tray.editAll"),
                (Strings.TrayEndEdit, "tray.endEdit"),
                (Strings.TrayDeleteAll, "tray.deleteAll"),
                (Strings.TrayExit, "tray.exit"),
                (Strings.ConfirmCannotUndo, "confirm.cannotUndo"),
                (Strings.ConfirmCancel, "confirm.cancel"),
                (Strings.ConfirmDelete, "confirm.delete"),
                (Strings.ConfirmClose, "confirm.close"),
                (Strings.PatternFill, "pattern.fill"),
                (Strings.PatternMosaic, "pattern.mosaic"),
                (Strings.PatternGenerated, "pattern.generated"),
                (Strings.ToolbarSettings, "toolbar.settings"),
                (Strings.ToolbarRefresh, "toolbar.refresh"),
                (Strings.ToolbarDelete, "toolbar.delete"),
                (Strings.ToolbarDone, "toolbar.done"),
                (Strings.ToolbarCaptureFailed, "toolbar.captureFailed"),
                (Strings.SettingsOpacity, "settings.opacity"),
                (Strings.SettingsMethod, "settings.method"),
                (Strings.SettingsFillSection, "settings.fillSection"),
                (Strings.SettingsMosaicSection, "settings.mosaicSection"),
                (Strings.SettingsGeneratedSection, "settings.generatedSection"),
                (Strings.SettingsColor, "settings.color"),
                (Strings.SettingsPresets, "settings.presets"),
                (Strings.SettingsBaseColor, "settings.baseColor"),
                (Strings.SettingsSeed, "settings.seed"),
                (Strings.SettingsReseed, "settings.reseed"),
                (Strings.FallbackTitle, "fallback.title"),
            };
            foreach (var (actual, key) in properties)
            {
                Assert.Equal(expectedTexts[key], actual);
            }
        }

        [Fact]
        public void Get_UnknownKey_IsTheKeyItself()
        {
            Assert.Equal("no.such.key", Strings.Get("no.such.key"));
            Assert.Equal("", Strings.Get(""));
            Assert.False(Strings.Contains("no.such.key"));
            Assert.False(Strings.Contains("TRAY.ADD")); // keys are case sensitive
            Assert.Equal("TRAY.ADD", Strings.Get("TRAY.ADD"));
        }

        [Fact]
        public void LabelKeys_OfEveryMethodAndParameter_Exist()
        {
            // The settings popup looks labels up by key: a missing one would show the key instead of Japanese.
            foreach (var method in MethodRegistry.All)
            {
                Assert.True(Strings.Contains(method.LabelKey), method.LabelKey);
                Assert.All(method.Params, _p => Assert.True(Strings.Contains(_p.LabelKey), _p.LabelKey));
            }
            Assert.All(CommonParams.Mosaic, _p => Assert.True(Strings.Contains(_p.LabelKey), _p.LabelKey));
        }

        [Theory]
        [InlineData(ParamUnit.None, "")]
        [InlineData(ParamUnit.Px, "px")]
        [InlineData(ParamUnit.Colors, "色")]
        [InlineData(ParamUnit.Steps, "段")]
        [InlineData(ParamUnit.Percent, "%")]
        [InlineData(ParamUnit.Ms, "ms")]
        public void Unit_EachText(ParamUnit _unit, string _expected)
        {
            Assert.Equal(_expected, Strings.Unit(_unit));
        }

        [Fact]
        public void Unit_Undefined_Throws()
        {
            Assert.Throws<KeyNotFoundException>(() => Strings.Unit((ParamUnit)99));
        }

        [Fact]
        public void ProperNouns()
        {
            Assert.Equal("Linux", Strings.LinuxBadge);
            Assert.Equal("Hider", Strings.AppName);
        }

        [Theory]
        [InlineData(0, "Hider — エリア 0 個")]
        [InlineData(1, "Hider — エリア 1 個")]
        [InlineData(1234567, "Hider — エリア 1234567 個")]
        public void TrayTooltip_Count(int _count, string _expected)
        {
            Assert.Equal(_expected, Strings.TrayTooltip(_count));
        }

        [Theory]
        [InlineData(0, "0 個のエリアをすべて削除します。")]
        [InlineData(1, "1 個のエリアをすべて削除します。")]
        [InlineData(10000, "10000 個のエリアをすべて削除します。")]
        public void ConfirmDeleteAll_Count(int _count, string _expected)
        {
            Assert.Equal(_expected, Strings.ConfirmDeleteAll(_count));
        }

        [Theory]
        [InlineData(24, 24, "24 × 24 px")]
        [InlineData(0, 0, "0 × 0 px")]
        [InlineData(16384, 1, "16384 × 1 px")]
        [InlineData(-5, 3, "-5 × 3 px")]
        public void ToolbarSize_WidthTimesHeight(int _w, int _h, string _expected)
        {
            Assert.Equal(_expected, Strings.ToolbarSize(_w, _h));
        }

        [Fact]
        public void TrayVersion_TextAsGiven()
        {
            Assert.Equal("バージョン 2.0.0-rc.1+abc", Strings.TrayVersion("2.0.0-rc.1+abc"));
            Assert.Equal("バージョン ", Strings.TrayVersion(""));
            Assert.Equal("バージョン {0}", Strings.TrayVersion("{0}")); // the value is not a format itself
        }

        [Fact]
        public void StartupLockFailed_PathAndDetailAsGiven()
        {
            Assert.Equal("Hider を起動できません。データフォルダのロックファイル（C:\\Users\\me\\.hider\\instance.lock）を開けませんでした。\n\n",
                Strings.StartupLockFailed(@"C:\Users\me\.hider\instance.lock", ""));
        }

        [Fact]
        public void SettingsPickColorWithValue_AnyText()
        {
            Assert.Equal("色を選ぶ: #000000", Strings.SettingsPickColorWithValue("#000000"));
            Assert.Equal("色を選ぶ: ", Strings.SettingsPickColorWithValue(""));
        }

        [Fact]
        public void Formats_IgnoreTheCurrentCulture()
        {
            // Numbers are formatted with the invariant culture whatever the thread's culture says.
            var previous = CultureInfo.CurrentCulture;
            var odd = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            odd.NumberFormat.NegativeSign = "~";
            try
            {
                CultureInfo.CurrentCulture = odd;
                Assert.Equal("~5", (-5).ToString(CultureInfo.CurrentCulture)); // the culture is in effect
                Assert.Equal("-5 × -6 px", Strings.ToolbarSize(-5, -6));
                Assert.Equal("Hider — エリア -1 個", Strings.TrayTooltip(-1));
                Assert.Equal("-2 個のエリアをすべて削除します。", Strings.ConfirmDeleteAll(-2));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        private static bool IsJapanese(char _c)
        {
            return _c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or >= '＀' and <= '￯' or >= '　' and <= '〿';
        }
    }
}
