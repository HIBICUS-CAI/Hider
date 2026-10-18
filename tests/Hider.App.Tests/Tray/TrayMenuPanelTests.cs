using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Hider.Tray;

namespace Hider.App.Tests.Tray
{
    public class TrayMenuPanelTests
    {
        private static readonly Dictionary<TrayCommand, string> icons = new()
        {
            [TrayCommand.AddArea] = "M8 3v10M3 8h10",
            [TrayCommand.EditAll] = "M11.5 2.5l2 2L5.5 12.5h-2v-2zM10 4l2 2",
            [TrayCommand.EndEdit] = "M3 8.5l3 3 7-7",
            [TrayCommand.DeleteAll] = "M3 4.5h10M6.5 4.5V3h3v1.5M4.5 4.5l.6 8.5h5.8l.6-8.5",
        };

        private static TrayMenuPanel Panel(int _areas = 1, bool _editing = false)
        {
            var panel = new TrayMenuPanel();
            panel.SetItems(TrayMenuModel.Build(_areas, _editing, "2.0.0"));
            return panel;
        }

        private static string Label(Button _button) => ((StackPanel)_button.Content!).Children.OfType<TextBlock>().Single().Text!;

        [AvaloniaFact]
        public void GT130_New_HasNoButtons_AndTheDesignSpacing()
        {
            var panel = new TrayMenuPanel();

            Assert.Empty(panel.Buttons);
            Assert.Empty(panel.Children);
            Assert.Equal(2, panel.Spacing);
        }

        [AvaloniaFact]
        public void GT19_SetItems_OneButtonPerItem_InOrder_WithTheCommandAsTag()
        {
            var items = TrayMenuModel.Build(2, false, "2.0.0");
            var panel = new TrayMenuPanel();

            panel.SetItems(items);

            Assert.Equal(items.Select(_i => _i.Command), panel.Buttons.Select(_b => (TrayCommand)_b.Tag!));
            Assert.Equal(items.Select(_i => _i.Label), panel.Buttons.Select(Label));
            Assert.Equal(items.Select(_i => _i.IsEnabled), panel.Buttons.Select(_b => _b.IsEnabled));
        }

        [AvaloniaFact]
        public void GT41_SetItems_SeparatorLineBeforeTheVersionRowOnly()
        {
            var panel = Panel();

            var separators = panel.Children.OfType<Border>().ToList();
            var separator = Assert.Single(separators);
            int index = panel.Children.IndexOf(separator);
            Assert.Equal(TrayCommand.Version, (TrayCommand)((Button)panel.Children[index + 1]).Tag!);
            Assert.Equal(1, separator.Height);
            Assert.Equal(new Thickness(8, 4), separator.Margin);
            Assert.Equal(Color.Parse("#E1E1E1"), ((ISolidColorBrush)separator.Background!).Color);
            Assert.Equal(6, panel.Children.Count); // five buttons and the line
        }

        [AvaloniaFact]
        public void GT130_SetItems_CommandsWithAnIcon_HaveTheirPath_AndLessLeftPadding()
        {
            var panel = Panel(1, false);
            var editing = Panel(1, true);

            foreach (var button in panel.Buttons.Concat(editing.Buttons))
            {
                var command = (TrayCommand)button.Tag!;
                var content = (StackPanel)button.Content!;
                var path = content.Children.OfType<Avalonia.Controls.Shapes.Path>().SingleOrDefault();
                if (icons.TryGetValue(command, out string? data))
                {
                    Assert.NotNull(path);
                    Assert.Equal(Geometry.Parse(data).Bounds, path!.Data!.Bounds);
                    Assert.Equal(16, path.Width);
                    Assert.Equal(16, path.Height);
                    Assert.Equal(1.6, path.StrokeThickness);
                    Assert.Equal(PenLineCap.Round, path.StrokeLineCap);
                    Assert.Equal(PenLineJoin.Round, path.StrokeJoin);
                    Assert.Same(path, content.Children[0]); // the icon comes before the label
                    Assert.Equal(new Thickness(10, 0, 10, 0), button.Padding);
                }
                else
                {
                    // Version and exit: no icon, the label lines up with the others' labels.
                    Assert.Null(path);
                    Assert.Equal(new Thickness(36, 0, 10, 0), button.Padding);
                }
            }
        }

        [AvaloniaFact]
        public void GT130_SetItems_ButtonsFollowTheDesign()
        {
            var panel = Panel();

            Assert.All(panel.Buttons, _b =>
            {
                Assert.Equal(36, _b.Height);
                Assert.Equal(HorizontalAlignment.Stretch, _b.HorizontalAlignment);
                Assert.Equal(HorizontalAlignment.Left, _b.HorizontalContentAlignment);
                Assert.Equal(VerticalAlignment.Center, _b.VerticalContentAlignment);
                Assert.Same(Brushes.Transparent, _b.Background);
                Assert.Equal(new Thickness(0), _b.BorderThickness);
                Assert.Equal(new CornerRadius(4), _b.CornerRadius);
                var text = ((StackPanel)_b.Content!).Children.OfType<TextBlock>().Single();
                Assert.Equal(13, text.FontSize);
                Assert.Equal(Orientation.Horizontal, ((StackPanel)_b.Content!).Orientation);
            });
        }

        [AvaloniaFact]
        public void GT19_SetItems_Again_ReplacesTheButtons()
        {
            var panel = Panel(0, false);
            var before = panel.Buttons;

            panel.SetItems(TrayMenuModel.Build(0, true, "2.0.0"));

            Assert.Equal(5, panel.Buttons.Count);
            Assert.DoesNotContain(panel.Buttons, before.Contains);
            Assert.Equal(TrayCommand.EndEdit, (TrayCommand)panel.Buttons[1].Tag!);
            panel.SetItems(Array.Empty<TrayMenuItem>());
            Assert.Empty(panel.Children);
        }

        [AvaloniaFact]
        public void GT19_Click_RaisesCommandSelected_WithTheItemsCommand()
        {
            var panel = Panel(1, false);
            var selected = new List<TrayCommand>();
            panel.CommandSelected += selected.Add;

            foreach (var button in panel.Buttons)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            Assert.Equal(panel.Buttons.Select(_b => (TrayCommand)_b.Tag!), selected);
        }

        [AvaloniaFact]
        public void GT19_Click_WithoutSubscribers_DoesNothing()
        {
            var panel = Panel();

            var error = Record.Exception(() => panel.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

            Assert.Null(error);
        }

        [AvaloniaFact]
        public void GT19_Buttons_IsASnapshot()
        {
            var panel = Panel();
            var buttons = panel.Buttons;

            panel.Children.Clear();

            Assert.Equal(5, buttons.Count);
            Assert.Empty(panel.Buttons);
        }
    }
}
