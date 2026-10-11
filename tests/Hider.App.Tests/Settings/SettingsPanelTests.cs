using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Hider.Areas;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Settings;

namespace Hider.App.Tests.Settings
{
    public class SettingsPanelTests
    {
        private static (AreaFixture, AreaController, SettingsPanel) Open(PatternKind _pattern)
        {
            var f = new AreaFixture();
            var area = f.Add(new Hider.Core.Model.PhysicalRect(100, 200, 400, 240), _pattern);
            area.Window.ToggleSettings();
            return (f, area, area.Window.Settings!);
        }

        private static ParamRow Row(SettingsPanel _panel, string _key) => _panel.ParamRows.Single(r => r.Spec.Key == _key);

        private static void Type(ParamRow _row, string _text)
        {
            _row.Input.Text = _text;
            _row.CommitInput();
        }

        [AvaloniaFact]
        public void GT26_RowsMatchDesignCanvas()
        {
            var (f1, _, fill) = Open(PatternKind.Fill);
            using (f1)
            {
                Assert.Equal(new[] { "透明度", "色", "プリセット" }, fill.RowLabels);
            }
            var (f2, _, mosaic) = Open(PatternKind.Mosaic);
            using (f2)
            {
                Assert.Equal(new[] { "透明度", "手法", "ブロック", "ぼかし", "色数", "更新間隔", "帯の高さ" }, mosaic.RowLabels);
                Assert.Equal(new[] { "%", "px", "px", "色", "ms", "px" }, mosaic.ParamRows.Select(r => r.Unit.Text));
            }
            var (f3, _, generated) = Open(PatternKind.Generated);
            using (f3)
            {
                Assert.Equal(new[] { "透明度", "手法", "ベース色", "セルサイズ", "色のばらつき", "目地幅", "シード" }, generated.RowLabels);
                Assert.Equal(new[] { "%", "px", "%", "px" }, generated.ParamRows.Select(r => r.Unit.Text));
            }
        }

        [AvaloniaFact]
        public void GT27_SliderAndInput_StayInSync()
        {
            var (f, area, panel) = Open(PatternKind.Mosaic);
            using (f)
            {
                var block = Row(panel, "block");
                Assert.Equal("12", block.Input.Text);
                block.Slider.Value = block.Spec.ToSliderPosition(40) * ParamRow.SliderSteps;
                Assert.Equal("40", block.Input.Text);
                Assert.Equal(40, area.Model.Mosaic.Params["pixelate"]["block"]);
                Type(block, "20");
                Assert.Equal(block.Spec.ToSliderPosition(20) * ParamRow.SliderSteps, block.Slider.Value, 6);
            }
        }

        [AvaloniaFact]
        public void GT28_InputAcceptsValuesBeyondTheSlider()
        {
            var (f, area, panel) = Open(PatternKind.Mosaic);
            using (f)
            {
                var block = Row(panel, "block");
                Type(block, "200");
                Assert.Equal(200, area.Model.Mosaic.Params["pixelate"]["block"]);
                Assert.Equal(ParamRow.SliderSteps, block.Slider.Value, 6);
                Assert.Equal("200", block.Input.Text);
            }
        }

        [AvaloniaFact]
        public void GT29_InputClampedToMinMax()
        {
            var (f, area, panel) = Open(PatternKind.Mosaic);
            using (f)
            {
                var block = Row(panel, "block");
                Type(block, "1000");
                Assert.Equal(256, area.Model.Mosaic.Params["pixelate"]["block"]);
                Assert.Equal("256", block.Input.Text);
                Type(block, "-5");
                Assert.Equal(2, area.Model.Mosaic.Params["pixelate"]["block"]);
                Type(block, "abc");
                Assert.Equal("2", block.Input.Text);
            }
        }

        [AvaloniaFact]
        public void GT30_IntervalLogSlider()
        {
            var (f, area, panel) = Open(PatternKind.Mosaic);
            using (f)
            {
                var interval = Row(panel, CommonParams.IntervalMs);
                foreach (int ms in new[] { 0, 100, 300000 })
                {
                    Type(interval, ms.ToString());
                    Assert.Equal(ms, area.Model.Mosaic.Common.IntervalMs);
                    double position = interval.Slider.Value / ParamRow.SliderSteps;
                    Assert.Equal(interval.Spec.ToSliderPosition(ms), position, 6);
                    interval.Slider.Value = 0;
                    interval.Slider.Value = position * ParamRow.SliderSteps;
                    Assert.InRange(area.Model.Mosaic.Common.IntervalMs, ms * 0.99, ms * 1.01 + 0.5);
                }
                Type(interval, "0");
                Assert.Equal(0, interval.Slider.Value);
            }
        }

        [AvaloniaFact]
        public void GT31_ChangesApplyAndSaveImmediately()
        {
            var (f, area, panel) = Open(PatternKind.Fill);
            using (f)
            {
                int saves = f.Manager.SaveCount;
                Type(Row(panel, "opacity"), "50");
                Assert.Equal(0.5, area.Model.Opacity, 6);
                Assert.Equal(saves + 1, f.Manager.SaveCount);
                Assert.Equal(0.5, area.Window.PatternView.Opacity, 6); // edit mode fades the pattern only
                Assert.Equal(0.5, Hider.Core.Persistence.AreasFile.Load(f.AreasPath, out _).Areas.Single().Opacity, 6);

                // Sliding saves once, 500 ms after the last move (no write per slider step).
                var opacity = Row(panel, "opacity");
                opacity.Slider.Value = 700;
                opacity.Slider.Value = 800;
                Assert.Equal(saves + 1, f.Manager.SaveCount);
                f.Time.Advance(TimeSpan.FromMilliseconds(500));
                Assert.Equal(saves + 2, f.Manager.SaveCount);
            }
        }

        [AvaloniaFact]
        public void GT32_MethodSwitch_KeepsOtherMethodsValues()
        {
            var (f, area, panel) = Open(PatternKind.Generated);
            using (f)
            {
                Type(Row(panel, "cellSize"), "30");
                area.Model.Generated.ParamsFor("other")["x"] = 5; // values of any other method id stay
                var methods = MethodRegistry.OfKind(MethodKind.Generated);
                if (methods.Count > 1)
                {
                    panel.MethodBox!.SelectedIndex = 1;
                    Assert.Equal(methods[1].Id, area.Model.Generated.Method);
                    panel = area.Window.Settings!;
                    panel.MethodBox!.SelectedIndex = 0;
                }
                Assert.Equal(30, area.Model.Generated.Params["block"]["cellSize"]);
                Assert.Equal(5, area.Model.Generated.Params["other"]["x"]);
                Assert.Equal("30", Row(area.Window.Settings!, "cellSize").Input.Text);
            }
        }

        [AvaloniaFact]
        public void GT33_FillPresets_SameAsCanvasAndApply()
        {
            var (f, area, panel) = Open(PatternKind.Fill);
            using (f)
            {
                Assert.Equal(new[] { "#68768A", "#2B2F36", "#4B4F57", "#8A8F99", "#C8CED8", "#3E5A7A", "#5B4B6E", "#6E5B3E" },
                    panel.PresetButtons.Select(b => (string)b.Tag!));
                int saves = f.Manager.SaveCount;
                panel.PresetButtons[5].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("#3E5A7A", area.Model.Fill.Color);
                Assert.Equal(saves + 1, f.Manager.SaveCount);
            }
        }

        [AvaloniaFact]
        public void GT34_StripHeight_HasLinuxBadgeOnEveryOs()
        {
            var (f, _, panel) = Open(PatternKind.Mosaic);
            using (f)
            {
                var strip = Row(panel, CommonParams.StripPx);
                Assert.NotNull(strip.Badge);
                Assert.Equal("Linux", ((TextBlock)strip.Badge!.Child!).Text);
                Assert.All(panel.ParamRows.Where(r => r != strip), r => Assert.Null(r.Badge));
            }
        }

        [AvaloniaFact]
        public void GT35_RefreshButton_MosaicOnlyAndRequestsCapture()
        {
            using var f = new AreaFixture();
            var capture = (FakeUnderlayCapture)f.Capture;
            var area = f.Add(new Hider.Core.Model.PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            area.SettingsChanged();
            Assert.True(area.Window.Toolbar.RefreshButton.IsVisible);
            int calls = capture.Calls;
            area.Window.Toolbar.RefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(calls + 1, capture.Calls);
            area.SetPattern(PatternKind.Fill);
            Assert.False(area.Window.Toolbar.RefreshButton.IsVisible);
            area.SetPattern(PatternKind.Generated);
            Assert.False(area.Window.Toolbar.RefreshButton.IsVisible);
        }

        [AvaloniaFact]
        public void GT40_NewMethodsGetRowsWithoutUiCode()
        {
            // Extensibility (CT-19, UI part): the rows come from ParamSpec alone.
            var spec = new ParamSpec("amount", "param.block", "px", 1, 10, 1, 10, 5);
            var row = new ParamRow(spec);
            row.SetValue(7);
            Assert.Equal("ブロック", row.Label.Text);
            Assert.Equal("7", row.Input.Text);
            Assert.Equal("px", row.Unit.Text);
            Assert.Equal(spec.ToSliderPosition(7) * ParamRow.SliderSteps, row.Slider.Value, 6);
        }
    }
}
