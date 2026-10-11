using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Hider.Core.Model;
using Hider.Tray;

namespace Hider.App.Tests.Tray
{
    public class TrayTests
    {
        private static TrayController CreateTray(AreaFixture _f)
        {
            return new TrayController(Avalonia.Application.Current!, _f.Manager, () => { });
        }

        [AvaloniaFact]
        public void GT16_MenuModel_FourItemsAndSeparator()
        {
            var empty = TrayMenuModel.Build(0, false);
            Assert.Equal(new[] { TrayCommand.AddArea, TrayCommand.EditAll, TrayCommand.DeleteAll, TrayCommand.Exit }, empty.Select(i => i.Command));
            Assert.Equal(new[] { true, false, false, true }, empty.Select(i => i.IsEnabled));
            Assert.Equal(new[] { false, false, false, true }, empty.Select(i => i.SeparatorBefore));
            Assert.Equal(new[] { "新しいエリアを追加", "すべてのエリアを編集", "すべてのエリアを削除…", "終了" }, empty.Select(i => i.Label));
            Assert.All(TrayMenuModel.Build(2, false), i => Assert.True(i.IsEnabled));

            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            Assert.Equal(5, tray.Menu.Items.Count);
            Assert.IsType<NativeMenuItemSeparator>(tray.Menu.Items[3]);
            Assert.False(((NativeMenuItem)tray.Menu.Items[1]).IsEnabled);
        }

        [AvaloniaFact]
        public void GT17_WhileEditing_SecondItemEndsEditing()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var area = f.Add(new PhysicalRect(100, 100, 300, 200));
            Assert.Equal(TrayCommand.EndEdit, tray.Items[1].Command);
            Assert.Equal("編集を終了", ((NativeMenuItem)tray.Menu.Items[1]).Header);
            area.ExitEdit();
            Assert.Equal(TrayCommand.EditAll, tray.Items[1].Command);
            Assert.Equal("すべてのエリアを編集", ((NativeMenuItem)tray.Menu.Items[1]).Header);
        }

        [AvaloniaFact]
        public async Task GT18_DeleteAllDialog_TextButtonsAndCancel()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            for (int i = 0; i < 3; i++)
            {
                f.Add(new PhysicalRect(100 + i * 50, 100, 300, 200)).ExitEdit();
            }
            var ask = tray.ConfirmDeleteAllAsync();
            var dialog = tray.ConfirmWindow!;
            Assert.Equal("3 個のエリアをすべて削除します。", dialog.MessageText.Text);
            Assert.Equal("キャンセル", dialog.CancelButton.Content);
            Assert.Equal("削除", dialog.DeleteButton.Content);
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await ask);
            Assert.Equal(3, f.Manager.Areas.Count);

            var again = tray.ExecuteAsync(TrayCommand.DeleteAll);
            tray.ConfirmWindow!.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await again;
            Assert.Empty(f.Manager.Areas);
        }

        [AvaloniaFact]
        public void GT19_Popup_HasSameItemsAsNativeMenu()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200));
            tray.ShowPopup();
            var buttons = tray.PopupWindow!.Buttons;
            var native = tray.Menu.Items.Where(i => i is not NativeMenuItemSeparator).Cast<NativeMenuItem>().ToArray();
            Assert.Equal(native.Length, buttons.Count);
            for (int i = 0; i < buttons.Count; i++)
            {
                Assert.Equal(native[i].IsEnabled, buttons[i].IsEnabled);
                Assert.Equal(tray.Items[i].Command, buttons[i].Tag);
                var label = ((StackPanel)buttons[i].Content!).Children.OfType<TextBlock>().Single().Text;
                Assert.Equal(native[i].Header, label);
            }
            tray.PopupWindow.Close();
        }

        [AvaloniaFact]
        public void GT20_Tooltip_FollowsAreaCount()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            Assert.Equal("Hider — エリア 0 個", tray.ToolTip);
            var a = f.Add(new PhysicalRect(100, 100, 300, 200));
            f.Add(new PhysicalRect(500, 100, 300, 200));
            Assert.Equal("Hider — エリア 2 個", tray.ToolTip);
            a.Delete();
            Assert.Equal("Hider — エリア 1 個", tray.ToolTip);
        }
    }
}
