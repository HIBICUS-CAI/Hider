using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Hider.Core;

namespace Hider.Dialogs
{
    /// <summary>"N 個のエリアをすべて削除します。" with Cancel / Delete (FR-05, design canvas 5).</summary>
    public partial class ConfirmDeleteWindow : Window
    {
        // Continuations run later, not inside the Click handler before Close() returns.
        private readonly TaskCompletionSource<bool> result = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConfirmDeleteWindow()
        {
            InitializeComponent();
            TitleText.Text = Strings.AppName;
            NoteText.Text = Strings.ConfirmCannotUndo;
            CancelButton.Content = Strings.ConfirmCancel;
            DeleteButton.Content = Strings.ConfirmDelete;
            ToolTip.SetTip(CloseButton, Strings.ConfirmClose);
            Avalonia.Automation.AutomationProperties.SetName(CloseButton, Strings.ConfirmClose);
            AppIcon.Source = AppIcons.Image;
            CancelButton.Click += (_, _) => Finish(false);
            CloseButton.Click += (_, _) => Finish(false);
            DeleteButton.Click += (_, _) => Finish(true);
            Closed += (_, _) => result.TrySetResult(false);
        }

        public void SetCount(int _count)
        {
            MessageText.Text = Strings.ConfirmDeleteAll(_count);
        }

        /// <summary>Shows the dialog and completes with true when the user chose Delete.</summary>
        public Task<bool> AskAsync()
        {
            Show();
            Activate();
            return result.Task;
        }

        private void Finish(bool _delete)
        {
            result.TrySetResult(_delete);
            Close();
        }
    }
}
