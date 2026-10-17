using Avalonia.Headless.XUnit;
using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Settings;

namespace Hider.App.Tests.Settings
{
    /// <summary>The common mosaic rows (blur, levels, update interval, strip height) and the fields behind them.</summary>
    public class SettingsPanelCommonTests
    {
        /// <summary>Each common row's field, written down here independently of SettingsPanel's switch.</summary>
        private static readonly Dictionary<string, Func<MosaicCommon, double>> fields = new()
        {
            [CommonParams.Blur] = _c => _c.Blur,
            [CommonParams.Levels] = _c => _c.Levels,
            [CommonParams.IntervalMs] = _c => _c.IntervalMs,
            [CommonParams.StripPx] = _c => _c.StripPx,
        };

        [Fact]
        public void GT119_EveryCommonRow_HasAField()
        {
            // A common parameter added to CommonParams.Mosaic without a field would show 0 and drop its edits: this table, and
            // GetCommon / SetCommon, must follow it.
            Assert.Equal(CommonParams.Mosaic.Select(_s => _s.Key).Order(), fields.Keys.Order());
        }

        [AvaloniaFact]
        public void GT119_EveryCommonRow_ShowsItsField_AndWritesIt()
        {
            using var f = new AreaFixture();
            var area = f.Add(new PhysicalRect(100, 200, 400, 240), PatternKind.Mosaic);
            var common = area.Model.Mosaic.Common;
            common.Blur = 7;
            common.Levels = 17;
            common.IntervalMs = 1234;
            common.StripPx = 12;
            area.Window.ToggleSettings();
            var panel = area.Window.Settings!;
            var shown = new Dictionary<string, double> { [CommonParams.Blur] = 7, [CommonParams.Levels] = 17, [CommonParams.IntervalMs] = 1234, [CommonParams.StripPx] = 12 };
            var typed = new Dictionary<string, double> { [CommonParams.Blur] = 9, [CommonParams.Levels] = 33, [CommonParams.IntervalMs] = 2500, [CommonParams.StripPx] = 16 };

            foreach (var spec in CommonParams.Mosaic)
            {
                var row = panel.ParamRows.Single(_r => _r.Spec.Key == spec.Key);
                Assert.Equal(shown[spec.Key], row.Value);

                row.Input.Text = typed[spec.Key].ToString(System.Globalization.CultureInfo.InvariantCulture);
                row.CommitInput();

                Assert.Equal(typed[spec.Key], fields[spec.Key](common));
            }
        }

        [Theory]
        [InlineData(CommonParams.Blur, 2.25, 2.25)]  // kept as is (a fraction of a pixel matters to the blur)
        [InlineData(CommonParams.Levels, 16.6, 17)]  // whole numbers: rounded
        [InlineData(CommonParams.IntervalMs, 99.4, 99)]
        [InlineData(CommonParams.StripPx, 7.7, 8)]
        public void GT119_SetCommon_StoresTheField_RoundingWholeNumbers(string _key, double _value, double _stored)
        {
            var common = new MosaicCommon();

            SettingsPanel.SetCommon(common, _key, _value);

            Assert.Equal(_stored, fields[_key](common));
            Assert.Equal(_stored, SettingsPanel.GetCommon(common, _key));
        }

        [Theory]
        [InlineData(CommonParams.Seed)] // a generated common parameter, not a mosaic one
        [InlineData("bogus")]
        [InlineData("")]
        public void GT119_UnknownKey_Throws_InsteadOfShowingZero(string _key)
        {
            var common = new MosaicCommon();

            var get = Assert.Throws<ArgumentOutOfRangeException>(() => SettingsPanel.GetCommon(common, _key));
            var set = Assert.Throws<ArgumentOutOfRangeException>(() => SettingsPanel.SetCommon(common, _key, 1));

            Assert.Equal("_key", get.ParamName);
            Assert.Equal("_key", set.ParamName);
            Assert.Equal(AreaDefaults.MosaicBlur, common.Blur); // nothing was written
            Assert.Equal(AreaDefaults.MosaicLevels, common.Levels);
        }
    }
}
