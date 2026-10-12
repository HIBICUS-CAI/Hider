using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Hider.Core;
using Hider.Core.Mosaic;

namespace Hider.Settings
{
    /// <summary>
    /// One numeric setting as "label, slider, number box, unit" (FR-37, design canvas 4a–4c). The slider covers the usual
    /// range (log scale where the spec says so); the box accepts anything in Min..Max, and the slider then sticks to its end.
    /// Values outside Min..Max are clamped.
    /// </summary>
    public sealed class ParamRow : Grid
    {
        public const int SliderSteps = 1000;
        private bool updating;

        public ParamRow(ParamSpec _spec, string? _badge = null, double _inputWidth = 56)
        {
            Spec = _spec;
            Height = 36;
            ColumnDefinitions = new ColumnDefinitions("88,*,Auto,20");
            ColumnSpacing = 10;

            var label = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            Label = new TextBlock { Text = Strings.Get(_spec.LabelKey), FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(Label);
            if (_badge is not null)
            {
                Badge = new Border
                {
                    Background = new SolidColorBrush(Color.Parse("#E4E8F0")), CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = _badge, FontSize = 10, LineHeight = 14, Foreground = new SolidColorBrush(Color.Parse("#3E4C66")) },
                };
                label.Children.Add(Badge);
            }
            SetColumn(label, 0);
            Children.Add(label);

            Slider = new Slider { Minimum = 0, Maximum = SliderSteps, VerticalAlignment = VerticalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(Slider, Strings.Get(_spec.LabelKey));
            SetColumn(Slider, 1);
            Children.Add(Slider);

            Input = new TextBox
            {
                Width = _inputWidth, Height = 28, MinHeight = 28, Padding = new Thickness(6, 0), FontSize = 12, TextAlignment = TextAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center, FontFamily = UiFonts.Mono,
            };
            Avalonia.Automation.AutomationProperties.SetName(Input, Strings.Get(_spec.LabelKey));
            SetColumn(Input, 2);
            Children.Add(Input);

            Unit = new TextBlock { Text = Strings.Unit(_spec.Unit), FontSize = 12, Foreground = new SolidColorBrush(Color.Parse("#444444")), VerticalAlignment = VerticalAlignment.Center };
            SetColumn(Unit, 3);
            Children.Add(Unit);

            Slider.PropertyChanged += (_, _e) =>
            {
                if (_e.Property == Avalonia.Controls.Primitives.RangeBase.ValueProperty && !updating)
                {
                    Commit(Round(Spec.FromSliderPosition(Slider.Value / SliderSteps)), false);
                }
            };
            Input.KeyDown += (_, _e) =>
            {
                if (_e.Key == Key.Enter)
                {
                    CommitInput();
                    _e.Handled = true;
                }
            };
            Input.LostFocus += (_, _) => CommitInput();
            // Before the slider's own handling: its small change is 1 / 1000 of the track, which rounds back to the same
            // value in the displayed unit, so arrows would not move it at all.
            Slider.AddHandler(KeyDownEvent, (_, _e) =>
            {
                bool large = _e.KeyModifiers.HasFlag(KeyModifiers.Shift);
                int direction = _e.Key switch
                {
                    Key.Left or Key.Down => -1,
                    Key.Right or Key.Up => 1,
                    Key.PageDown => -10,
                    Key.PageUp => 10,
                    _ => 0,
                };
                if (direction != 0)
                {
                    Step(Math.Sign(direction), large || Math.Abs(direction) > 1);
                    _e.Handled = true;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        }

        public ParamSpec Spec { get; }
        public TextBlock Label { get; }
        public Border? Badge { get; }
        public Slider Slider { get; }
        public TextBox Input { get; }
        public TextBlock Unit { get; }

        /// <summary>The stored value (a fraction for "%").</summary>
        public double Value { get; private set; }

        /// <summary>Value changed by the user; the flag is true for a typed value (save now) and false while sliding.</summary>
        public event Action<double, bool>? ValueChanged;

        /// <summary>Shows a value without raising <see cref="ValueChanged"/>.</summary>
        public void SetValue(double _value)
        {
            Value = Spec.Clamp(_value);
            updating = true;
            try
            {
                Slider.Value = Spec.ToSliderPosition(Value) * SliderSteps;
                Input.Text = Format(Value);
            }
            finally
            {
                updating = false;
            }
        }

        /// <summary>
        /// One keyboard step: 1% of the slider track (10% when large), at least one displayed unit (1 px, 1 ms, 1 %), as if
        /// the slider had been moved.
        /// </summary>
        public void Step(int _direction, bool _large)
        {
            double position = Spec.ToSliderPosition(Value) + _direction * (_large ? 0.1 : 0.01);
            double value = Round(Spec.FromSliderPosition(position));
            if (_direction > 0 ? value <= Value : value >= Value)
            {
                value = Value + _direction / Spec.DisplayFactor;
            }
            Commit(value, false);
        }

        /// <summary>Applies the text in the box (Enter / focus loss), clamped to Min..Max. Full-width digits are accepted.</summary>
        public void CommitInput()
        {
            if (!double.TryParse(NumberText.Normalize(Input.Text), NumberStyles.Float, CultureInfo.InvariantCulture, out double shown))
            {
                Input.Text = Format(Value);
                return;
            }
            Commit(Round(shown / Spec.DisplayFactor), true);
        }

        private void Commit(double _value, bool _typed)
        {
            double value = Spec.Clamp(_value);
            bool changed = Math.Abs(value - Value) > 1e-12;
            SetValue(value);
            if (changed)
            {
                ValueChanged?.Invoke(Value, _typed);
            }
        }

        /// <summary>Whole numbers in the displayed unit (1 px, 1 ms, 1 %).</summary>
        private double Round(double _value)
        {
            return Math.Round(_value * Spec.DisplayFactor) / Spec.DisplayFactor;
        }

        private string Format(double _value)
        {
            return Math.Round(_value * Spec.DisplayFactor).ToString(CultureInfo.InvariantCulture);
        }
    }
}
