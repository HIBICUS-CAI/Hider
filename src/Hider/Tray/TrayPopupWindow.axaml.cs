using Avalonia.Controls;

namespace Hider.Tray
{
    /// <summary>Borderless menu window at the cursor for the tray's left click on Windows (design §6.3, GT-19).</summary>
    public partial class TrayPopupWindow : Window
    {
        public TrayPopupWindow()
        {
            InitializeComponent();
            Deactivated += (_, _) => Close();
            Items.CommandSelected += _c =>
            {
                Close();
                CommandSelected?.Invoke(_c);
            };
        }

        public event Action<TrayCommand>? CommandSelected;

        public IReadOnlyList<Button> Buttons => Items.Buttons;

        public void SetItems(IReadOnlyList<TrayMenuItem> _items)
        {
            Items.SetItems(_items);
        }
    }
}
