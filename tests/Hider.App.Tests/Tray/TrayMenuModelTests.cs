using Hider.Tray;

namespace Hider.App.Tests.Tray
{
    public class TrayMenuModelTests
    {
        private static readonly TrayCommand[] idleCommands = { TrayCommand.AddArea, TrayCommand.EditAll, TrayCommand.DeleteAll, TrayCommand.Version, TrayCommand.Exit };
        private static readonly TrayCommand[] editingCommands = { TrayCommand.AddArea, TrayCommand.EndEdit, TrayCommand.DeleteAll, TrayCommand.Version, TrayCommand.Exit };
        private static readonly string[] editingLabels = { "新しいエリアを追加", "編集を終了", "すべてのエリアを削除…", "バージョン 2.0.0", "終了" };
        private static readonly bool[] allButVersion = { true, true, true, false, true };
        private static readonly bool[] editingWithoutAreas = { true, true, false, false, true };
        private static readonly TrayCommand[] allCommands =
            { TrayCommand.AddArea, TrayCommand.EditAll, TrayCommand.EndEdit, TrayCommand.DeleteAll, TrayCommand.Version, TrayCommand.Exit };

        [Theory]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(int.MaxValue)]
        public void Build_WithAreas_NotEditing_EverythingButTheVersionIsEnabled(int _count)
        {
            var items = TrayMenuModel.Build(_count, false, "2.0.0");

            Assert.Equal(idleCommands, items.Select(_i => _i.Command));
            Assert.Equal(allButVersion, items.Select(_i => _i.IsEnabled));
        }

        [Fact]
        public void Build_WhileEditing_SecondItemEndsEditing()
        {
            var items = TrayMenuModel.Build(2, true, "2.0.0");

            Assert.Equal(editingCommands, items.Select(_i => _i.Command));
            Assert.Equal(editingLabels, items.Select(_i => _i.Label));
            Assert.Equal(allButVersion, items.Select(_i => _i.IsEnabled));
        }

        [Fact]
        public void Build_EditingWithoutAreas_EndEditStaysEnabled_DeleteAllDoesNot()
        {
            // "End editing" depends on the editing state only; "delete all" on there being areas.
            var items = TrayMenuModel.Build(0, true, "2.0.0");

            Assert.Equal(editingCommands, items.Select(_i => _i.Command));
            Assert.Equal(editingWithoutAreas, items.Select(_i => _i.IsEnabled));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void Build_NoAreas_EditAllAndDeleteAllAreDisabled(int _count)
        {
            var items = TrayMenuModel.Build(_count, false, "2.0.0");

            Assert.False(items.Single(_i => _i.Command == TrayCommand.EditAll).IsEnabled);
            Assert.False(items.Single(_i => _i.Command == TrayCommand.DeleteAll).IsEnabled);
            Assert.True(items.Single(_i => _i.Command == TrayCommand.AddArea).IsEnabled);
            Assert.True(items.Single(_i => _i.Command == TrayCommand.Exit).IsEnabled);
        }

        [Theory]
        [InlineData("2.0.0", "バージョン 2.0.0")]
        [InlineData("10.20.30", "バージョン 10.20.30")]
        [InlineData("", "バージョン ")]
        [InlineData(null, "バージョン 不明")]
        public void Build_VersionRow_ShowsTheVersionOrUnknown(string? _version, string _label)
        {
            var version = TrayMenuModel.Build(0, false, _version).Single(_i => _i.Command == TrayCommand.Version);

            Assert.Equal(_label, version.Label);
            Assert.False(version.IsEnabled);
            Assert.True(version.SeparatorBefore);
        }

        [Theory]
        [InlineData(0, false)]
        [InlineData(3, false)]
        [InlineData(3, true)]
        public void Build_OnlyTheVersionRowHasASeparatorBefore(int _count, bool _editing)
        {
            var items = TrayMenuModel.Build(_count, _editing, "2.0.0");

            Assert.Equal(5, items.Count);
            Assert.Equal(TrayCommand.Version, items.Single(_i => _i.SeparatorBefore).Command);
            Assert.Equal(TrayCommand.Exit, items[^1].Command);
        }

        [Fact]
        public void Build_ReturnsANewListEachTime()
        {
            var first = TrayMenuModel.Build(1, false, "2.0.0");
            var second = TrayMenuModel.Build(1, false, "2.0.0");

            Assert.NotSame(first, second);
            Assert.Equal(first, second); // records compare by value
        }

        [Fact]
        public void TrayMenuItem_IsAValueRecord()
        {
            var item = new TrayMenuItem(TrayCommand.Exit, "終了", true, false);

            Assert.Equal(new TrayMenuItem(TrayCommand.Exit, "終了", true, false), item);
            Assert.NotEqual(item with { IsEnabled = false }, item);
            Assert.NotEqual(item with { SeparatorBefore = true }, item);
            Assert.Equal("終了", item.Label);
        }

        [Fact]
        public void TrayCommand_HasEveryMenuCommand()
        {
            Assert.Equal(allCommands, Enum.GetValues<TrayCommand>());
        }
    }
}
