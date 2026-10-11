using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace Hider.Tray
{
    /// <summary>Borderless menu window at the cursor for the tray's left click on Windows (design §6.3, GT-19).</summary>
    public partial class TrayPopupWindow : Window
    {
        private static readonly Dictionary<TrayCommand, string> icons = new()
        {
            [TrayCommand.AddArea] = "M8 3v10M3 8h10",
            [TrayCommand.EditAll] = "M11.5 2.5l2 2L5.5 12.5h-2v-2zM10 4l2 2",
            [TrayCommand.EndEdit] = "M3 8.5l3 3 7-7",
            [TrayCommand.DeleteAll] = "M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5",
        };

        public TrayPopupWindow()
        {
            InitializeComponent();
            Deactivated += (_, _) => Close();
        }

        public event Action<TrayCommand>? CommandSelected;

        public IReadOnlyList<Button> Buttons => Items.Children.OfType<Button>().ToArray();

        public void SetItems(IReadOnlyList<TrayMenuItem> _items)
        {
            Items.Children.Clear();
            foreach (var item in _items)
            {
                if (item.SeparatorBefore)
                {
                    Items.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#E1E1E1")), Margin = new Thickness(8, 4) });
                }
                var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
                if (icons.TryGetValue(item.Command, out string? icon))
                {
                    content.Children.Add(new Avalonia.Controls.Shapes.Path
                    {
                        Width = 16, Height = 16, Stretch = Stretch.None, Data = Geometry.Parse(icon),
                        Stroke = new SolidColorBrush(Color.Parse("#1B1B1B")), StrokeThickness = 1.6,
                        StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
                    });
                }
                content.Children.Add(new TextBlock { Text = item.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 13 });
                var button = new Button
                {
                    Content = content, Tag = item.Command, IsEnabled = item.IsEnabled, Height = 36,
                    HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                    VerticalContentAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                    Padding = new Thickness(icons.ContainsKey(item.Command) ? 10 : 36, 0, 10, 0), CornerRadius = new CornerRadius(4),
                };
                var command = item.Command;
                button.Click += (_, _) =>
                {
                    Close();
                    CommandSelected?.Invoke(command);
                };
                Items.Children.Add(button);
            }
        }
    }
}
