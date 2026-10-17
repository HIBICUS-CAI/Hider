using Hider.Core.Model;
using Hider.Core.Mosaic;
using Hider.Core.Tests.Fakes;
using Hider.Core.Tests.Imaging;
using SkiaSharp;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>
    /// Every parameter of every method (and the mosaic common values) at its boundaries: Min and Max draw, values past them
    /// draw as the boundary, NaN draws as the default, and the CPU stand-ins (cell evaluation, ICpuRenderer) give what the
    /// shader gives there.
    /// </summary>
    public class MethodBoundaryTests
    {
        private const int Width = 64, Height = 40;

        public static TheoryData<string, string> EveryMethodParam()
        {
            var data = new TheoryData<string, string>();
            foreach (var method in MethodRegistry.All)
            {
                foreach (var spec in method.Params)
                {
                    data.Add(method.Id, spec.Key);
                }
            }
            return data;
        }

        public static TheoryData<string, string> EveryMosaicCommonParam()
        {
            var data = new TheoryData<string, string>();
            foreach (var method in MethodRegistry.OfKind(MethodKind.Mosaic))
            {
                data.Add(method.Id, CommonParams.Blur);
                data.Add(method.Id, CommonParams.Levels);
            }
            return data;
        }

        private static SKBitmap Draw(string _id, string _key, double _value, SKImage _source)
        {
            var method = MethodRegistry.Find(_id)!;
            var values = new Dictionary<string, double> { [_key] = _value };
            return method.Kind == MethodKind.Mosaic
                ? Render.Mosaic(_id, _params: values, _source: _source)
                : Render.Generated(_id, Width, Height, values);
        }

        [Fact]
        public void CT120_EveryMethodParam_CoversEveryRegisteredSpec()
        {
            Assert.Equal(MethodRegistry.All.Sum(_m => _m.Params.Count), EveryMethodParam().Count);
            Assert.Equal(8, EveryMethodParam().Count); // block 3, noise 2, pixelate, hexcell, blur
        }

        [Theory]
        [MemberData(nameof(EveryMethodParam))]
        public void CT120_PastTheRange_DrawsAsTheBoundary_NaNAsTheDefault(string _id, string _key)
        {
            var spec = MethodRegistry.Find(_id)!.Params.Single(_s => _s.Key == _key);
            using var source = SyntheticImage.CreateImage(Width, Height);
            using var min = Draw(_id, _key, spec.Min, source);
            using var belowMin = Draw(_id, _key, spec.Min - 1, source);
            using var farBelow = Draw(_id, _key, double.NegativeInfinity, source);
            using var max = Draw(_id, _key, spec.Max, source);
            using var aboveMax = Draw(_id, _key, spec.Max + 1, source);
            using var farAbove = Draw(_id, _key, double.PositiveInfinity, source);
            using var byDefault = Draw(_id, _key, spec.Default, source);
            using var nan = Draw(_id, _key, double.NaN, source);
            Assert.Equal(0, Golden.MaxDiff(min, belowMin));
            Assert.Equal(0, Golden.MaxDiff(min, farBelow));
            Assert.Equal(0, Golden.MaxDiff(max, aboveMax));
            Assert.Equal(0, Golden.MaxDiff(max, farAbove));
            Assert.Equal(0, Golden.MaxDiff(byDefault, nan));
            Assert.Equal((Width, Height), (min.Width, min.Height));
        }

        [Theory]
        [MemberData(nameof(EveryMethodParam))]
        public void CT119_AtTheBoundaries_CpuPathsMatchTheShader(string _id, string _key)
        {
            var method = MethodRegistry.Find(_id)!;
            var spec = method.Params.Single(_s => _s.Key == _key);
            foreach (double value in new[] { spec.Min, spec.SliderMin, spec.SliderMax, spec.Max })
            {
                var stored = new Dictionary<string, Dictionary<string, double>> { [_id] = new() { [_key] = value } };
                if (method.Kind == MethodKind.Generated)
                {
                    var settings = new MethodSettings<GeneratedCommon> { Params = stored };
                    var p = ParamValues.ForGenerated(method, settings);
                    MethodConsistencyTests.AssertClose(method, new MosaicInputs { AreaSize = new SKSizeI(97, 61) }, p, 0.002);
                }
                else
                {
                    var settings = new MethodSettings<MosaicCommon> { Params = stored };
                    var p = ParamValues.ForMosaic(method, settings, 1.0);
                    using var source = SyntheticImage.CreateImage(97, 61);
                    var inputs = MosaicPipeline.Prepare(source, default, p, null, method);
                    try
                    {
                        MethodConsistencyTests.AssertClose(method, inputs, p, 0.005);
                    }
                    finally
                    {
                        MosaicPipeline.Release(inputs, source);
                    }
                }
            }
        }

        [Theory]
        [InlineData("pixelate")]
        [InlineData("hexcell")]
        [InlineData("blur")]
        public void CT03_Opacity_OutsideZeroToOne_IsClamped(string _id)
        {
            using var source = SyntheticImage.CreateImage(Width, Height);
            using var zero = Render.Mosaic(_id, _opacity: 0.0, _source: source);
            using var below = Render.Mosaic(_id, _opacity: -2.0, _source: source);
            using var one = Render.Mosaic(_id, _opacity: 1.0, _source: source);
            using var above = Render.Mosaic(_id, _opacity: 7.0, _source: source);
            using var plain = Render.ToBitmap(source);
            Assert.Equal(0, Golden.MaxDiff(zero, below));
            Assert.Equal(0, Golden.MaxDiff(one, above));
            Assert.Equal(0, Golden.MaxDiff(plain, zero)); // opacity 0 is the capture itself (CT-43, CT-73, CT-75)
        }

        [Theory]
        [MemberData(nameof(EveryMosaicCommonParam))]
        public void CT120_MosaicCommonValues_AtAndPastTheirBoundaries(string _id, string _key)
        {
            var spec = CommonParams.Mosaic.Single(_s => _s.Key == _key);
            using var source = SyntheticImage.CreateImage(Width, Height);
            SKBitmap Draw(double _value) => _key == CommonParams.Blur
                ? Render.Mosaic(_id, _blur: _value, _source: source)
                : Render.Mosaic(_id, _levels: (int)_value, _source: source);
            using var min = Draw(spec.Min);
            using var below = Draw(spec.Min - 1);
            using var max = Draw(spec.Max);
            Assert.Equal(0, Golden.MaxDiff(min, below));
            if (_key == CommonParams.Levels)
            {
                // Render passes the same number to the pipeline's level filter, which clamps 2..256 too.
                using var above = Draw(spec.Max + 1);
                Assert.Equal(0, Golden.MaxDiff(max, above));
            }
            else
            {
                using var above = Draw(spec.Max + 50);
                Assert.Equal(0, Golden.MaxDiff(max, above));
            }
        }

        [Theory]
        [InlineData("pixelate")]
        [InlineData("hexcell")]
        [InlineData("blur")]
        public void CT120_MosaicCommonBlur_NotFinite_NaNIsTheDefault_InfinitiesTheEnds(string _id)
        {
            // MosaicCommon.Blur is a double: a value set in code or a future reader may be NaN or infinite.
            var spec = CommonParams.Mosaic.Single(_s => _s.Key == CommonParams.Blur);
            using var source = SyntheticImage.CreateImage(Width, Height);
            using var nan = Render.Mosaic(_id, _blur: double.NaN, _source: source);
            using var byDefault = Render.Mosaic(_id, _blur: spec.Default, _source: source);
            using var positive = Render.Mosaic(_id, _blur: double.PositiveInfinity, _source: source);
            using var max = Render.Mosaic(_id, _blur: spec.Max, _source: source);
            using var negative = Render.Mosaic(_id, _blur: double.NegativeInfinity, _source: source);
            using var min = Render.Mosaic(_id, _blur: spec.Min, _source: source);
            Assert.Equal(0, Golden.MaxDiff(byDefault, nan));
            Assert.Equal(0, Golden.MaxDiff(max, positive));
            Assert.Equal(0, Golden.MaxDiff(min, negative));
        }

        [Theory]
        [InlineData("pixelate")]
        [InlineData("hexcell")]
        [InlineData("blur")]
        public void CT120_MosaicCommonLevels_ExtremeIntegers_AreTheEnds(string _id)
        {
            // Levels is an int (a hand-edited 1e300 is read as int.MaxValue by LenientInt32Converter).
            using var source = SyntheticImage.CreateImage(Width, Height);
            using var two = Render.Mosaic(_id, _levels: 2, _source: source);
            using var lowest = Render.Mosaic(_id, _levels: int.MinValue, _source: source);
            using var all = Render.Mosaic(_id, _levels: 256, _source: source);
            using var highest = Render.Mosaic(_id, _levels: int.MaxValue, _source: source);
            Assert.Equal(0, Golden.MaxDiff(two, lowest));
            Assert.Equal(0, Golden.MaxDiff(all, highest));
        }
    }
}
