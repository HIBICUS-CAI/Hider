using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Hider.Tray;

namespace Hider.App.Tests.Tray
{
    /// <summary>The Windows left-click popup and the D-9 menu window (both show <see cref="TrayMenuPanel"/>).</summary>
    public class TrayWindowTests
    {
        private static IReadOnlyList<TrayMenuItem> Items(int _areas = 1) => TrayMenuModel.Build(_areas, false, "2.0.0");

        private static void Click(Button _button) => _button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        /// <summary>
        /// What the platform calls when another window takes the focus: IWindowBaseImpl.Deactivated (checked on Avalonia
        /// 12.1.4) is hidden from user code, so it is reached by reflection.
        /// </summary>
        private static void RaiseDeactivated(Window _window)
        {
            var property = typeof(IWindowBaseImpl).GetProperty("Deactivated")
                ?? throw new InvalidOperationException("IWindowBaseImpl.Deactivated was renamed: update RaiseDeactivated");
            var callback = (Action?)property.GetValue(_window.PlatformImpl);
            Assert.NotNull(callback); // the window subscribes when it is created
            callback!();
        }

        [AvaloniaFact]
        public void Popup_IsABorderlessTopmostWindowOutsideTheTaskbar()
        {
            var popup = new TrayPopupWindow();

            Assert.Equal(WindowDecorations.None, popup.WindowDecorations);
            Assert.False(popup.CanResize);
            Assert.True(popup.Topmost);
            Assert.False(popup.ShowInTaskbar);
            Assert.Equal(SizeToContent.WidthAndHeight, popup.SizeToContent);
            Assert.Equal(WindowStartupLocation.Manual, popup.WindowStartupLocation);
            Assert.Empty(popup.Buttons);
            popup.Close();
        }

        [AvaloniaFact]
        public void Popup_SetItems_ShowsTheItems()
        {
            var popup = new TrayPopupWindow();

            popup.SetItems(Items(2));

            Assert.Equal(Items(2).Select(_i => _i.Command), popup.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Assert.Equal(Items(2).Select(_i => _i.IsEnabled), popup.Buttons.Select(_b => _b.IsEnabled));
            popup.Close();
        }

        [AvaloniaFact]
        public void Popup_Click_ClosesFirst_ThenReportsTheCommand()
        {
            var popup = new TrayPopupWindow();
            popup.SetItems(Items());
            popup.Show();
            bool? visibleWhenReported = null;
            var selected = new List<TrayCommand>();
            popup.CommandSelected += _c =>
            {
                visibleWhenReported = popup.IsVisible;
                selected.Add(_c);
            };

            Click(popup.Buttons[0]);

            Assert.Equal(new[] { TrayCommand.AddArea }, selected);
            Assert.False(visibleWhenReported); // a dialog the command opens is not covered by the popup
            Assert.False(popup.IsVisible);
        }

        [AvaloniaFact]
        public void Popup_ClickWithoutSubscribers_StillCloses()
        {
            var popup = new TrayPopupWindow();
            popup.SetItems(Items());
            popup.Show();

            Click(popup.Buttons[^1]);

            Assert.False(popup.IsVisible);
        }

        [AvaloniaFact]
        public void Popup_LosingTheFocus_Closes()
        {
            // Like a menu: a click anywhere else dismisses it.
            var popup = new TrayPopupWindow();
            popup.SetItems(Items());
            popup.Show();
            int closed = 0;
            popup.Closed += (_, _) => closed++;

            RaiseDeactivated(popup);

            Assert.False(popup.IsVisible);
            Assert.Equal(1, closed);
        }

        [AvaloniaFact]
        public void Fallback_IsAnOrdinaryFixedSizeWindow_WithTheExplainingTitleAndTheIcon()
        {
            var window = new TrayFallbackWindow();

            Assert.Equal("Hider（トレイが使えないため窓で表示しています）", window.Title);
            Assert.NotNull(window.Icon);
            Assert.False(window.CanResize);
            Assert.Equal(SizeToContent.WidthAndHeight, window.SizeToContent);
            Assert.Equal(WindowStartupLocation.CenterScreen, window.WindowStartupLocation);
            Assert.True(window.ShowInTaskbar); // the only way back to the menu once minimised
            Assert.Empty(window.Buttons);
            Assert.Equal("", window.StatusText);
            window.CloseForExit();
        }

        [AvaloniaFact]
        public void Fallback_SetItems_ShowsTheItemsAndTheStatus()
        {
            var window = new TrayFallbackWindow();

            window.SetItems(Items(3), "Hider — エリア 3 個");

            Assert.Equal(Items(3).Select(_i => _i.Command), window.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Assert.Equal("Hider — エリア 3 個", window.StatusText);
            window.SetItems(Items(0), "");
            Assert.False(window.Buttons[1].IsEnabled);
            Assert.Equal("", window.StatusText);
            window.CloseForExit();
        }

        [AvaloniaFact]
        public void Fallback_Click_ReportsTheCommand_AndStaysOpen()
        {
            var window = new TrayFallbackWindow();
            window.SetItems(Items(), "x");
            window.Show();
            var selected = new List<TrayCommand>();
            window.CommandSelected += selected.Add;

            Click(window.Buttons[2]);
            Click(window.Buttons[0]);

            Assert.Equal(new[] { TrayCommand.DeleteAll, TrayCommand.AddArea }, selected);
            Assert.True(window.IsVisible);
            window.CloseForExit();
        }

        [AvaloniaFact]
        public void Fallback_ClickWithoutSubscribers_DoesNothing()
        {
            var window = new TrayFallbackWindow();
            window.SetItems(Items(), "x");
            window.Show();

            var error = Record.Exception(() => Click(window.Buttons[0]));

            Assert.Null(error);
            Assert.True(window.IsVisible);
            window.CloseForExit();
        }

        [AvaloniaFact]
        public void Fallback_UserClose_Minimises_CloseForExit_Closes()
        {
            var window = new TrayFallbackWindow();
            window.Show();
            int closed = 0;
            window.Closed += (_, _) => closed++;

            window.Close();
            Assert.True(window.IsVisible);
            Assert.Equal(WindowState.Minimized, window.WindowState);
            Assert.Equal(0, closed);

            window.CloseForExit();
            Assert.False(window.IsVisible);
            Assert.Equal(1, closed);
        }

        /// <summary>
        /// What the platform asks before closing a window for <paramref name="_reason"/> (sign-out, the app's own shutdown):
        /// IWindowImpl.Closing (checked on Avalonia 12.1.4) is hidden from user code, so it is reached by reflection. True:
        /// the window refused.
        /// </summary>
        private static bool RaiseClosing(Window _window, WindowCloseReason _reason)
        {
            var property = typeof(IWindowImpl).GetProperty("Closing")
                ?? throw new InvalidOperationException("IWindowImpl.Closing was renamed: update RaiseClosing");
            var callback = (Func<WindowCloseReason, bool>?)property.GetValue(_window.PlatformImpl);
            Assert.NotNull(callback); // the window subscribes when it is created
            return callback!(_reason);
        }

        [AvaloniaTheory]
        [InlineData(WindowCloseReason.OSShutdown)]
        [InlineData(WindowCloseReason.ApplicationShutdown)]
        public void Fallback_SessionOrAppShutdown_IsNotHeldUp_NorMinimised(WindowCloseReason _reason)
        {
            // Refusing a sign-out would make the desktop report Hider as blocking it (AreaWindow.KeepOpen).
            var window = new TrayFallbackWindow();
            window.Show();

            bool refused = RaiseClosing(window, _reason);

            Assert.False(refused);
            Assert.Equal(WindowState.Normal, window.WindowState);
            window.CloseForExit();
        }

        [AvaloniaTheory]
        [InlineData(WindowCloseReason.WindowClosing)]
        [InlineData(WindowCloseReason.OwnerWindowClosing)]
        [InlineData(WindowCloseReason.Undefined)]
        public void Fallback_OtherCloses_AreRefused_AndMinimise(WindowCloseReason _reason)
        {
            var window = new TrayFallbackWindow();
            window.Show();

            bool refused = RaiseClosing(window, _reason);

            Assert.True(refused);
            Assert.Equal(WindowState.Minimized, window.WindowState);
            window.CloseForExit();
        }
    }
}
