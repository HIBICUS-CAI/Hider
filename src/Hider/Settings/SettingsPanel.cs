using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Hider.Areas;
using Hider.Core;
using Hider.Core.Model;
using Hider.Core.Mosaic;

namespace Hider.Settings
{
    /// <summary>
    /// The settings popup of an area (FR-35 .. FR-37, design canvas 4a / 4b / 4c). Numeric rows are generated from the
    /// method's <see cref="ParamSpec"/>s and the kind's common specs, so a new method needs no UI code. Every change is applied
    /// to the area at once (FR-36): drawing values redraw without capturing again (D-10).
    /// </summary>
    public sealed class SettingsPanel : Border
    {
        /// <summary>Fill presets (design canvas 4a).</summary>
        public static readonly IReadOnlyList<string> Presets = new[]
        {
            "#68768A", "#2B2F36", "#4B4F57", "#8A8F99", "#C8CED8", "#3E5A7A", "#5B4B6E", "#6E5B3E",
        };

        private readonly AreaController area;
        private readonly StackPanel content = new() { Spacing = 14 };
        private readonly Random random = new();

        public SettingsPanel(AreaController _area)
        {
            area = _area;
            Width = 360;
            Background = new SolidColorBrush(Color.Parse("#F9F9F9"));
            BorderBrush = new SolidColorBrush(Color.Parse("#D6D6D6"));
            BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(16);
            BoxShadow = BoxShadows.Parse("0 8 24 0 #40000000");
            Child = content;
            Rebuild();
        }

        /// <summary>The pattern the rows were built for.</summary>
        public PatternKind Pattern { get; private set; }

        public IReadOnlyList<ParamRow> ParamRows { get; private set; } = Array.Empty<ParamRow>();
        public ComboBox? MethodBox { get; private set; }
        public Button? ColorButton { get; private set; }
        public IReadOnlyList<Button> PresetButtons { get; private set; } = Array.Empty<Button>();
        public TextBox? SeedBox { get; private set; }
        public Button? ReseedButton { get; private set; }
        public ColorView? ColorView { get; private set; }

        /// <summary>Row labels top to bottom (GT-26).</summary>
        public IReadOnlyList<string> RowLabels { get; private set; } = Array.Empty<string>();

        /// <summary>Builds the rows for the area's current pattern and method.</summary>
        public void Rebuild()
        {
            content.Children.Clear();
            var labels = new List<string>();
            var rows = new List<ParamRow>();
            var model = area.Model;
            Pattern = model.Pattern;
            MethodBox = null;
            ColorButton = null;
            PresetButtons = Array.Empty<Button>();
            SeedBox = null;
            ReseedButton = null;

            // Opacity: alpha for fill / generated (10..100 %), fineness for mosaic (0..100 %), spec §4.5.
            double min = model.Pattern == PatternKind.Mosaic ? 0.0 : Area.MinAlphaOpacity;
            var opacity = new ParamRow(new ParamSpec("opacity", "settings.opacity", ParamUnit.Percent, min, 1, min, 1, 1));
            opacity.SetValue(model.EffectiveOpacity);
            opacity.ValueChanged += (_v, _typed) =>
            {
                model.Opacity = _v;
                Changed(_typed);
            };
            Add(opacity, labels, rows);
            content.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Color.Parse("#E1E1E1")) });

            switch (model.Pattern)
            {
                case PatternKind.Fill:
                    content.Children.Add(Header(Strings.SettingsFillSection));
                    AddColorRow(Strings.SettingsColor, () => model.Fill.Color, _c => model.Fill.Color = _c, labels);
                    AddPresets(labels);
                    break;

                case PatternKind.Mosaic:
                {
                    content.Children.Add(Header(Strings.SettingsMosaicSection));
                    var method = MethodRegistry.Find(model.Mosaic.Method) ?? MethodRegistry.Find(AreaDefaults.MosaicMethod)!;
                    AddMethodBox(MethodKind.Mosaic, method, _id => model.Mosaic.Method = _id, labels);
                    var values = model.Mosaic.ParamsFor(method.Id);
                    foreach (var spec in method.Params)
                    {
                        var row = new ParamRow(spec);
                        row.SetValue(values.TryGetValue(spec.Key, out double v) ? v : spec.Default);
                        row.ValueChanged += (_value, _typed) =>
                        {
                            values[spec.Key] = _value;
                            Changed(_typed);
                        };
                        Add(row, labels, rows);
                    }
                    foreach (var spec in CommonParams.Mosaic)
                    {
                        bool linuxOnly = spec.Key == CommonParams.StripPx;
                        var row = new ParamRow(spec, linuxOnly ? Strings.LinuxBadge : null, spec.Key == CommonParams.IntervalMs ? 64 : 56);
                        row.SetValue(GetCommon(model.Mosaic.Common, spec.Key));
                        row.ValueChanged += (_value, _typed) =>
                        {
                            SetCommon(model.Mosaic.Common, spec.Key, _value);
                            Changed(_typed);
                        };
                        Add(row, labels, rows);
                    }
                    break;
                }

                default:
                {
                    content.Children.Add(Header(Strings.SettingsGeneratedSection));
                    var method = MethodRegistry.Find(model.Generated.Method) ?? MethodRegistry.Find(AreaDefaults.GeneratedMethod)!;
                    AddMethodBox(MethodKind.Generated, method, _id => model.Generated.Method = _id, labels);
                    AddColorRow(Strings.SettingsBaseColor, () => model.Generated.Common.BaseColor, _c => model.Generated.Common.BaseColor = _c, labels);
                    var values = model.Generated.ParamsFor(method.Id);
                    foreach (var spec in method.Params)
                    {
                        var row = new ParamRow(spec);
                        row.SetValue(values.TryGetValue(spec.Key, out double v) ? v : spec.Default);
                        row.ValueChanged += (_value, _typed) =>
                        {
                            values[spec.Key] = _value;
                            Changed(_typed);
                        };
                        Add(row, labels, rows);
                    }
                    AddSeedRow(labels);
                    break;
                }
            }
            ParamRows = rows;
            RowLabels = labels;
        }

        private void Changed(bool _saveNow)
        {
            area.SettingsChanged();
            area.SaveSettings(_saveNow);
        }

        private void Add(ParamRow _row, List<string> _labels, List<ParamRow> _rows)
        {
            content.Children.Add(_row);
            _labels.Add(_row.Label.Text ?? "");
            _rows.Add(_row);
        }

        private void AddMethodBox(MethodKind _kind, IMosaicMethod _current, Action<string> _set, List<string> _labels)
        {
            var methods = MethodRegistry.Filter(MethodRegistry.OfKind(_kind), MethodRegistry.CurrentPlatform);
            var box = new ComboBox
            {
                ItemsSource = methods.Select(_m => Strings.Get(_m.LabelKey)).ToArray(),
                SelectedIndex = Math.Max(0, methods.ToList().FindIndex(_m => _m.Id == _current.Id)),
                HorizontalAlignment = HorizontalAlignment.Stretch, Height = 30, MinHeight = 30, FontSize = 13,
            };
            Avalonia.Automation.AutomationProperties.SetName(box, Strings.SettingsMethod);
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex >= 0 && methods[box.SelectedIndex].Id != _current.Id)
                {
                    // Other methods keep their values (FR-35); the rows change to the new method's parameters.
                    _set(methods[box.SelectedIndex].Id);
                    Changed(true);
                    Rebuild();
                }
            };
            MethodBox = box;
            content.Children.Add(LabeledRow(Strings.SettingsMethod, box));
            _labels.Add(Strings.SettingsMethod);
        }

        private void AddColorRow(string _label, Func<string> _get, Action<string> _set, List<string> _labels)
        {
            var swatch = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(3), BorderBrush = new SolidColorBrush(Color.Parse("#26000000")), BorderThickness = new Thickness(1) };
            var hex = new TextBlock { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, FontFamily = UiFonts.Mono };
            void Show()
            {
                string color = _get();
                swatch.Background = new SolidColorBrush(Color.Parse(color));
                hex.Text = color;
                if (ColorButton is not null)
                {
                    Avalonia.Automation.AutomationProperties.SetName(ColorButton, Strings.SettingsPickColorWithValue(color));
                }
            }
            var view = new ColorView { Color = Color.Parse(_get()), IsAlphaEnabled = false, IsAlphaVisible = false, Width = 300 };
            view.ColorChanged += (_, _e) =>
            {
                string color = $"#{_e.NewColor.R:X2}{_e.NewColor.G:X2}{_e.NewColor.B:X2}";
                if (color != _get())
                {
                    _set(color);
                    Show();
                    Changed(false);
                    HighlightPreset();
                }
            };
            var button = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, hex } },
                Height = 30, MinHeight = 30, Padding = new Thickness(8, 0), Background = Brushes.White,
                Flyout = new Flyout { Content = view, Placement = PlacementMode.BottomEdgeAlignedLeft },
            };
            ColorButton = button;
            ColorView = view;
            Show();
            content.Children.Add(LabeledRow(_label, button));
            _labels.Add(_label);
        }

        private void AddPresets(List<string> _labels)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            var buttons = new List<Button>();
            foreach (string preset in Presets)
            {
                var button = new Button
                {
                    Width = 22, Height = 22, MinHeight = 22, MinWidth = 22, Padding = new Thickness(0), Tag = preset,
                    Background = new SolidColorBrush(Color.Parse(preset)), CornerRadius = new CornerRadius(4),
                };
                Avalonia.Automation.AutomationProperties.SetName(button, preset);
                button.Click += (_, _) =>
                {
                    area.Model.Fill.Color = preset;
                    Changed(true);
                    Rebuild();
                };
                buttons.Add(button);
                panel.Children.Add(button);
            }
            PresetButtons = buttons;
            HighlightPreset();
            content.Children.Add(LabeledRow(Strings.SettingsPresets, panel));
            _labels.Add(Strings.SettingsPresets);
        }

        private void HighlightPreset()
        {
            foreach (var button in PresetButtons)
            {
                bool selected = string.Equals((string)button.Tag!, area.Model.Fill.Color, StringComparison.OrdinalIgnoreCase);
                button.BorderBrush = new SolidColorBrush(Color.Parse(selected ? "#4C9AFF" : "#26000000"));
                button.BorderThickness = new Thickness(selected ? 2 : 1);
            }
        }

        private void AddSeedRow(List<string> _labels)
        {
            var common = area.Model.Generated.Common;
            var box = new TextBox
            {
                Text = common.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 110, Height = 30, MinHeight = 30, FontSize = 12,
                VerticalContentAlignment = VerticalAlignment.Center, FontFamily = UiFonts.Mono,
            };
            Avalonia.Automation.AutomationProperties.SetName(box, Strings.SettingsSeed);
            void CommitSeed()
            {
                // Full-width digits from an IME are accepted; values beyond the hash's range are clamped to it, so every shown
                // seed is its own pattern.
                if (long.TryParse(NumberText.Normalize(box.Text), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long typed))
                {
                    int seed = (int)Math.Clamp(typed, 0, AreaDefaults.SeedCount - 1);
                    if (seed != common.Seed)
                    {
                        common.Seed = seed;
                        Changed(true);
                    }
                }
                box.Text = common.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            box.LostFocus += (_, _) => CommitSeed();
            box.KeyDown += (_, _e) =>
            {
                if (_e.Key == Avalonia.Input.Key.Enter)
                {
                    CommitSeed();
                    _e.Handled = true;
                }
            };
            var reseed = new Button
            {
                Width = 30, Height = 30, MinHeight = 30, Padding = new Thickness(0), Background = Brushes.White,
                Content = new Avalonia.Controls.Shapes.Path
                {
                    Width = 16, Height = 16, Stretch = Stretch.None, Stroke = new SolidColorBrush(Color.Parse("#1B1B1B")), StrokeThickness = 1.4,
                    Data = Geometry.Parse("M4.5 2.5h7a2 2 0 0 1 2 2v7a2 2 0 0 1-2 2h-7a2 2 0 0 1-2-2v-7a2 2 0 0 1 2-2zM5.5 5.5h.01M8 8h.01M10.5 10.5h.01"),
                },
            };
            ToolTip.SetTip(reseed, Strings.SettingsReseed);
            Avalonia.Automation.AutomationProperties.SetName(reseed, Strings.SettingsReseed);
            reseed.Click += (_, _) =>
            {
                common.Seed = random.Next(0, AreaDefaults.SeedCount);
                box.Text = common.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture);
                Changed(true);
            };
            SeedBox = box;
            ReseedButton = reseed;
            content.Children.Add(LabeledRow(Strings.SettingsSeed, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { box, reseed } }));
            _labels.Add(Strings.SettingsSeed);
        }

        private static Grid LabeledRow(string _label, Control _control)
        {
            var grid = new Grid { Height = 36, ColumnDefinitions = new ColumnDefinitions("88,*"), ColumnSpacing = 10 };
            var label = new TextBlock { Text = _label, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
            _control.VerticalAlignment = VerticalAlignment.Center;
            if (_control is not ComboBox)
            {
                _control.HorizontalAlignment = HorizontalAlignment.Left;
            }
            Grid.SetColumn(_control, 1);
            grid.Children.Add(label);
            grid.Children.Add(_control);
            return grid;
        }

        private static TextBlock Header(string _text)
        {
            return new TextBlock { Text = _text, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = new SolidColorBrush(Color.Parse("#666666")), Height = 18 };
        }

        private static double GetCommon(MosaicCommon _common, string _key)
        {
            return _key switch
            {
                CommonParams.Blur => _common.Blur,
                CommonParams.Levels => _common.Levels,
                CommonParams.IntervalMs => _common.IntervalMs,
                CommonParams.StripPx => _common.StripPx,
                _ => 0,
            };
        }

        private static void SetCommon(MosaicCommon _common, string _key, double _value)
        {
            switch (_key)
            {
                case CommonParams.Blur: _common.Blur = _value; break;
                case CommonParams.Levels: _common.Levels = (int)Math.Round(_value); break;
                case CommonParams.IntervalMs: _common.IntervalMs = (int)Math.Round(_value); break;
                case CommonParams.StripPx: _common.StripPx = (int)Math.Round(_value); break;
            }
        }
    }
}
