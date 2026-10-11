using Avalonia.Controls;

namespace Hider.Tray
{
    /// <summary>
    /// The tray menu in an ordinary small window, for Linux desktops without a tray (D-9, PT-15). Closing it only minimises
    /// it, because it is the only way to reach the menu; "終了" in the menu exits.
    /// </summary>
    public partial class TrayFallbackWindow : Window
    {
        private bool closingAllowed;

        public TrayFallbackWindow()
        {
            InitializeComponent();
            Icon = AppIcons.CreateWindowIcon();
            Items.CommandSelected += c => CommandSelected?.Invoke(c);
            Closing += (_, e) =>
            {
                if (Hider.Areas.AreaWindow.KeepOpen(closingAllowed, e.CloseReason))
                {
                    e.Cancel = true;
                    WindowState = WindowState.Minimized;
                }
            };
        }

        public event Action<TrayCommand>? CommandSelected;

        public IReadOnlyList<Button> Buttons => Items.Buttons;

        public string StatusText => Status.Text ?? "";

        public void SetItems(IReadOnlyList<TrayMenuItem> _items, string _status)
        {
            Items.SetItems(_items);
            Status.Text = _status;
        }

        public void CloseForExit()
        {
            closingAllowed = true;
            Close();
        }
    }
}
