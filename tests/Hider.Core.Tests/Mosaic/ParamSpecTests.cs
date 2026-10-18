using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>Every member and branch of <see cref="ParamSpec"/> with specs whose numbers are easy to check by hand.</summary>
    public class ParamSpecTests
    {
        // Linear: box 0..100, slider 10..50, default 20.
        private static readonly ParamSpec linear = new("k", "param.block", ParamUnit.Px, 0, 100, 10, 50, 20);

        // Log with a zero zone (Min < SliderMin), like the refresh interval.
        private static readonly ParamSpec logZone = new("i", "param.intervalMs", ParamUnit.Ms, 0, 1000, 10, 1000, 100, LogScale: true);

        // Log without a zero zone (Min == SliderMin).
        private static readonly ParamSpec logPlain = new("s", "param.noiseScale", ParamUnit.None, 1, 1000, 1, 100, 10, LogScale: true);

        [Fact]
        public void CT07_LogZeroZone_IsTwoPercent()
        {
            Assert.Equal(0.02, ParamSpec.LogZeroZone);
        }

        [Fact]
        public void CT127_Defaults_OfTheOptionalMembers()
        {
            Assert.False(linear.LogScale);
            Assert.False(linear.LerpWithOpacity);
            Assert.Equal(1.0, linear.LerpFloor);
            Assert.Equal("k", linear.Key);
            Assert.Equal("param.block", linear.LabelKey);
            Assert.Equal(ParamUnit.Px, linear.Unit);
            Assert.Equal((0.0, 100.0, 10.0, 50.0, 20.0), (linear.Min, linear.Max, linear.SliderMin, linear.SliderMax, linear.Default));
        }

        [Fact]
        public void CT127_Record_EqualityAndWith()
        {
            var same = new ParamSpec("k", "param.block", ParamUnit.Px, 0, 100, 10, 50, 20);
            Assert.Equal(linear, same);
            Assert.Equal(linear.GetHashCode(), same.GetHashCode());
            Assert.NotEqual(linear, linear with { Default = 21 });
        }

        [Theory]
        [InlineData(ParamUnit.None, false, 1.0)]
        [InlineData(ParamUnit.Px, false, 1.0)]
        [InlineData(ParamUnit.Colors, false, 1.0)]
        [InlineData(ParamUnit.Steps, false, 1.0)]
        [InlineData(ParamUnit.Percent, true, 100.0)]
        [InlineData(ParamUnit.Ms, false, 1.0)]
        public void CT127_IsFractionAndDisplayFactor_OnlyPercentIsAFraction(ParamUnit _unit, bool _fraction, double _factor)
        {
            var spec = linear with { Unit = _unit };
            Assert.Equal(_fraction, spec.IsFraction);
            Assert.Equal(_factor, spec.DisplayFactor);
        }

        [Theory]
        [InlineData(50, 50)]
        [InlineData(0, 0)]
        [InlineData(100, 100)]
        [InlineData(-0.001, 0)]
        [InlineData(100.5, 100)]
        [InlineData(double.NegativeInfinity, 0)]
        [InlineData(double.PositiveInfinity, 100)]
        [InlineData(double.NaN, 20)] // NaN takes the default
        public void CT120_Clamp_KeepsTheBoxRange_NaNIsTheDefault(double _value, double _expected)
        {
            Assert.Equal(_expected, linear.Clamp(_value));
        }

        [Theory]
        [InlineData(30, 0.0, 30)]
        [InlineData(30, 1.0, 30)]
        [InlineData(500, 0.5, 100)]
        [InlineData(double.NaN, 0.5, 20)]
        public void CT04_Effective_WithoutLerp_IsTheClampedValueWhateverTheOpacity(double _value, double _opacity, double _expected)
        {
            Assert.Equal(_expected, linear.Effective(_value, _opacity));
        }

        [Theory]
        [InlineData(30, 0.0, 1)]
        [InlineData(30, 1.0, 30)]
        [InlineData(30, 0.5, 15.5)]
        [InlineData(30, -1.0, 1)]       // opacity below 0 counts as 0
        [InlineData(30, 2.0, 30)]       // above 1 as 1
        [InlineData(500, 0.5, 50.5)]    // the value is clamped before the interpolation
        [InlineData(double.NaN, 1.0, 20)]
        public void CT04_Effective_WithLerp_DefaultFloorIsOne(double _value, double _opacity, double _expected)
        {
            var spec = linear with { LerpWithOpacity = true };
            Assert.Equal(_expected, spec.Effective(_value, _opacity), 9);
        }

        [Fact]
        public void CT05_Effective_WithLerp_FloorBelowTheBoxMinimum()
        {
            // The floor is reached at opacity 0 even when it is outside Min..Max (hexcell / pixelate: floor 1 below Min 2).
            var spec = linear with { Min = 2, LerpWithOpacity = true, LerpFloor = -10 };
            Assert.Equal(-10, spec.Effective(30, 0.0), 9);
            Assert.Equal(10, spec.Effective(30, 0.5), 9);
        }

        [Theory]
        [InlineData(10, 0.0)]
        [InlineData(50, 1.0)]
        [InlineData(30, 0.5)]
        [InlineData(20, 0.25)]
        [InlineData(5, 0.0)]       // below the slider: sticks to the start
        [InlineData(80, 1.0)]      // above the slider: sticks to the end
        [InlineData(-50, 0.0)]
        [InlineData(double.NaN, 0.25)] // NaN is the default 20
        public void CT07_ToSliderPosition_Linear(double _value, double _expected)
        {
            Assert.Equal(_expected, linear.ToSliderPosition(_value), 12);
        }

        [Theory]
        [InlineData(0.0, 10)]
        [InlineData(1.0, 50)]
        [InlineData(0.25, 20)]
        [InlineData(-1.0, 10)]
        [InlineData(2.0, 50)]
        public void CT07_FromSliderPosition_Linear(double _position, double _expected)
        {
            Assert.Equal(_expected, linear.FromSliderPosition(_position), 12);
        }

        [Theory]
        [InlineData(0, 0.0)]          // v <= Min: start of the zero zone
        [InlineData(-5, 0.0)]         // clamped to Min
        [InlineData(5, 0.01)]         // inside the zero zone: linear over Min..SliderMin
        [InlineData(10, 0.02)]        // SliderMin: end of the zero zone
        [InlineData(100, 0.51)]       // geometric middle of 10..1000
        [InlineData(1000, 1.0)]
        [InlineData(5000, 1.0)]
        public void CT07_ToSliderPosition_LogWithZeroZone(double _value, double _expected)
        {
            Assert.Equal(_expected, logZone.ToSliderPosition(_value), 12);
        }

        [Theory]
        [InlineData(0.0, 0)]
        [InlineData(0.01, 5)]
        [InlineData(0.02, 10)]
        [InlineData(0.51, 100)]
        [InlineData(1.0, 1000)]
        [InlineData(-0.5, 0)]
        [InlineData(1.5, 1000)]
        public void CT07_FromSliderPosition_LogWithZeroZone(double _position, double _expected)
        {
            Assert.Equal(_expected, logZone.FromSliderPosition(_position), 9);
        }

        [Theory]
        [InlineData(1, 0.0)]
        [InlineData(10, 0.5)]
        [InlineData(100, 1.0)]
        [InlineData(500, 1.0)]
        [InlineData(0, 0.0)] // clamped to Min = SliderMin
        public void CT07_ToSliderPosition_LogWithoutZeroZone(double _value, double _expected)
        {
            Assert.Equal(_expected, logPlain.ToSliderPosition(_value), 12);
        }

        [Theory]
        [InlineData(0.0, 1)]
        [InlineData(0.5, 10)]
        [InlineData(1.0, 100)]
        [InlineData(0.001, 1.0046157902783952)] // no zero zone: the log scale starts at once
        public void CT07_FromSliderPosition_LogWithoutZeroZone(double _position, double _expected)
        {
            Assert.Equal(_expected, logPlain.FromSliderPosition(_position), 9);
        }

        [Fact]
        public void CT07_SliderRoundTrip_EverySpecOfEveryMethod()
        {
            // Inside the slider range a value survives value -> position -> value for every registered spec.
            var specs = MethodRegistry.All.SelectMany(_m => _m.Params).Concat(CommonParams.Mosaic);
            foreach (var spec in specs)
            {
                for (int i = 0; i <= 20; i++)
                {
                    double position = i / 20.0;
                    double value = spec.FromSliderPosition(position);
                    Assert.InRange(value, spec.Min, spec.Max);
                    Assert.Equal(position, spec.ToSliderPosition(value), 9);
                }
            }
        }
    }
}
