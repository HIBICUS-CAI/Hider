namespace Hider.Core.Tests
{
    public class StringsTests
    {
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

        private static bool IsJapanese(char _c)
        {
            return _c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿' or >= '＀' and <= '￯' or >= '　' and <= '〿';
        }
    }
}
