using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    /// <summary>Area, AreaDefaults and the settings classes it holds.</summary>
    public class AreaTests
    {
        [Fact]
        public void CT10_Area_Defaults()
        {
            var area = new Area { Id = "0f3b8d2e" };
            Assert.Equal("0f3b8d2e", area.Id);
            Assert.Equal(default, area.Rect);
            Assert.Equal(1.0, area.Opacity);
            Assert.Equal(PatternKind.Mosaic, area.Pattern);
            Assert.Equal(AreaDefaults.FillColor, area.Fill.Color);
            Assert.Equal(AreaDefaults.MosaicMethod, area.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, area.Generated.Method);
            Assert.Empty(area.Mosaic.Params);
            Assert.Empty(area.Generated.Params);
            Assert.Equal(1.0, area.EffectiveOpacity);
        }

        [Fact]
        public void CT111_Area_EachInstanceHasItsOwnSettings()
        {
            var a = new Area { Id = "00000001" };
            var b = new Area { Id = "00000002" };
            Assert.NotSame(a.Fill, b.Fill);
            Assert.NotSame(a.Mosaic, b.Mosaic);
            Assert.NotSame(a.Generated, b.Generated);
            a.Fill.Color = "#000000";
            Assert.Equal(AreaDefaults.FillColor, b.Fill.Color);
        }

        [Fact]
        public void CT111_Area_RectIsSettable()
        {
            var area = new Area { Id = "00000001", Rect = new PhysicalRect(1, 2, 3, 4) };
            Assert.Equal(new PhysicalRect(1, 2, 3, 4), area.Rect);
            area.Rect = new PhysicalRect(-5, -6, 7, 8);
            Assert.Equal(new PhysicalRect(-5, -6, 7, 8), area.Rect);
        }

        [Theory]
        [InlineData(0.0, 0.0)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 1.0)]
        [InlineData(-0.1, 0.0)]
        [InlineData(1.1, 1.0)]
        [InlineData(double.NegativeInfinity, 0.0)]
        [InlineData(double.PositiveInfinity, 1.0)]
        [InlineData(double.NaN, 1.0)]
        public void CT03_Area_Opacity_ClampedTo01_NaNIsOpaque(double _value, double _expected)
        {
            var area = new Area { Id = "00000001", Opacity = 0.3 };
            area.Opacity = _value;
            Assert.Equal(_expected, area.Opacity);
        }

        [Theory]
        [InlineData(PatternKind.Fill, 0.0, 0.10)]
        [InlineData(PatternKind.Fill, 0.10, 0.10)]
        [InlineData(PatternKind.Fill, 0.5, 0.5)]
        [InlineData(PatternKind.Generated, 0.09, 0.10)]
        [InlineData(PatternKind.Generated, 1.0, 1.0)]
        [InlineData(PatternKind.Mosaic, 0.0, 0.0)]
        [InlineData(PatternKind.Mosaic, 0.05, 0.05)]
        public void CT03_Area_EffectiveOpacity_FollowsThePattern(PatternKind _pattern, double _opacity, double _expected)
        {
            var area = new Area { Id = "00000001", Pattern = _pattern, Opacity = _opacity };
            Assert.Equal(_expected, area.EffectiveOpacity);
            Assert.Equal(_opacity, area.Opacity); // the stored value is not changed by the pattern's range
        }

        [Theory]
        [InlineData(PatternKind.Fill)]
        [InlineData(PatternKind.Mosaic)]
        [InlineData(PatternKind.Generated)]
        public void CT03_ClampOpacity_NaN_IsOpaque_ForEveryPattern(PatternKind _pattern)
        {
            Assert.Equal(1.0, Area.ClampOpacity(_pattern, double.NaN));
            Assert.Equal(1.0, Area.ClampOpacity(_pattern, double.PositiveInfinity));
            Assert.Equal(_pattern == PatternKind.Mosaic ? 0.0 : Area.MinAlphaOpacity, Area.ClampOpacity(_pattern, double.NegativeInfinity));
        }

        [Fact]
        public void CT03_ClampOpacity_UndefinedPattern_UsesTheAlphaRange()
        {
            // Only mosaic may go down to 0: any other value (also one not defined) keeps the area visible.
            Assert.Equal(Area.MinAlphaOpacity, Area.ClampOpacity((PatternKind)7, 0.0));
        }

        [Fact]
        public void CT03_MinAlphaOpacity_IsTenPercent()
        {
            Assert.Equal(0.10, Area.MinAlphaOpacity);
        }

        [Theory]
        [InlineData(true, AreaDefaults.IntervalMsWindows)]
        [InlineData(false, AreaDefaults.IntervalMsLinux)]
        public void CT10_CreateNew_IdRectAndOsInterval(bool _isWindows, int _interval)
        {
            var rect = new PhysicalRect(-10, 20, 300, 400);
            var area = Area.CreateNew("1a2b3c4d", rect, _isWindows);
            Assert.Equal("1a2b3c4d", area.Id);
            Assert.Equal(rect, area.Rect);
            Assert.Equal(_interval, area.Mosaic.Common.IntervalMs);
            Assert.Equal(PatternKind.Mosaic, area.Pattern);
            Assert.Equal(1.0, area.Opacity);
            Assert.Equal(AreaDefaults.MosaicMethod, area.Mosaic.Method);
            Assert.Equal(AreaDefaults.GeneratedMethod, area.Generated.Method);
            Assert.Equal(AreaDefaults.FillColor, area.Fill.Color);
            Assert.Equal(AreaDefaults.MosaicBlur, area.Mosaic.Common.Blur);
            Assert.Equal(AreaDefaults.MosaicLevels, area.Mosaic.Common.Levels);
            Assert.Equal(AreaDefaults.StripPx, area.Mosaic.Common.StripPx);
            Assert.Equal(AreaDefaults.Seed, area.Generated.Common.Seed);
            Assert.Equal(AreaDefaults.FillColor, area.Generated.Common.BaseColor);
        }

        [Fact]
        public void CT10_AreaDefaults_Values()
        {
            // Spec §4.5 / FR-14 / FR-51.
            Assert.Equal("#68768A", AreaDefaults.FillColor);
            Assert.Equal(400, AreaDefaults.NewAreaWidthDip);
            Assert.Equal(240, AreaDefaults.NewAreaHeightDip);
            Assert.Equal(24, AreaDefaults.MinSizeDip);
            Assert.Equal(16384, AreaDefaults.MaxSizeDip);
            Assert.Equal("pixelate", AreaDefaults.MosaicMethod);
            Assert.Equal("block", AreaDefaults.GeneratedMethod);
            Assert.Equal(0, AreaDefaults.MosaicBlur);
            Assert.Equal(256, AreaDefaults.MosaicLevels);
            Assert.Equal(8, AreaDefaults.StripPx);
            Assert.Equal(1234, AreaDefaults.Seed);
            Assert.Equal(65536, AreaDefaults.SeedCount);
            Assert.Equal(100, AreaDefaults.IntervalMsWindows);
            Assert.Equal(300000, AreaDefaults.IntervalMsLinux);
            Assert.True(ColorText.IsValid(AreaDefaults.FillColor));
            Assert.InRange(AreaDefaults.Seed, 0, AreaDefaults.SeedCount - 1);
        }

        [Fact]
        public void CT10_AreaDefaults_IntervalMs_PerOs()
        {
            Assert.Equal(100, AreaDefaults.IntervalMs(true));
            Assert.Equal(300000, AreaDefaults.IntervalMs(false));
            Assert.Equal(OperatingSystem.IsWindows() ? 100 : 300000, AreaDefaults.IntervalMsForCurrentOs);
        }

        [Fact]
        public void CT111_FillSettings_DefaultColor_Settable()
        {
            var fill = new FillSettings();
            Assert.Equal(AreaDefaults.FillColor, fill.Color);
            fill.Color = "#123456";
            Assert.Equal("#123456", fill.Color);
        }

        [Fact]
        public void CT111_MethodSettings_Defaults()
        {
            var settings = new MethodSettings<MosaicCommon>();
            Assert.Equal("", settings.Method);
            Assert.NotNull(settings.Common);
            Assert.Empty(settings.Params);
            var generated = new MethodSettings<GeneratedCommon>();
            Assert.Equal("", generated.Method);
            Assert.Equal(AreaDefaults.Seed, generated.Common.Seed);
        }

        [Fact]
        public void CT111_MethodSettings_ParamsFor_CreatesOnceAndReturnsTheSameValues()
        {
            var settings = new MethodSettings<MosaicCommon>();
            var first = settings.ParamsFor("pixelate");
            Assert.Empty(first);
            Assert.Same(first, settings.Params["pixelate"]);
            first["block"] = 20;
            var again = settings.ParamsFor("pixelate");
            Assert.Same(first, again);
            Assert.Equal(20, again["block"]);
            Assert.Single(settings.Params);
        }

        [Fact]
        public void CT111_MethodSettings_ParamsFor_ReturnsAnExistingEntry_OthersKept()
        {
            var existing = new Dictionary<string, double> { ["k"] = 1.5 };
            var settings = new MethodSettings<GeneratedCommon>
            {
                Params = new() { ["future"] = existing, ["block"] = new() { ["gap"] = 2 } },
            };
            Assert.Same(existing, settings.ParamsFor("future"));
            settings.ParamsFor("noise")["scale"] = 3;
            Assert.Equal(3, settings.Params.Count);
            Assert.Equal(1.5, settings.Params["future"]["k"]);
            Assert.Equal(2, settings.Params["block"]["gap"]);
        }

        [Fact]
        public void CT111_MethodSettings_CommonAndParamsAreReplaceable()
        {
            var common = new MosaicCommon { Levels = 4 };
            var values = new Dictionary<string, Dictionary<string, double>>();
            var settings = new MethodSettings<MosaicCommon> { Method = "blur", Common = common, Params = values };
            Assert.Equal("blur", settings.Method);
            Assert.Same(common, settings.Common);
            Assert.Same(values, settings.Params);
        }

        [Fact]
        public void CT111_MosaicCommon_Defaults_AndSetters()
        {
            var common = new MosaicCommon();
            Assert.Equal(AreaDefaults.MosaicBlur, common.Blur);
            Assert.Equal(AreaDefaults.MosaicLevels, common.Levels);
            Assert.Equal(AreaDefaults.IntervalMsForCurrentOs, common.IntervalMs);
            Assert.Equal(AreaDefaults.StripPx, common.StripPx);
            common.Blur = 3.5;
            common.Levels = 16;
            common.IntervalMs = 250;
            common.StripPx = 4;
            Assert.Equal((3.5, 16, 250, 4), (common.Blur, common.Levels, common.IntervalMs, common.StripPx));
        }

        [Fact]
        public void CT111_GeneratedCommon_Defaults_AndSetters()
        {
            var common = new GeneratedCommon();
            Assert.Equal(AreaDefaults.FillColor, common.BaseColor);
            Assert.Equal(AreaDefaults.Seed, common.Seed);
            common.BaseColor = "#ABCDEF";
            common.Seed = 7;
            Assert.Equal(("#ABCDEF", 7), (common.BaseColor, common.Seed));
        }
    }
}
