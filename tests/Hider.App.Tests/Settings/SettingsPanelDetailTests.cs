using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Hider.Areas;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Settings;

namespace Hider.App.Tests.Settings
{
    /// <summary>Every row, control and branch of the settings popup (the GT-26 .. GT-33 tests cover the design-level behaviour).</summary>
    public class SettingsPanelDetailTests
    {
        private static (AreaFixture, AreaController) Area(PatternKind _pattern)
        {
            var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 200, 400, 240), _pattern);
            return (f, area);
        }

        private static ParamRow Row(SettingsPanel _panel, string _key) => _panel.ParamRows.Single(_r => _r.Spec.Key == _key);

        private static void Type(ParamRow _row, string _text)
        {
            _row.Input.Text = _text;
            _row.CommitInput();
        }

        private static TextBlock Hex(SettingsPanel _panel) => ((StackPanel)_panel.ColorButton!.Content!).Children.OfType<TextBlock>().Single();

        private static Border Swatch(SettingsPanel _panel) => ((StackPanel)_panel.ColorButton!.Content!).Children.OfType<Border>().Single();

        private static void Enter(TextBox _box)
        {
            _box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = _box });
        }

        [AvaloniaFact]
        public void Panel_FollowsTheDesign()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                var panel = new SettingsPanel(area);

                Assert.Equal(360, panel.Width);
                Assert.Equal(new Thickness(16), panel.Padding);
                Assert.Equal(new CornerRadius(8), panel.CornerRadius);
                Assert.Equal(new Thickness(1), panel.BorderThickness);
                Assert.Equal(Color.Parse("#F9F9F9"), ((ISolidColorBrush)panel.Background!).Color);
                Assert.Equal(Color.Parse("#D6D6D6"), ((ISolidColorBrush)panel.BorderBrush!).Color);
                Assert.Equal(BoxShadows.Parse("0 8 24 0 #40000000"), panel.BoxShadow);
                Assert.Equal(14, ((StackPanel)panel.Child!).Spacing);
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill, "settings.fillSection")]
        [InlineData(PatternKind.Mosaic, "settings.mosaicSection")]
        [InlineData(PatternKind.Generated, "settings.generatedSection")]
        public void Rebuild_SectionHeaderAndPatternFollowTheArea(PatternKind _pattern, string _headerKey)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                var panel = new SettingsPanel(area);

                Assert.Equal(_pattern, panel.Pattern);
                var header = ((StackPanel)panel.Child!).Children.OfType<TextBlock>().Single();
                Assert.Equal(Hider.Core.Strings.Get(_headerKey), header.Text);
                Assert.Equal(12, header.FontSize);
                Assert.Equal(FontWeight.Medium, header.FontWeight);
            }
        }

        [AvaloniaFact]
        public void Rebuild_ControlsExistOnlyForTheirPattern()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                var panel = new SettingsPanel(area);
                Assert.NotNull(panel.ColorButton);
                Assert.NotNull(panel.ColorView);
                Assert.Equal(SettingsPanel.Presets.Count, panel.PresetButtons.Count);
                Assert.Null(panel.MethodBox);
                Assert.Null(panel.SeedBox);
                Assert.Null(panel.ReseedButton);

                area.Model.Pattern = PatternKind.Mosaic;
                panel.Rebuild();
                Assert.Equal(PatternKind.Mosaic, panel.Pattern);
                Assert.NotNull(panel.MethodBox);
                Assert.Null(panel.ColorButton);
                Assert.Empty(panel.PresetButtons);
                Assert.Null(panel.SeedBox);
                Assert.Null(panel.ReseedButton);

                area.Model.Pattern = PatternKind.Generated;
                panel.Rebuild();
                Assert.NotNull(panel.MethodBox);
                Assert.NotNull(panel.ColorButton);
                Assert.Empty(panel.PresetButtons);
                Assert.NotNull(panel.SeedBox);
                Assert.NotNull(panel.ReseedButton);

                area.Model.Pattern = PatternKind.Fill;
                panel.Rebuild();
                Assert.Null(panel.MethodBox);
                Assert.Null(panel.SeedBox);
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill, "0", 0.1)]
        [InlineData(PatternKind.Generated, "5", 0.1)]
        [InlineData(PatternKind.Mosaic, "0", 0.0)]
        [InlineData(PatternKind.Mosaic, "35", 0.35)]
        public void Opacity_RangeDependsOnThePattern(PatternKind _pattern, string _typed, double _stored)
        {
            // Spec §4.5: alpha for fill / generated (10..100 %), fineness for mosaic (0..100 %).
            var (f, area) = Area(_pattern);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var opacity = Row(panel, "opacity");
                Assert.Equal(_pattern == PatternKind.Mosaic ? 0.0 : 0.1, opacity.Spec.Min);
                Assert.Equal(1.0, opacity.Spec.Max);

                Type(opacity, _typed);

                Assert.Equal(_stored, area.Model.Opacity, 9);
            }
        }

        [AvaloniaFact]
        public void Opacity_ShowsTheEffectiveOpacity()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                area.Model.Opacity = 0.0; // stored for a mosaic, shown as 10 % for a fill

                var panel = new SettingsPanel(area);

                Assert.Equal("10", Row(panel, "opacity").Input.Text);
            }
        }

        [AvaloniaFact]
        public void SlidingAValue_SavesLater_TypingSavesNow()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var block = Row(panel, "block");
                int saves = f.Manager.SaveCount;

                block.Slider.Value = 800;
                Assert.Equal(saves, f.Manager.SaveCount);
                f.Time.Advance(TimeSpan.FromMilliseconds(500));
                Assert.Equal(saves + 1, f.Manager.SaveCount);

                Type(block, "30");
                Assert.Equal(saves + 2, f.Manager.SaveCount);
            }
        }

        [AvaloniaTheory]
        [InlineData(CommonParams.Blur, "7", 7.0)]
        [InlineData(CommonParams.Levels, "16", 16.0)]
        [InlineData(CommonParams.IntervalMs, "250", 250.0)]
        [InlineData(CommonParams.StripPx, "48", 48.0)]
        public void MosaicCommonRows_WriteTheirOwnValue(string _key, string _typed, double _value)
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var common = area.Model.Mosaic.Common;
                var before = (common.Blur, common.Levels, common.IntervalMs, common.StripPx);
                var panel = new SettingsPanel(area);

                Type(Row(panel, _key), _typed);

                var after = (common.Blur, common.Levels, common.IntervalMs, common.StripPx);
                Assert.Equal(_key == CommonParams.Blur ? _value : before.Blur, after.Blur);
                Assert.Equal(_key == CommonParams.Levels ? (int)_value : before.Levels, after.Levels);
                Assert.Equal(_key == CommonParams.IntervalMs ? (int)_value : before.IntervalMs, after.IntervalMs);
                Assert.Equal(_key == CommonParams.StripPx ? (int)_value : before.StripPx, after.StripPx);
            }
        }

        [AvaloniaFact]
        public void MosaicCommonRows_ShowTheStoredValues_AndTheIntervalBoxIsWider()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var common = area.Model.Mosaic.Common;
                common.Blur = 3;
                common.Levels = 12;
                common.IntervalMs = 1500;
                common.StripPx = 64;

                var panel = new SettingsPanel(area);

                Assert.Equal("3", Row(panel, CommonParams.Blur).Input.Text);
                Assert.Equal("12", Row(panel, CommonParams.Levels).Input.Text);
                Assert.Equal("1500", Row(panel, CommonParams.IntervalMs).Input.Text);
                Assert.Equal("64", Row(panel, CommonParams.StripPx).Input.Text);
                Assert.Equal(64, Row(panel, CommonParams.IntervalMs).Input.Width);
                Assert.All(panel.ParamRows.Where(_r => _r.Spec.Key != CommonParams.IntervalMs), _r => Assert.Equal(56, _r.Input.Width));
            }
        }

        [AvaloniaFact]
        public void MosaicCommonRows_RoundIntegerValues()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var levels = Row(panel, CommonParams.Levels);

                levels.Slider.Value = levels.Spec.ToSliderPosition(100.4) * ParamRow.SliderSteps;

                Assert.Equal(100, area.Model.Mosaic.Common.Levels);
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void MethodRows_ShowStoredValues_ElseTheDefault(PatternKind _pattern)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                bool mosaic = _pattern == PatternKind.Mosaic;
                string id = mosaic ? area.Model.Mosaic.Method : area.Model.Generated.Method;
                var method = MethodRegistry.Find(id)!;
                var values = mosaic ? area.Model.Mosaic.ParamsFor(id) : area.Model.Generated.ParamsFor(id);
                values.Clear();
                var first = method.Params[0];
                values[first.Key] = first.Default + 3;

                var panel = new SettingsPanel(area);

                Assert.Equal(first.Default + 3, Row(panel, first.Key).Value);
                foreach (var spec in method.Params.Skip(1))
                {
                    Assert.Equal(spec.Default, Row(panel, spec.Key).Value);
                }
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void MethodRows_NothingStored_ShowEveryDefault(PatternKind _pattern)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                bool mosaic = _pattern == PatternKind.Mosaic;
                string id = mosaic ? area.Model.Mosaic.Method : area.Model.Generated.Method;
                (mosaic ? area.Model.Mosaic.ParamsFor(id) : area.Model.Generated.ParamsFor(id)).Clear();

                var panel = new SettingsPanel(area);

                Assert.All(MethodRegistry.Find(id)!.Params, _p => Assert.Equal(_p.Default, Row(panel, _p.Key).Value));
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void MethodRows_WriteToTheCurrentMethodsValues(PatternKind _pattern)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                bool mosaic = _pattern == PatternKind.Mosaic;
                string id = mosaic ? area.Model.Mosaic.Method : area.Model.Generated.Method;
                var spec = MethodRegistry.Find(id)!.Params[0];
                var panel = new SettingsPanel(area);
                int saves = f.Manager.SaveCount;

                Type(Row(panel, spec.Key), "9");

                var values = mosaic ? area.Model.Mosaic.ParamsFor(id) : area.Model.Generated.ParamsFor(id);
                Assert.Equal(spec.Clamp(9), values[spec.Key]);
                Assert.Equal(saves + 1, f.Manager.SaveCount);
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic, "no-such-method")]
        [InlineData(PatternKind.Mosaic, AreaDefaults.GeneratedMethod)] // a method of the other kind
        [InlineData(PatternKind.Generated, "no-such-method")]
        [InlineData(PatternKind.Generated, AreaDefaults.MosaicMethod)]
        public void UnknownMethod_ShowsTheDefaultMethod(PatternKind _pattern, string _stored)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                bool mosaic = _pattern == PatternKind.Mosaic;
                if (mosaic)
                {
                    area.Model.Mosaic.Method = _stored;
                }
                else
                {
                    area.Model.Generated.Method = _stored;
                }

                var panel = new SettingsPanel(area);

                string expected = mosaic ? AreaDefaults.MosaicMethod : AreaDefaults.GeneratedMethod;
                var methods = MethodRegistry.Filter(MethodRegistry.OfKind(mosaic ? MethodKind.Mosaic : MethodKind.Generated), MethodRegistry.CurrentPlatform);
                Assert.Equal(methods.ToList().FindIndex(_m => _m.Id == expected), panel.MethodBox!.SelectedIndex);
                Assert.All(MethodRegistry.Find(expected)!.Params, _p => Assert.Contains(panel.ParamRows, _r => _r.Spec.Key == _p.Key));
                Assert.Equal(_stored, mosaic ? area.Model.Mosaic.Method : area.Model.Generated.Method); // shown, not rewritten
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void MethodBox_ListsThePlatformsMethods_StretchedAndNamed(PatternKind _pattern)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var kind = _pattern == PatternKind.Mosaic ? MethodKind.Mosaic : MethodKind.Generated;
                var methods = MethodRegistry.Filter(MethodRegistry.OfKind(kind), MethodRegistry.CurrentPlatform);

                var box = panel.MethodBox!;
                Assert.Equal(methods.Select(_m => Hider.Core.Strings.Get(_m.LabelKey)), box.ItemsSource!.Cast<string>());
                Assert.Equal(HorizontalAlignment.Stretch, box.HorizontalAlignment);
                Assert.Equal(VerticalAlignment.Center, box.VerticalAlignment);
                Assert.Equal("手法", Avalonia.Automation.AutomationProperties.GetName(box));
                Assert.Equal(1, Grid.GetColumn(box));
            }
        }

        [AvaloniaFact]
        public void MethodBox_SameMethodOrNoSelection_ChangesNothing()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var rows = panel.ParamRows;
                int saves = f.Manager.SaveCount;
                int index = panel.MethodBox!.SelectedIndex;

                panel.MethodBox.SelectedIndex = -1;
                panel.MethodBox.SelectedIndex = index;

                Assert.Same(rows, panel.ParamRows); // not rebuilt
                Assert.Equal(saves, f.Manager.SaveCount);
                Assert.Equal(AreaDefaults.MosaicMethod, area.Model.Mosaic.Method);
            }
        }

        [AvaloniaFact]
        public void MethodBox_OtherMethod_SavesAndRebuildsForIt()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var methods = MethodRegistry.Filter(MethodRegistry.OfKind(MethodKind.Mosaic), MethodRegistry.CurrentPlatform);
                int other = methods.ToList().FindIndex(_m => _m.Id != AreaDefaults.MosaicMethod);
                int saves = f.Manager.SaveCount;
                var rows = panel.ParamRows;

                panel.MethodBox!.SelectedIndex = other;

                Assert.Equal(methods[other].Id, area.Model.Mosaic.Method);
                Assert.Equal(saves + 1, f.Manager.SaveCount);
                Assert.NotSame(rows, panel.ParamRows);
                Assert.Equal(other, panel.MethodBox!.SelectedIndex);
            }
        }

        [AvaloniaTheory]
        [InlineData(PatternKind.Fill)]
        [InlineData(PatternKind.Generated)]
        public void ColorButton_ShowsTheColor_SwatchHexAndName(PatternKind _pattern)
        {
            var (f, area) = Area(_pattern);
            using (f)
            {
                if (_pattern == PatternKind.Fill)
                {
                    area.Model.Fill.Color = "#123456";
                }
                else
                {
                    area.Model.Generated.Common.BaseColor = "#123456";
                }

                var panel = new SettingsPanel(area);

                Assert.Equal("#123456", Hex(panel).Text);
                Assert.Same(UiFonts.Mono, Hex(panel).FontFamily);
                Assert.Equal(Color.Parse("#123456"), ((ISolidColorBrush)Swatch(panel).Background!).Color);
                Assert.Equal("色を選ぶ: #123456", Avalonia.Automation.AutomationProperties.GetName(panel.ColorButton!));
                Assert.Equal(Color.Parse("#123456"), panel.ColorView!.Color);
                Assert.False(panel.ColorView.IsAlphaEnabled);
                Assert.False(panel.ColorView.IsAlphaVisible);
                Assert.Same(panel.ColorView, ((Flyout)panel.ColorButton!.Flyout!).Content);
                Assert.Equal(PlacementMode.BottomEdgeAlignedLeft, ((Flyout)panel.ColorButton.Flyout!).Placement);
                Assert.Equal(HorizontalAlignment.Left, panel.ColorButton.HorizontalAlignment); // not stretched like the method box
            }
        }

        [AvaloniaFact]
        public void ColorPicked_Fill_AppliesAndSavesLater_AndHighlightsAMatchingPreset()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                var panel = new SettingsPanel(area);
                int saves = f.Manager.SaveCount;

                panel.ColorView!.Color = Color.Parse("#3e5a7a");

                Assert.Equal("#3E5A7A", area.Model.Fill.Color);
                Assert.Equal("#3E5A7A", Hex(panel).Text);
                Assert.Equal(Color.Parse("#3E5A7A"), ((ISolidColorBrush)Swatch(panel).Background!).Color);
                Assert.Equal("色を選ぶ: #3E5A7A", Avalonia.Automation.AutomationProperties.GetName(panel.ColorButton!));
                Assert.Equal(new Thickness(2), panel.PresetButtons[5].BorderThickness);
                Assert.Equal(new Thickness(1), panel.PresetButtons[0].BorderThickness);
                Assert.Equal(saves, f.Manager.SaveCount); // dragging in the picker: saved once it settles
                f.Time.Advance(TimeSpan.FromMilliseconds(500));
                Assert.Equal(saves + 1, f.Manager.SaveCount);
            }
        }

        [AvaloniaFact]
        public void ColorPicked_SameHexOnlyAlphaDiffers_ChangesNothing()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                area.Model.Fill.Color = "#112233";
                var panel = new SettingsPanel(area);
                f.Time.Advance(TimeSpan.FromSeconds(1));
                int saves = f.Manager.SaveCount;

                panel.ColorView!.Color = Color.Parse("#80112233");
                f.Time.Advance(TimeSpan.FromSeconds(1));

                Assert.Equal("#112233", area.Model.Fill.Color);
                Assert.Equal(saves, f.Manager.SaveCount);
            }
        }

        [AvaloniaFact]
        public void ColorPicked_Generated_SetsTheBaseColor()
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                var panel = new SettingsPanel(area);
                string fill = area.Model.Fill.Color;

                panel.ColorView!.Color = Color.Parse("#0A0B0C");

                Assert.Equal("#0A0B0C", area.Model.Generated.Common.BaseColor);
                Assert.Equal(fill, area.Model.Fill.Color);
                Assert.Equal("#0A0B0C", Hex(panel).Text);
            }
        }

        [AvaloniaFact]
        public void Presets_OnlyTheCurrentColorIsHighlighted_IgnoringCase()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                area.Model.Fill.Color = "#c8ced8";

                var panel = new SettingsPanel(area);

                for (int i = 0; i < panel.PresetButtons.Count; i++)
                {
                    var button = panel.PresetButtons[i];
                    bool selected = i == 4;
                    Assert.Equal(new Thickness(selected ? 2 : 1), button.BorderThickness);
                    Assert.Equal(Color.Parse(selected ? "#4C9AFF" : "#26000000"), ((ISolidColorBrush)button.BorderBrush!).Color);
                }
            }
        }

        [AvaloniaFact]
        public void Presets_NoneMatches_NoneHighlighted()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                area.Model.Fill.Color = "#010203";

                var panel = new SettingsPanel(area);

                Assert.All(panel.PresetButtons, _b => Assert.Equal(new Thickness(1), _b.BorderThickness));
            }
        }

        [AvaloniaFact]
        public void Presets_AreSwatchButtonsNamedByColor()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                var panel = new SettingsPanel(area);

                for (int i = 0; i < SettingsPanel.Presets.Count; i++)
                {
                    var button = panel.PresetButtons[i];
                    Assert.Equal(SettingsPanel.Presets[i], button.Tag);
                    Assert.Equal(SettingsPanel.Presets[i], Avalonia.Automation.AutomationProperties.GetName(button));
                    Assert.Equal(Color.Parse(SettingsPanel.Presets[i]), ((ISolidColorBrush)button.Background!).Color);
                    Assert.Equal(22, button.Width);
                    Assert.Equal(22, button.Height);
                }
            }
        }

        [AvaloniaFact]
        public void Preset_Click_RebuildsWithTheNewHighlight()
        {
            var (f, area) = Area(PatternKind.Fill);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var before = panel.PresetButtons;

                before[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                Assert.Equal(SettingsPanel.Presets[2], area.Model.Fill.Color);
                Assert.NotSame(before, panel.PresetButtons);
                Assert.Equal(new Thickness(2), panel.PresetButtons[2].BorderThickness);
                Assert.Equal(SettingsPanel.Presets[2], Hex(panel).Text);
            }
        }

        [AvaloniaTheory]
        [InlineData("42", 42)]
        [InlineData("0", 0)]
        [InlineData("-5", 0)]
        [InlineData("65535", 65535)]
        [InlineData("99999999999", 65535)]
        [InlineData("０４２", 42)]
        public void Seed_Enter_StoresTheClampedSeed(string _text, int _seed)
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var box = panel.SeedBox!;
                box.Text = _text;
                int saves = f.Manager.SaveCount;

                Enter(box);

                Assert.Equal(_seed, area.Model.Generated.Common.Seed);
                Assert.Equal(_seed.ToString(System.Globalization.CultureInfo.InvariantCulture), box.Text);
                Assert.Equal(saves + 1, f.Manager.SaveCount);
            }
        }

        [AvaloniaTheory]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1.5")]
        [InlineData("1234")] // the current seed
        public void Seed_NotANumberOrUnchanged_RestoresTheTextWithoutSaving(string _text)
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                area.Model.Generated.Common.Seed = 1234;
                var panel = new SettingsPanel(area);
                var box = panel.SeedBox!;
                box.Text = _text;
                int saves = f.Manager.SaveCount;

                Enter(box);

                Assert.Equal(1234, area.Model.Generated.Common.Seed);
                Assert.Equal("1234", box.Text);
                Assert.Equal(saves, f.Manager.SaveCount);
            }
        }

        [AvaloniaFact]
        public void Seed_EnterIsHandled_OtherKeysAreNot()
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var box = panel.SeedBox!;
                box.Text = "77";
                var other = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape, Source = box };

                box.RaiseEvent(other);
                Assert.NotEqual(77, area.Model.Generated.Common.Seed);

                var enter = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = box };
                box.RaiseEvent(enter);
                Assert.True(enter.Handled);
                Assert.Equal(77, area.Model.Generated.Common.Seed);
            }
        }

        [AvaloniaFact]
        public void Seed_LeavingTheBox_Commits()
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var window = new Window { Content = panel };
                window.Show();
                var box = panel.SeedBox!;
                Assert.True(box.Focus());
                box.Text = "321";

                Assert.True(panel.ReseedButton!.Focus());

                Assert.Equal(321, area.Model.Generated.Common.Seed);
                window.Close();
            }
        }

        [AvaloniaFact]
        public void Seed_BoxShowsTheStoredSeed_AndIsNamed()
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                area.Model.Generated.Common.Seed = 4321;

                var panel = new SettingsPanel(area);

                Assert.Equal("4321", panel.SeedBox!.Text);
                Assert.Same(UiFonts.Mono, panel.SeedBox.FontFamily);
                Assert.Equal("シード", Avalonia.Automation.AutomationProperties.GetName(panel.SeedBox));
                Assert.Equal(Hider.Core.Strings.SettingsReseed, ToolTip.GetTip(panel.ReseedButton!));
                Assert.Equal(Hider.Core.Strings.SettingsReseed, Avalonia.Automation.AutomationProperties.GetName(panel.ReseedButton!));
            }
        }

        [AvaloniaFact]
        public void Reseed_PicksANewSeedInRange_ShowsAndSavesIt()
        {
            var (f, area) = Area(PatternKind.Generated);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var seen = new HashSet<int>();
                int saves = f.Manager.SaveCount;

                for (int i = 0; i < 20; i++)
                {
                    panel.ReseedButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    int seed = area.Model.Generated.Common.Seed;
                    Assert.InRange(seed, 0, AreaDefaults.SeedCount - 1);
                    Assert.Equal(seed.ToString(System.Globalization.CultureInfo.InvariantCulture), panel.SeedBox!.Text);
                    seen.Add(seed);
                }

                Assert.Equal(saves + 20, f.Manager.SaveCount);
                Assert.True(seen.Count > 1); // random: 20 draws of 65536 are not all the same
            }
        }

        [AvaloniaFact]
        public void Rebuild_ReplacesTheRows_AndTheLabels()
        {
            var (f, area) = Area(PatternKind.Mosaic);
            using (f)
            {
                var panel = new SettingsPanel(area);
                var rows = panel.ParamRows;
                var labels = panel.RowLabels;

                panel.Rebuild();

                Assert.NotSame(rows, panel.ParamRows);
                Assert.Equal(labels, panel.RowLabels);
                Assert.Equal(rows.Select(_r => _r.Spec.Key), panel.ParamRows.Select(_r => _r.Spec.Key));
                Assert.All(panel.ParamRows, _r => Assert.Contains(_r, ((StackPanel)panel.Child!).Children));
                Assert.All(rows, _r => Assert.DoesNotContain(_r, ((StackPanel)panel.Child!).Children));
            }
        }
    }
}
