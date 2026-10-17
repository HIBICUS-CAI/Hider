using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Model;
using Hider.Tray;

namespace Hider.App.Tests.Tray
{
    public class TrayControllerTests
    {
        public static bool IsWindows => OperatingSystem.IsWindows();

        private sealed class Exits
        {
            public int Count { get; set; }
        }

        private static TrayController CreateTray(AreaFixture _f, Exits? _exits = null, Func<Task<bool>>? _probe = null)
        {
            return new TrayController(Application.Current!, _f.Manager, () =>
            {
                if (_exits is not null)
                {
                    _exits.Count++;
                }
            }, _probe);
        }

        private static List<NativeMenuItem> Commands(TrayController _tray)
        {
            return _tray.Menu.Items.Where(_i => _i is not NativeMenuItemSeparator).Cast<NativeMenuItem>().ToList();
        }

        /// <summary>
        /// What a native menu does on a click: the exporter bridge (Avalonia.Controls.INativeMenuItemExporterEventsImplBridge.
        /// RaiseClicked, checked on Avalonia 12.1.4) is hidden from user code, so it is reached by reflection.
        /// </summary>
        private static void ClickNative(NativeMenuItem _item)
        {
            var bridge = typeof(NativeMenuItem).Assembly.GetType("Avalonia.Controls.INativeMenuItemExporterEventsImplBridge")
                ?? throw new InvalidOperationException("Avalonia no longer has INativeMenuItemExporterEventsImplBridge: find what a native menu calls on a click and update ClickNative");
            var raise = bridge.GetMethod("RaiseClicked")
                ?? throw new InvalidOperationException("INativeMenuItemExporterEventsImplBridge.RaiseClicked was renamed: update ClickNative");
            raise.Invoke(_item, null);
        }

        /// <summary>Lets the controller's continuation after the probe run (it resumes on the UI thread).</summary>
        private static async Task AnswerAsync()
        {
            await Task.Yield();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        [AvaloniaFact]
        public void New_RegistersOneVisibleTrayIcon_WithTheMenuAndTooltip()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            var icon = Assert.Single(TrayIcon.GetIcons(Application.Current!)!);
            Assert.True(icon.IsVisible);
            Assert.NotNull(icon.Icon);
            Assert.Same(tray.Menu, icon.Menu);
            Assert.Equal("Hider — エリア 0 個", icon.ToolTipText);
            Assert.Null(tray.FallbackWindow);
            Assert.Null(tray.PopupWindow);
            Assert.Null(tray.ConfirmWindow);
        }

        [AvaloniaFact]
        public void Items_AreTheModelForTheCurrentAreas()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            Assert.Equal(TrayMenuModel.Build(0, false, AppInfo.Version), tray.Items);
            var area = f.Add(new PhysicalRect(100, 100, 300, 200));
            Assert.Equal(TrayMenuModel.Build(1, true, AppInfo.Version), tray.Items);
            area.ExitEdit();
            Assert.Equal(TrayMenuModel.Build(1, false, AppInfo.Version), tray.Items);
        }

        [AvaloniaFact]
        public void Update_FollowsAreaChanges_MenuTooltipAndIcon()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var icon = TrayIcon.GetIcons(Application.Current!)!.Single();

            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();
            f.Add(new PhysicalRect(500, 100, 300, 200)).ExitEdit();

            var items = Commands(tray);
            Assert.Equal(tray.Items.Select(_i => _i.Label), items.Select(_i => _i.Header));
            Assert.Equal(tray.Items.Select(_i => _i.IsEnabled), items.Select(_i => _i.IsEnabled));
            Assert.Equal("Hider — エリア 2 個", icon.ToolTipText);
            Assert.Equal(6, tray.Menu.Items.Count);
            Assert.Equal(3, tray.Menu.Items.Select((_i, _n) => (_i, _n)).Single(_p => _p._i is NativeMenuItemSeparator)._n);
        }

        [AvaloniaFact]
        public void NativeMenuClick_RunsTheItemsCommand()
        {
            using var f = new AreaFixture();
            var exits = new Exits();
            using var tray = CreateTray(f, exits);

            ClickNative(Commands(tray)[0]);
            Assert.Single(f.Manager.Areas);
            f.Manager.Areas[0].ExitEdit();

            ClickNative(Commands(tray)[1]);
            Assert.True(f.Manager.AnyEditing);

            ClickNative(Commands(tray)[^1]);
            Assert.Equal(1, exits.Count);
        }

        [AvaloniaFact]
        public async Task Execute_AddArea_AddsAnAreaInEditMode()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            await tray.ExecuteAsync(TrayCommand.AddArea);

            var area = Assert.Single(f.Manager.Areas);
            Assert.Equal(AreaMode.Editing, area.Mode);
        }

        [AvaloniaFact]
        public async Task Execute_EditAll_ThenEndEdit()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();
            f.Add(new PhysicalRect(500, 100, 300, 200)).ExitEdit();

            await tray.ExecuteAsync(TrayCommand.EditAll);
            Assert.All(f.Manager.Areas, _a => Assert.Equal(AreaMode.Editing, _a.Mode));
            Assert.Equal(TrayCommand.EndEdit, tray.Items[1].Command);

            await tray.ExecuteAsync(TrayCommand.EndEdit);
            Assert.All(f.Manager.Areas, _a => Assert.NotEqual(AreaMode.Editing, _a.Mode));
            Assert.Equal(TrayCommand.EditAll, tray.Items[1].Command);
        }

        [AvaloniaFact]
        public async Task Execute_Exit_CallsTheExitAction_Only()
        {
            using var f = new AreaFixture();
            var exits = new Exits();
            using var tray = CreateTray(f, exits);
            f.Add(new PhysicalRect(100, 100, 300, 200));

            await tray.ExecuteAsync(TrayCommand.Exit);

            Assert.Equal(1, exits.Count);
            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaTheory]
        [InlineData(TrayCommand.Version)]
        [InlineData((TrayCommand)99)]
        public async Task Execute_VersionOrUnknown_DoesNothing(TrayCommand _command)
        {
            using var f = new AreaFixture();
            var exits = new Exits();
            using var tray = CreateTray(f, exits);
            f.Add(new PhysicalRect(100, 100, 300, 200));

            await tray.ExecuteAsync(_command);

            Assert.Single(f.Manager.Areas);
            Assert.Equal(0, exits.Count);
            Assert.Null(tray.ConfirmWindow);
        }

        [AvaloniaFact]
        public async Task ConfirmDeleteAll_HoldsCapturesWhileOpen()
        {
            // The dialog covers areas: their captures wait until it is gone (no dialog pixels in a mosaic).
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();
            Assert.False(f.Manager.AnyPopupOpen);

            var ask = tray.ConfirmDeleteAllAsync();
            Assert.True(f.Manager.AnyPopupOpen);
            Assert.True(tray.ConfirmWindow!.IsVisible);

            tray.ConfirmWindow.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(await ask);
            Assert.False(f.Manager.AnyPopupOpen);
        }

        [AvaloniaFact]
        public async Task ConfirmDeleteAll_AskedAgain_ReplacesTheOpenDialog()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();

            var first = tray.ConfirmDeleteAllAsync();
            var firstWindow = tray.ConfirmWindow!;
            var second = tray.ConfirmDeleteAllAsync();

            Assert.False(await first);
            Assert.False(firstWindow.IsVisible);
            Assert.NotSame(firstWindow, tray.ConfirmWindow);
            Assert.True(tray.ConfirmWindow!.IsVisible);
            Assert.True(f.Manager.AnyPopupOpen); // the second one still holds
            tray.ConfirmWindow.DeleteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(await second);
            Assert.False(f.Manager.AnyPopupOpen);
        }

        [AvaloniaFact]
        public async Task DeleteAll_Cancelled_KeepsTheAreas()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();

            var run = tray.ExecuteAsync(TrayCommand.DeleteAll);
            tray.ConfirmWindow!.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await run;

            Assert.Single(f.Manager.Areas);
        }

        [AvaloniaFact]
        public async Task Update_AfterTheDialogClosed_LeavesItClosed()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();
            var ask = tray.ConfirmDeleteAllAsync();
            var dialog = tray.ConfirmWindow!;
            dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await ask;

            f.Add(new PhysicalRect(500, 100, 300, 200)).ExitEdit();
            f.Manager.DeleteAll();

            Assert.False(dialog.IsVisible);
            Assert.Equal("1 個のエリアをすべて削除します。", dialog.MessageText.Text); // not updated once closed
        }

        [AvaloniaFact]
        public void ShowPopup_WithoutACursor_ShowsTheItemsWhereTheWindowIs()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            tray.ShowPopup();

            var popup = tray.PopupWindow!;
            Assert.True(popup.IsVisible);
            Assert.Equal(new PixelPoint(0, 0), popup.Position); // not placed: Manual startup location at the origin
            Assert.Equal(tray.Items.Select(_i => _i.Command), popup.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            // Active, so a click elsewhere (deactivation) closes it. Headless Show() activates already; the extra Activate()
            // is for a real tray click while another app has the foreground, which only PT-01 can show.
            Assert.True(popup.IsActive);
            popup.Close();
        }

        [AvaloniaFact]
        public void ShowPopup_Again_ClosesTheOpenOne()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            tray.ShowPopup();
            var first = tray.PopupWindow!;

            tray.ShowPopup();

            Assert.False(first.IsVisible);
            Assert.NotSame(first, tray.PopupWindow);
            Assert.True(tray.PopupWindow!.IsVisible);
            tray.PopupWindow.Close();
        }

        [AvaloniaFact]
        public void ShowPopup_AtACursorOnAScreen_OpensAboveLeftOfIt()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var probe = new Window();
            var screen = probe.Screens.Primary!;
            var cursor = new PixelPoint(screen.WorkingArea.Right - 10, screen.WorkingArea.Bottom - 10);

            tray.ShowPopup(cursor);

            var popup = tray.PopupWindow!;
            var size = PixelSize.FromSize(popup.DesiredSize, screen.Scaling);
            Assert.True(size.Width > 200 && size.Height > 100); // measured before placing
            Assert.Equal(TrayController.PlaceAboveLeft(cursor, size, screen.WorkingArea), popup.Position);
            Assert.Equal(new PixelPoint(cursor.X - size.Width, cursor.Y - size.Height), popup.Position);
            popup.Close();
            probe.Close();
        }

        [AvaloniaFact]
        public void ShowPopup_AtACursorOffEveryScreen_IsKeptOnThePrimary()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var probe = new Window();
            var primary = probe.Screens.Primary!;
            var cursor = new PixelPoint(-50000, -50000);
            Assert.Null(probe.Screens.ScreenFromPoint(cursor));

            tray.ShowPopup(cursor);

            Assert.Equal(new PixelPoint(primary.WorkingArea.X, primary.WorkingArea.Y), tray.PopupWindow!.Position);
            tray.PopupWindow.Close();
            probe.Close();
        }

        [AvaloniaFact]
        public void Popup_Command_RunsThroughTheController()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            tray.ShowPopup();

            tray.PopupWindow!.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.Single(f.Manager.Areas);
            Assert.False(tray.PopupWindow.IsVisible);
        }

        [Theory]
        [InlineData(1000, 500, 300, 200, 0, 0, 1920, 1040, 700, 300)]     // room above-left: right-bottom corner at the cursor
        [InlineData(10, 10, 300, 200, 0, 0, 1920, 1040, 0, 0)]            // top-left corner: pushed into the working area
        [InlineData(1910, 1030, 300, 200, 0, 0, 1920, 1040, 1610, 830)]   // bottom-right corner
        [InlineData(2000, 900, 300, 200, 1920, 40, 1280, 984, 1920, 700)] // second monitor with a taskbar on top
        [InlineData(500, 500, 3000, 2000, 0, 0, 1920, 1040, 0, 0)]        // larger than the working area: its top-left wins
        [InlineData(500, 500, 0, 0, 0, 0, 1920, 1040, 500, 500)]          // empty size: at the cursor
        public void PlaceAboveLeft_ClampsIntoTheWorkingArea(int _cx, int _cy, int _w, int _h, int _ax, int _ay, int _aw, int _ah, int _x, int _y)
        {
            var place = TrayController.PlaceAboveLeft(new PixelPoint(_cx, _cy), new PixelSize(_w, _h), new PixelRect(_ax, _ay, _aw, _ah));

            Assert.Equal(new PixelPoint(_x, _y), place);
        }

        [AvaloniaFact(Skip = OsOnly.NotWindowsTrayLeftClick, SkipWhen = nameof(IsWindows))]
        public void IconClick_NotWindows_OpensNothing()
        {
            // Linux AppIndicator opens the native menu itself for both buttons.
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            tray.OnClicked();

            Assert.Null(tray.PopupWindow);
        }

        [AvaloniaFact]
        public void Click_NotWindows_OpensNothing_AndAsksNoCursor()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            int asked = 0;

            tray.OnClicked(false, () =>
            {
                asked++;
                return new PixelPoint(10, 10);
            });

            Assert.Null(tray.PopupWindow);
            Assert.Equal(0, asked);
        }

        [AvaloniaFact]
        public void Click_Windows_OpensThePopupAboveLeftOfTheCursor()
        {
            // The cursor near the bottom-right corner of the primary screen's working area (where the taskbar's tray is): the
            // popup's bottom-right corner is at the cursor.
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var probe = new Window();
            var screen = probe.Screens.Primary!;
            var cursor = new PixelPoint(screen.WorkingArea.Right - 10, screen.WorkingArea.Bottom - 10);
            int asked = 0;

            tray.OnClicked(true, () =>
            {
                asked++;
                return cursor;
            });

            var popup = tray.PopupWindow!;
            Assert.True(popup.IsVisible);
            Assert.Equal(1, asked);
            var size = PixelSize.FromSize(popup.DesiredSize, screen.Scaling);
            Assert.True(size.Width > 200 && size.Height > 100);
            Assert.Equal(new PixelPoint(cursor.X - size.Width, cursor.Y - size.Height), popup.Position);
            Assert.Equal(tray.Items.Select(_i => _i.Command), popup.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            popup.Close();
            probe.Close();
        }

        [AvaloniaFact]
        public void Click_Windows_WithoutACursor_OpensThePopupUnplaced()
        {
            using var f = new AreaFixture();
            using var tray = CreateTray(f);

            tray.OnClicked(true, () => null);

            var popup = tray.PopupWindow!;
            Assert.True(popup.IsVisible);
            Assert.Equal(new PixelPoint(0, 0), popup.Position);
            popup.Close();
        }

        [AvaloniaFact(Skip = OsOnly.TrayLeftClick, SkipUnless = nameof(IsWindows))]
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        public void IconClick_Windows_OpensThePopupNearTheCursor()
        {
            // The real cursor: when it did not move during the click, the popup is above-left of it, kept in the working area
            // of its monitor; when it did, the popup is at least inside a monitor's working area.
            using var f = new AreaFixture();
            using var tray = CreateTray(f);
            var before = Hider.Platform.Windows.Win32Cursor.Position();

            tray.OnClicked();

            var after = Hider.Platform.Windows.Win32Cursor.Position();
            var popup = tray.PopupWindow;
            Assert.NotNull(popup);
            Assert.True(popup!.IsVisible);
            Assert.Equal(tray.Items.Select(_i => _i.Command), popup.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Assert.NotNull(before); // a desktop session always has a cursor
            var screen = popup.Screens.ScreenFromPoint(before!.Value) ?? popup.Screens.Primary!;
            var size = PixelSize.FromSize(popup.DesiredSize, screen.Scaling);
            if (before == after)
            {
                Assert.Equal(TrayController.PlaceAboveLeft(before.Value, size, screen.WorkingArea), popup.Position);
            }
            else
            {
                Assert.Contains(popup.Screens.All, _s => _s.WorkingArea.Contains(popup.Position));
            }
            popup.Close();
        }

        [AvaloniaFact]
        public async Task Probe_TrayFound_LogsIt_AndShowsNoWindow()
        {
            using var f = new AreaFixture();
            var log = new StringWriter();
            TrayController tray;
            using (Log.CaptureTo(log))
            {
                tray = CreateTray(f, null, () => Task.FromResult(true));
            }
            using (tray)
            {
                await AnswerAsync();

                Assert.Null(tray.FallbackWindow);
                Assert.Contains("tray: StatusNotifierWatcher found", log.ToString());
            }
        }

        [AvaloniaFact]
        public async Task Probe_NoTray_LogsIt_AndTheWindowRunsCommands()
        {
            using var f = new AreaFixture();
            var exits = new Exits();
            var log = new StringWriter();
            TrayController tray;
            using (Log.CaptureTo(log))
            {
                tray = CreateTray(f, exits, () => Task.FromResult(false));
            }
            using (tray)
            {
                await AnswerAsync();

                var window = tray.FallbackWindow!;
                Assert.Contains("tray: no StatusNotifierWatcher, showing the menu window (D-9)", log.ToString());
                Assert.True(window.IsVisible);
                window.Buttons[^1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(1, exits.Count);
                Assert.False(window.Buttons.Single(_b => (TrayCommand)_b.Tag! == TrayCommand.Version).IsEnabled);
            }
        }

        [AvaloniaFact]
        public async Task Probe_Fails_LogsIt_AndShowsNoWindow()
        {
            // A probe that throws (it catches its own D-Bus errors, so a bug) is logged, not taken as "no tray".
            using var f = new AreaFixture();
            var log = new StringWriter();
            TrayController tray;
            using (Log.CaptureTo(log))
            {
                tray = CreateTray(f, null, () => Task.FromException<bool>(new IOException("boom")));
            }
            using (tray)
            {
                await AnswerAsync();

                Assert.Null(tray.FallbackWindow);
                Assert.Contains("tray: probing for a StatusNotifierWatcher failed: IOException: boom", log.ToString());
                Assert.DoesNotContain("showing the menu window", log.ToString());
            }
        }

        [AvaloniaFact]
        public async Task Probe_AnsweredAfterDispose_ShowsNoWindow()
        {
            using var f = new AreaFixture();
            var probe = new TaskCompletionSource<bool>();
            var tray = CreateTray(f, null, () => probe.Task);

            tray.Dispose();
            probe.SetResult(false);
            await AnswerAsync();

            Assert.Null(tray.FallbackWindow);
        }

        [AvaloniaFact]
        public async Task Dispose_ClosesEveryWindow_AndStopsFollowingTheAreas()
        {
            using var f = new AreaFixture();
            var tray = CreateTray(f, null, () => Task.FromResult(false));
            await AnswerAsync();
            f.Add(new PhysicalRect(100, 100, 300, 200)).ExitEdit();
            var fallback = tray.FallbackWindow!;
            tray.ShowPopup();
            var popup = tray.PopupWindow!;
            var ask = tray.ConfirmDeleteAllAsync();
            var confirm = tray.ConfirmWindow!;

            tray.Dispose();

            Assert.False(fallback.IsVisible); // closed for good, not minimised
            Assert.False(popup.IsVisible);
            Assert.False(confirm.IsVisible);
            Assert.False(await ask);
            Assert.False(f.Manager.AnyPopupOpen);
            f.Add(new PhysicalRect(500, 100, 300, 200));
            Assert.Equal("すべてのエリアを編集", Commands(tray)[1].Header); // the menu no longer follows (an area is editing now)
            Assert.Equal("Hider — エリア 1 個", fallback.StatusText);
        }

        [AvaloniaFact]
        public void Dispose_WithNothingOpen_DoesNotThrow()
        {
            using var f = new AreaFixture();
            var tray = CreateTray(f);

            var error = Record.Exception(tray.Dispose);

            Assert.Null(error);
        }
    }
}
