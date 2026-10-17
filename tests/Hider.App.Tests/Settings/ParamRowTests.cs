using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Hider.Core.Mosaic;
using Hider.Settings;

namespace Hider.App.Tests.Settings
{
    public class ParamRowTests
    {
        // 2..256 px, slider 2..64 (like the mosaic block size).
        private static readonly ParamSpec px = new("block", "param.block", ParamUnit.Px, 2, 256, 2, 64, 12);

        // 0..20 px on the whole track: 1 % of the track is 0.2 px, less than the displayed unit.
        private static readonly ParamSpec small = new("blur", "param.blur", ParamUnit.Px, 0, 20, 0, 20, 4);

        // A fraction shown in percent (opacity of a fill).
        private static readonly ParamSpec percent = new("opacity", "settings.opacity", ParamUnit.Percent, 0.1, 1, 0.1, 1, 1);

        // A box range wider than the slider on both sides.
        private static readonly ParamSpec wide = new("x", "param.block", ParamUnit.Px, 0, 100, 10, 50, 20);

        // "88,*,Auto,20": label, slider, box, unit.
        private static readonly double[] columnWidths = { 88.0, 1.0, 0.0, 20.0 };
        private static readonly GridUnitType[] columnUnits = { GridUnitType.Pixel, GridUnitType.Star, GridUnitType.Auto, GridUnitType.Pixel };

        private sealed class Changes
        {
            public List<(double Value, bool Typed)> All { get; } = new();
        }

        private static (ParamRow, Changes) Row(ParamSpec _spec, double _value)
        {
            var row = new ParamRow(_spec);
            row.SetValue(_value);
            var changes = new Changes();
            row.ValueChanged += (_v, _typed) => changes.All.Add((_v, _typed));
            return (row, changes);
        }

        private static void Type(ParamRow _row, string _text)
        {
            _row.Input.Text = _text;
            _row.CommitInput();
        }

        private static KeyEventArgs Key(Control _source, Key _key, KeyModifiers _modifiers = KeyModifiers.None)
        {
            return new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = _key, KeyModifiers = _modifiers, Source = _source };
        }

        [AvaloniaFact]
        public void GT26_New_LabelSliderBoxAndUnit_InFourColumns()
        {
            var row = new ParamRow(px);

            Assert.Same(px, row.Spec);
            Assert.Equal(36, row.Height);
            Assert.Equal(10, row.ColumnSpacing);
            Assert.Equal(columnWidths, row.ColumnDefinitions.Select(_c => _c.Width.Value));
            Assert.Equal(columnUnits, row.ColumnDefinitions.Select(_c => _c.Width.GridUnitType));
            Assert.Equal("ブロック", row.Label.Text);
            Assert.Equal(13, row.Label.FontSize);
            Assert.Null(row.Badge);
            Assert.Equal(0, Grid.GetColumn((Control)row.Label.Parent!));
            Assert.Equal(1, Grid.GetColumn(row.Slider));
            Assert.Equal(2, Grid.GetColumn(row.Input));
            Assert.Equal(3, Grid.GetColumn(row.Unit));
            Assert.Equal(0, row.Slider.Minimum);
            Assert.Equal(ParamRow.SliderSteps, row.Slider.Maximum);
            Assert.Equal(1000, ParamRow.SliderSteps);
            Assert.Equal(56, row.Input.Width);
            Assert.Equal(28, row.Input.Height);
            Assert.Equal(TextAlignment.Right, row.Input.TextAlignment);
            Assert.Same(UiFonts.Mono, row.Input.FontFamily);
            Assert.Equal("px", row.Unit.Text);
            Assert.Equal(0, row.Value); // nothing shown until SetValue
        }

        [AvaloniaFact]
        public void GT34_New_WithBadgeAndWidth_ShowsTheBadgeAfterTheLabel()
        {
            var row = new ParamRow(px, "Linux", 64);

            Assert.NotNull(row.Badge);
            var label = (StackPanel)row.Label.Parent!;
            Assert.Equal(new Control[] { row.Label, row.Badge! }, label.Children);
            var text = (TextBlock)row.Badge!.Child!;
            Assert.Equal("Linux", text.Text);
            Assert.Equal(10, text.FontSize);
            Assert.Equal(Color.Parse("#E4E8F0"), ((ISolidColorBrush)row.Badge.Background!).Color);
            Assert.Equal(64, row.Input.Width);
        }

        [AvaloniaFact]
        public void GT79_New_SliderAndBox_AreNamedAfterTheLabel()
        {
            var row = new ParamRow(percent);

            Assert.Equal("透明度", Avalonia.Automation.AutomationProperties.GetName(row.Slider));
            Assert.Equal("透明度", Avalonia.Automation.AutomationProperties.GetName(row.Input));
            Assert.Equal("%", row.Unit.Text);
        }

        [AvaloniaTheory]
        [InlineData(12, 12, "12")]
        [InlineData(1, 2, "2")]          // below Min
        [InlineData(1000, 256, "256")]   // above Max
        [InlineData(100, 100, "100")]    // beyond the slider, inside the box range
        [InlineData(double.NaN, 12, "12")] // not a number: the default
        public void GT29_SetValue_ClampsAndShows_WithoutRaisingValueChanged(double _value, double _stored, string _text)
        {
            var (row, changes) = Row(px, 20);

            row.SetValue(_value);

            Assert.Equal(_stored, row.Value);
            Assert.Equal(_text, row.Input.Text);
            Assert.Equal(px.ToSliderPosition(_stored) * ParamRow.SliderSteps, row.Slider.Value, 9);
            Assert.Empty(changes.All);
        }

        [AvaloniaFact]
        public void GT116_SetValue_Percent_ShowsWholePercent()
        {
            var (row, _) = Row(percent, 0.456);

            Assert.Equal(0.456, row.Value);
            Assert.Equal("46", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT27_Slider_Move_RaisesRoundedValue_NotTyped()
        {
            var (row, changes) = Row(px, 12);

            row.Slider.Value = 500; // 2 + 62 * 0.5 = 33

            Assert.Equal(new[] { (33.0, false) }, changes.All);
            Assert.Equal(33, row.Value);
            Assert.Equal("33", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT27_Slider_MoveThatRoundsToTheSameValue_RaisesNothing()
        {
            var (row, changes) = Row(px, 33);

            row.Slider.Value = 501; // still 33 after rounding

            Assert.Empty(changes.All);
            Assert.Equal(33, row.Value);
        }

        [AvaloniaFact]
        public void GT27_Slider_OtherPropertyChanges_AreIgnored()
        {
            var (row, changes) = Row(px, 12);

            row.Slider.Maximum = 2000;
            row.Slider.IsEnabled = false;

            Assert.Empty(changes.All);
            Assert.Equal(12, row.Value);
        }

        [AvaloniaTheory]
        [InlineData("40", 40)]
        [InlineData(" 40 ", 40)]
        [InlineData("12.6", 13)]   // whole displayed units
        [InlineData("１００", 100)] // full-width digits from an IME
        [InlineData("1e2", 100)]
        [InlineData("999", 256)]   // clamped to Max
        [InlineData("-3", 2)]      // clamped to Min
        public void GT117_CommitInput_RaisesTypedValue(string _text, double _value)
        {
            var (row, changes) = Row(px, 12);

            Type(row, _text);

            Assert.Equal(new[] { (_value, true) }, changes.All);
            Assert.Equal(_value, row.Value);
            Assert.Equal(_value.ToString(System.Globalization.CultureInfo.InvariantCulture), row.Input.Text);
        }

        [AvaloniaTheory]
        [InlineData("")]
        [InlineData("abc")]
        [InlineData("1,5")]
        [InlineData(null)]
        public void GT117_CommitInput_NotANumber_RestoresTheText(string? _text)
        {
            var (row, changes) = Row(px, 12);

            Type(row, _text!);

            Assert.Empty(changes.All);
            Assert.Equal(12, row.Value);
            Assert.Equal("12", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT117_CommitInput_SameValue_RaisesNothing_ButNormalisesTheText()
        {
            var (row, changes) = Row(px, 12);

            Type(row, "12.2");

            Assert.Empty(changes.All);
            Assert.Equal("12", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT116_CommitInput_Percent_StoresAFraction()
        {
            var (row, changes) = Row(percent, 1);

            Type(row, "50");
            Type(row, "5"); // below 10 %

            Assert.Equal(new[] { (0.5, true), (0.1, true) }, changes.All);
            Assert.Equal("10", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT117_EnterInTheBox_Commits_AndIsHandled()
        {
            var (row, changes) = Row(px, 12);
            row.Input.Text = "30";
            var enter = Key(row.Input, Avalonia.Input.Key.Enter);

            row.Input.RaiseEvent(enter);

            Assert.True(enter.Handled);
            Assert.Equal(new[] { (30.0, true) }, changes.All);
        }

        [AvaloniaFact]
        public void GT117_OtherKeysInTheBox_DoNotCommit()
        {
            var (row, changes) = Row(px, 12);
            row.Input.Text = "30";
            var key = Key(row.Input, Avalonia.Input.Key.Escape);

            row.Input.RaiseEvent(key);

            Assert.Empty(changes.All);
            Assert.Equal(12, row.Value);
            Assert.Equal("30", row.Input.Text);
        }

        [AvaloniaFact]
        public void GT117_LeavingTheBox_Commits()
        {
            var (row, changes) = Row(px, 12);
            var other = new Button { Content = "x" };
            var window = new Window { Content = new StackPanel { Children = { row, other } } };
            window.Show();
            Assert.True(row.Input.Focus());
            row.Input.Text = "44";

            Assert.True(other.Focus());

            Assert.Equal(new[] { (44.0, true) }, changes.All);
            window.Close();
        }

        [AvaloniaTheory]
        [InlineData(Avalonia.Input.Key.Right, KeyModifiers.None, 1, false)]
        [InlineData(Avalonia.Input.Key.Up, KeyModifiers.None, 1, false)]
        [InlineData(Avalonia.Input.Key.Left, KeyModifiers.None, -1, false)]
        [InlineData(Avalonia.Input.Key.Down, KeyModifiers.None, -1, false)]
        [InlineData(Avalonia.Input.Key.PageUp, KeyModifiers.None, 1, true)]
        [InlineData(Avalonia.Input.Key.PageDown, KeyModifiers.None, -1, true)]
        [InlineData(Avalonia.Input.Key.Right, KeyModifiers.Shift, 1, true)]
        [InlineData(Avalonia.Input.Key.Left, KeyModifiers.Shift, -1, true)]
        [InlineData(Avalonia.Input.Key.PageUp, KeyModifiers.Shift, 1, true)]
        public void GT118_SliderKeys_StepLikeStep_AndAreHandled(Key _key, KeyModifiers _modifiers, int _direction, bool _large)
        {
            var (row, changes) = Row(px, 30);
            var (expected, _) = Row(px, 30);
            expected.Step(_direction, _large);
            var key = Key(row.Slider, _key, _modifiers);

            row.Slider.RaiseEvent(key);

            Assert.True(key.Handled);
            Assert.Equal(expected.Value, row.Value);
            Assert.NotEqual(30, row.Value);
            Assert.Equal(new[] { (expected.Value, false) }, changes.All);
        }

        [AvaloniaTheory]
        [InlineData(Avalonia.Input.Key.Home, 2.0)]
        [InlineData(Avalonia.Input.Key.End, 64.0)]
        public void GT118_SliderKeys_HomeAndEnd_AreLeftToTheSlider_WhichJumpsToItsEnds(Key _key, double _value)
        {
            var (row, changes) = Row(px, 30);

            row.Slider.RaiseEvent(Key(row.Slider, _key));

            Assert.Equal(new[] { (_value, false) }, changes.All);
        }

        [AvaloniaTheory]
        [InlineData(Avalonia.Input.Key.A)]
        [InlineData(Avalonia.Input.Key.Enter)]
        [InlineData(Avalonia.Input.Key.Tab)]
        public void GT118_SliderKeys_OthersMoveNothing(Key _key)
        {
            var (row, changes) = Row(px, 30);
            var key = Key(row.Slider, _key);

            row.Slider.RaiseEvent(key);

            Assert.Empty(changes.All);
            Assert.Equal(30, row.Value);
        }

        [AvaloniaFact]
        public void GT118_Step_MovesOnePercentOfTheTrack_OrTenPercentWhenLarge()
        {
            var (row, changes) = Row(new ParamSpec("x", "param.block", ParamUnit.Px, 0, 1000, 0, 1000, 500), 500);

            row.Step(1, false);
            Assert.Equal(510, row.Value);
            row.Step(1, true);
            Assert.Equal(610, row.Value);
            row.Step(-1, true);
            Assert.Equal(510, row.Value);
            row.Step(-1, false);
            Assert.Equal(500, row.Value);
            Assert.All(changes.All, _c => Assert.False(_c.Typed));
            Assert.Equal(4, changes.All.Count);
        }

        [AvaloniaFact]
        public void GT118_Step_LessThanADisplayedUnit_MovesOneUnit()
        {
            var (row, _) = Row(small, 4);

            row.Step(1, false);   // 1 % of 20 px = 0.2 px: rounds back to 4
            Assert.Equal(5, row.Value);
            row.Step(-1, false);
            Assert.Equal(4, row.Value);
            row.Step(1, true);    // 10 % = 2 px
            Assert.Equal(6, row.Value);
        }

        [AvaloniaFact]
        public void GT116_Step_Percent_MovesOnePercentAtLeast()
        {
            var (row, _) = Row(percent, 0.5);

            row.Step(1, false);  // 1 % of a 90 % track = 0.9 %: rounds to 51 %
            Assert.Equal(0.51, row.Value, 9);
            row.Step(-1, false);
            Assert.Equal(0.50, row.Value, 9);
        }

        [AvaloniaFact]
        public void GT118_Step_AtTheTracksEnds_StaysThere_AndRaisesNothing()
        {
            var (row, changes) = Row(px, 64);

            row.Step(1, false);
            row.Step(1, true);
            Assert.Equal(64, row.Value);

            row.SetValue(2);
            row.Step(-1, false);
            row.Step(-1, true);
            Assert.Equal(2, row.Value);
            Assert.Empty(changes.All);
        }

        [AvaloniaTheory]
        [InlineData(5)]    // typed below the slider
        [InlineData(80)]   // typed above the slider
        public void GT28_Step_TypedValueBeyondTheTrack_IsKept(double _value)
        {
            // The slider could not have produced it either: keys do not pull it back.
            var (row, changes) = Row(wide, 20);
            Type(row, _value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            changes.All.Clear();

            row.Step(1, false);
            row.Step(-1, true);

            Assert.Equal(_value, row.Value);
            Assert.Empty(changes.All);
        }

        [AvaloniaFact]
        public void GT118_Step_AtTheTracksEdgeValue_StillMovesInwards()
        {
            var (row, _) = Row(wide, 10);

            row.Step(-1, false);
            Assert.Equal(10, row.Value);
            row.Step(1, false);
            Assert.Equal(11, row.Value);
            row.SetValue(50);
            row.Step(-1, false);
            Assert.Equal(49, row.Value);
        }

        [AvaloniaFact]
        public void GT27_Changes_WithoutSubscribers_DoNotThrow()
        {
            var row = new ParamRow(px);
            row.SetValue(12);

            row.Slider.Value = 900;
            Type(row, "50");
            row.Step(1, false);

            Assert.Equal(51, row.Value);
        }
    }
}
