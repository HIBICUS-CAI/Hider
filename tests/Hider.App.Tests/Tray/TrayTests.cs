using System.Runtime.Versioning;
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
            Assert.Equal(new[] { TrayCommand.AddArea, TrayCommand.EditAll, TrayCommand.DeleteAll, TrayCommand.Exit }, empty.Select(_i => _i.Command));
            Assert.Equal(new[] { true, false, false, true }, empty.Select(_i => _i.IsEnabled));
            Assert.Equal(new[] { false, false, false, true }, empty.Select(_i => _i.SeparatorBefore));
            Assert.Equal(new[] { "新しいエリアを追加", "すべてのエリアを編集", "すべてのエリアを削除…", "終了" }, empty.Select(_i => _i.Label));
            Assert.All(TrayMenuModel.Build(2, false), _i => Assert.True(_i.IsEnabled));

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

            Assert.Equal("閉じる", Avalonia.Automation.AutomationProperties.GetName(dialog.CloseButton));

            var again = tray.ExecuteAsync(TrayCommand.DeleteAll);
            var second = tray.ConfirmWindow!;
            second.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            // The deletion runs after the Click handler (and its Close) returned, not inside it.
            Assert.Equal(3, f.Manager.Areas.Count);
            Assert.False(second.IsVisible);
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
            var native = tray.Menu.Items.Where(_i => _i is not NativeMenuItemSeparator).Cast<NativeMenuItem>().ToArray();
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

        /// <summary>Lets the controller's continuation after the probe run (it resumes on the UI thread).</summary>
        private static async Task AnswerAsync()
        {
            await Task.Yield();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public async Task D9_NoTray_MenuWindowWithTheSameItems()
        {
            using var f = new AreaFixture();
            var probe = new TaskCompletionSource<bool>();
            using var tray = new TrayController(Avalonia.Application.Current!, f.Manager, () => { }, () => probe.Task);
            Assert.Null(tray.FallbackWindow); // nothing until the D-Bus probe answers
            probe.SetResult(false);
            await AnswerAsync();
            var window = tray.FallbackWindow;
            Assert.NotNull(window);
            Assert.True(window!.IsVisible);
            Assert.Equal(tray.Items.Select(_i => _i.Command), window.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Assert.Equal("Hider — エリア 0 個", window.StatusText);

            // The buttons work and the window follows the menu state (FR-03, FR-07).
            window.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Single(f.Manager.Areas);
            Assert.Equal("Hider — エリア 1 個", window.StatusText);
            Assert.Equal(TrayCommand.EndEdit, (TrayCommand)window.Buttons[1].Tag!);

            // Closing it would leave no way to the menu: it minimises instead.
            window.Close();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Minimized, window.WindowState);
        }

        [AvaloniaFact]
        public async Task D9_WithTray_NoMenuWindow()
        {
            using var f = new AreaFixture();
            var probe = new TaskCompletionSource<bool>();
            using var tray = new TrayController(Avalonia.Application.Current!, f.Manager, () => { }, () => probe.Task);
            probe.SetResult(true);
            await AnswerAsync();
            Assert.Null(tray.FallbackWindow);
        }

        [Fact]
        [SupportedOSPlatform("linux")] // a managed D-Bus client: runs anywhere, but is Linux-only code
        public async Task D9_ProbeWithoutBus_ReportsNoTray()
        {
            Assert.False(await Hider.Platform.Linux.StatusNotifierProbe.HasWatcherAsync("unix:path=/nonexistent/hider-test-bus"));
        }

        [AvaloniaFact]
        public void FR71_IconsLoadFromAssets()
        {
            Assert.Equal(32, AppIcons.Image.PixelSize.Width);
            Assert.NotNull(AppIcons.CreateWindowIcon());
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
        }

        [Theory]
        [InlineData(1900, 1070, 236, 200, 1664, 840)]   // 100 %: above-left of the cursor near the taskbar
        [InlineData(1900, 1070, 472, 400, 1428, 640)]   // 200 %: the same DIP size is twice as many pixels
        [InlineData(100, 1070, 472, 400, 0, 640)]       // cursor near the left edge: kept on screen
        [InlineData(1900, 100, 472, 400, 1428, 0)]      // taskbar at the top: kept below the top edge
        public void Popup_PlacedAboveLeftInsideTheWorkingArea(int _cx, int _cy, int _w, int _h, int _x, int _y)
        {
            var place = TrayController.PlaceAboveLeft(new Avalonia.PixelPoint(_cx, _cy), new Avalonia.PixelSize(_w, _h),
                new Avalonia.PixelRect(0, 0, 1920, 1040));
            Assert.Equal(new Avalonia.PixelPoint(_x, _y), place);
        }
    }
}
