using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Hider.Dialogs;

namespace Hider.App.Tests.Dialogs
{
    /// <summary>The delete-all question (FR-05, design canvas 5) on its own.</summary>
    public class ConfirmDeleteWindowTests
    {
        private static void Click(Button _button)
        {
            _button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }

        [AvaloniaFact]
        public void GT88_Window_IsASmallFramelessTopmostDialog()
        {
            var dialog = new ConfirmDeleteWindow();
            Assert.Equal(400, dialog.Width);
            Assert.Equal(176, dialog.Height);
            Assert.Equal(WindowDecorations.None, dialog.WindowDecorations);
            Assert.False(dialog.CanResize);
            Assert.True(dialog.Topmost);
            Assert.False(dialog.ShowInTaskbar);
            Assert.Equal(WindowStartupLocation.CenterScreen, dialog.WindowStartupLocation);
            Assert.Equal("Hider", dialog.Title);
        }

        [AvaloniaFact]
        public void GT18_Texts_AreTheJapaneseStrings()
        {
            var dialog = new ConfirmDeleteWindow();
            Assert.Equal("Hider", dialog.TitleText.Text);
            Assert.Equal("この操作は元に戻せません。", dialog.NoteText.Text);
            Assert.Equal("キャンセル", dialog.CancelButton.Content);
            Assert.Equal("削除", dialog.DeleteButton.Content);
            Assert.Equal("閉じる", ToolTip.GetTip(dialog.CloseButton));
            Assert.Equal("閉じる", AutomationProperties.GetName(dialog.CloseButton));
            Assert.NotNull(dialog.AppIcon.Source);
            Assert.True(dialog.CancelButton.IsCancel); // Esc answers "cancel"
            Assert.Null(dialog.MessageText.Text); // until the count is set
        }

        [AvaloniaTheory]
        [InlineData(0, "0 個のエリアをすべて削除します。")]
        [InlineData(1, "1 個のエリアをすべて削除します。")]
        [InlineData(12, "12 個のエリアをすべて削除します。")]
        public void GT18_SetCount_PutsTheNumberInTheQuestion(int _count, string _text)
        {
            var dialog = new ConfirmDeleteWindow();
            dialog.SetCount(_count);
            Assert.Equal(_text, dialog.MessageText.Text);
            dialog.SetCount(_count + 1); // the count can change while it is open
            Assert.Equal($"{_count + 1} 個のエリアをすべて削除します。", dialog.MessageText.Text);
        }

        [AvaloniaFact]
        public async Task GT18_Delete_AnswersTrue_AndCloses()
        {
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            Assert.True(dialog.IsVisible);
            Assert.False(ask.IsCompleted);
            Click(dialog.DeleteButton);
            Assert.False(dialog.IsVisible);
            Assert.True(await ask);
        }

        [AvaloniaFact]
        public async Task GT18_Cancel_AnswersFalse_AndCloses()
        {
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            Click(dialog.CancelButton);
            Assert.False(dialog.IsVisible);
            Assert.False(await ask);
        }

        [AvaloniaFact]
        public async Task GT88_CloseButton_AnswersFalse_AndCloses()
        {
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            Click(dialog.CloseButton);
            Assert.False(dialog.IsVisible);
            Assert.False(await ask);
        }

        [AvaloniaFact]
        public async Task GT88_ClosedAnotherWay_AnswersFalse()
        {
            // Alt+F4, or the tray closing it when the areas are gone.
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            dialog.Close();
            Assert.False(await ask);
        }

        [AvaloniaFact]
        public async Task GT88_Escape_AnswersFalse_AndCloses()
        {
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            Dispatcher.UIThread.RunJobs();
            dialog.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ask.IsCompleted);
            Assert.False(dialog.IsVisible);
            Assert.False(await ask);
        }

        [AvaloniaFact]
        public async Task GT88_OnlyTheFirstAnswerCounts()
        {
            var dialog = new ConfirmDeleteWindow();
            var ask = dialog.AskAsync();
            Click(dialog.CancelButton);
            Click(dialog.DeleteButton);
            Assert.False(await ask);
        }

        [AvaloniaFact]
        public void GT88_AskAsync_ShowsAndActivatesTheDialog()
        {
            var dialog = new ConfirmDeleteWindow();
            _ = dialog.AskAsync();
            Dispatcher.UIThread.RunJobs();
            Assert.True(dialog.IsVisible);
            Assert.True(dialog.IsActive);
            dialog.Close();
        }
    }
}
