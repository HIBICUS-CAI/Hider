using Hider.Core.Model;
using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    public class ParamTests
    {
        private static ParamSpec Block => MethodRegistry.Find("pixelate")!.Params.Single(p => p.Key == "block");
        private static ParamSpec Blur => CommonParams.Mosaic.Single(p => p.Key == CommonParams.Blur);
        private static ParamSpec Interval => CommonParams.Mosaic.Single(p => p.Key == CommonParams.IntervalMs);

        [Fact]
        public void CT04_Effective_LerpFloorOne()
        {
            Assert.Equal(12, Block.Effective(12, 1.0), 9);
            Assert.Equal(1, Block.Effective(12, 0.0), 9);
            Assert.Equal(6.5, Block.Effective(12, 0.5), 9);
        }

        [Fact]
        public void CT05_Effective_LerpFloorZero()
        {
            Assert.Equal(0, Blur.Effective(8, 0.0), 9);
            Assert.Equal(4, Blur.Effective(8, 0.5), 9);
            Assert.Equal(8, Blur.Effective(8, 1.0), 9);
        }

        [Fact]
        public void CT06_ParamSpec_RangesConsistent()
        {
            var specs = MethodRegistry.All.SelectMany(m => m.Params).Concat(CommonParams.Mosaic).ToList();
            Assert.NotEmpty(specs);
            foreach (var s in specs)
            {
                Assert.True(s.Min <= s.SliderMin, $"{s.Key}: Min {s.Min} > SliderMin {s.SliderMin}");
                Assert.True(s.SliderMin < s.SliderMax, $"{s.Key}: SliderMin {s.SliderMin} >= SliderMax {s.SliderMax}");
                Assert.True(s.SliderMax <= s.Max, $"{s.Key}: SliderMax {s.SliderMax} > Max {s.Max}");
                Assert.InRange(s.Default, s.Min, s.Max);
                Assert.True(!s.LogScale || s.SliderMin > 0, $"{s.Key}: log slider needs SliderMin > 0");
            }
        }

        [Fact]
        public void CT07_LogSlider_RoundTripWithinOnePercent()
        {
            Assert.True(Interval.LogScale);
            foreach (double v in new[] { 10.0, 33, 100, 1000, 30000, 300000, 3600000 })
            {
                double back = Interval.FromSliderPosition(Interval.ToSliderPosition(v));
                Assert.True(Math.Abs(back - v) <= v * 0.01, $"{v} -> {back}");
            }
            Assert.Equal(0.0, Interval.ToSliderPosition(0));
            Assert.Equal(0.0, Interval.FromSliderPosition(0));
            Assert.Equal(1.0, Interval.ToSliderPosition(3600000));
            Assert.True(Interval.ToSliderPosition(5) is > 0 and < ParamSpec.LogZeroZone);
        }

        [Fact]
        public void CT08_ParamValues_DefaultsAndUnknownKeys()
        {
            var method = MethodRegistry.Find("pixelate")!;
            var stored = new Dictionary<string, double> { ["somethingElse"] = 99 };
            var p = new ParamValues(method.Params, stored, 1.0);
            Assert.Equal(12, p.Raw("block"));
            Assert.Equal(0, p.Raw("notAKey"));
            Assert.Equal(0, p.Effective("notAKey"));

            var settings = new MethodSettings<MosaicCommon> { Method = "pixelate" };
            settings.ParamsFor("pixelate")["block"] = 1000; // out of range: clamped to Max
            settings.Common.Levels = 16;
            var resolved = ParamValues.ForMosaic(method, settings, 1.0);
            Assert.Equal(256, resolved.Raw("block"));
            Assert.Equal(16, resolved.Raw(CommonParams.Levels));
            Assert.Equal(0, resolved.Raw(CommonParams.Blur));
        }

        [Fact]
        public void CT96_Slider_OutOfRangeValueSticksToEnd()
        {
            Assert.Equal(1.0, Block.ToSliderPosition(200));
            Assert.Equal(0.0, Block.ToSliderPosition(2));
            Assert.Equal(64, Block.FromSliderPosition(1.0));
        }
    }
}
