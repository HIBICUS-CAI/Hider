using System.Globalization;
using Hider.Core.Model;
using Hider.Core.Mosaic;

namespace Hider.Core.Tests.Mosaic
{
    /// <summary>Every member and branch of <see cref="ParamValues"/>.</summary>
    public class ParamValuesTests
    {
        private static readonly ParamSpec size = new("size", "param.block", ParamUnit.Px, 2, 100, 2, 50, 10, LerpWithOpacity: true, LerpFloor: 1);
        private static readonly ParamSpec plain = new("plain", "param.gap", ParamUnit.Px, 0, 8, 0, 8, 3);

        private static ParamValues Values(Dictionary<string, double>? _stored, double _opacity = 1.0)
        {
            return new ParamValues(new[] { size, plain }, _stored, _opacity);
        }

        [Fact]
        public void NullStoredValues_EverySpecTakesItsDefault()
        {
            var p = Values(null);
            Assert.Equal(10, p.Raw("size"));
            Assert.Equal(3, p.Raw("plain"));
        }

        [Theory]
        [InlineData(0.3, 0.3)]
        [InlineData(-0.5, 0.0)]
        [InlineData(1.5, 1.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(1.0, 1.0)]
        public void Opacity_IsClampedTo0And1(double _opacity, double _expected)
        {
            Assert.Equal(_expected, Values(null, _opacity).Opacity);
        }

        [Fact]
        public void LaterSpecWithTheSameKey_Wins()
        {
            var p = new ParamValues(new[] { plain, plain with { Default = 7 } }, null, 1.0);
            Assert.Equal(7, p.Raw("plain"));
        }

        [Theory]
        [InlineData("size", 40.0, 40.0)]      // stored, known: as stored
        [InlineData("size", 1000.0, 100.0)]   // stored, known: clamped to the spec
        [InlineData("size", -3.0, 2.0)]
        [InlineData("size", double.NaN, 10.0)] // stored NaN: the default
        [InlineData("extra", -5.0, -5.0)]     // stored, no spec: as stored, unclamped
        [InlineData("extra", 1e9, 1e9)]
        public void Raw_StoredValues(string _key, double _stored, double _expected)
        {
            var p = Values(new Dictionary<string, double> { [_key] = _stored });
            Assert.Equal(_expected, p.Raw(_key));
        }

        [Fact]
        public void Raw_NotStored_DefaultOrZero()
        {
            var p = Values(new Dictionary<string, double>());
            Assert.Equal(10, p.Raw("size"));
            Assert.Equal(0, p.Raw("nothing"));
        }

        [Fact]
        public void Effective_LerpsOnlySpecsThatAskForIt()
        {
            var p = Values(new Dictionary<string, double> { ["size"] = 21, ["plain"] = 5, ["extra"] = 9 }, 0.5);
            Assert.Equal(11, p.Effective("size"), 9);   // 1 + (21 - 1) * 0.5
            Assert.Equal(5, p.Effective("plain"));      // no lerp
            Assert.Equal(9, p.Effective("extra"));      // no spec: the raw value
            Assert.Equal(0, p.Effective("nothing"));
            Assert.Equal(1, Values(new Dictionary<string, double> { ["size"] = 21 }, 0.0).Effective("size"), 9);
        }

        [Fact]
        public void Fingerprint_ResolvedValuesInOrdinalKeyOrder_ThenTheOpacity()
        {
            // Specs and stored keys together, ordinal order ("Z" before "a"), the resolved (clamped) values, then a=opacity.
            var p = Values(new Dictionary<string, double> { ["size"] = 1000, ["Zeta"] = 0.1, ["alpha"] = -2.5 }, 0.25);
            Assert.Equal("Zeta=0.1;alpha=-2.5;plain=3;size=100;a=0.25", p.Fingerprint());
        }

        [Fact]
        public void Fingerprint_IsBuiltOnce()
        {
            var p = Values(null);
            Assert.Same(p.Fingerprint(), p.Fingerprint());
        }

        [Fact]
        public void Fingerprint_EqualForTheSameResolvedValues_DifferentOtherwise()
        {
            // Clamping makes 1000 and 100 the same drawing, so the same cache key.
            Assert.Equal(Values(new() { ["size"] = 1000 }).Fingerprint(), Values(new() { ["size"] = 100 }).Fingerprint());
            Assert.Equal(Values(null).Fingerprint(), Values(new() { ["size"] = 10 }).Fingerprint());
            Assert.NotEqual(Values(new() { ["size"] = 11 }).Fingerprint(), Values(null).Fingerprint());
            Assert.NotEqual(Values(null, 0.5).Fingerprint(), Values(null, 1.0).Fingerprint());
            Assert.NotEqual(Values(new() { ["extra"] = 0 }).Fingerprint(), Values(null).Fingerprint());
        }

        [Fact]
        public void Fingerprint_RoundTripFormat_IndependentOfTheCulture()
        {
            var previous = CultureInfo.CurrentCulture;
            try
            {
                // The build runs in invariant globalization mode: a culture with a decimal comma and another minus sign.
                var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                culture.NumberFormat.NumberDecimalSeparator = ",";
                culture.NumberFormat.NegativeSign = "~";
                CultureInfo.CurrentCulture = culture;
                Assert.Equal("~0,5", (-0.5).ToString(CultureInfo.CurrentCulture));
                var p = Values(new() { ["extra"] = 0.1 + 0.2, ["minus"] = -1.25 }, 0.5);
                Assert.Equal("extra=0.30000000000000004;minus=-1.25;plain=3;size=10;a=0.5", p.Fingerprint());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        [Fact]
        public void ForMosaic_MethodParamsCommonValuesAndOpacity()
        {
            var method = MethodRegistry.Find("pixelate")!;
            var settings = new MethodSettings<MosaicCommon> { Method = "pixelate" };
            settings.ParamsFor("pixelate")["block"] = 20;
            settings.ParamsFor("hexcell")["radius"] = 99; // another method's values are not used
            settings.Common.Blur = 3;
            settings.Common.Levels = 16;
            settings.Common.IntervalMs = 5000;
            settings.Common.StripPx = 4;
            var p = ParamValues.ForMosaic(method, settings, 0.75);
            Assert.Equal(20, p.Raw("block"));
            Assert.Equal(0, p.Raw("radius"));
            Assert.Equal(3, p.Raw(CommonParams.Blur));
            Assert.Equal(16, p.Raw(CommonParams.Levels));
            Assert.Equal(5000, p.Raw(CommonParams.IntervalMs));
            Assert.Equal(4, p.Raw(CommonParams.StripPx));
            Assert.Equal(0.75, p.Opacity);
            // The common specs are known: blur fades with the opacity (floor 0), levels are clamped.
            Assert.Equal(2.25, p.Effective(CommonParams.Blur), 9);
            settings.Common.Levels = 1;
            Assert.Equal(2, ParamValues.ForMosaic(method, settings, 1.0).Raw(CommonParams.Levels));
        }

        [Fact]
        public void ForMosaic_CommonValuesWinOverAStoredParamOfTheSameKey()
        {
            var method = MethodRegistry.Find("pixelate")!;
            var settings = new MethodSettings<MosaicCommon>();
            settings.ParamsFor("pixelate")[CommonParams.Blur] = 50;
            settings.Common.Blur = 6;
            Assert.Equal(6, ParamValues.ForMosaic(method, settings, 1.0).Raw(CommonParams.Blur));
        }

        [Fact]
        public void ForMosaic_NoStoredParams_DefaultsAndTheSettingsAreNotChanged()
        {
            var method = MethodRegistry.Find("hexcell")!;
            var settings = new MethodSettings<MosaicCommon>();
            var p = ParamValues.ForMosaic(method, settings, 1.0);
            Assert.Equal(10, p.Raw("radius"));
            Assert.Empty(settings.Params); // nothing was added to the area's settings
        }

        [Fact]
        public void ForMosaic_CopiesTheStoredValues()
        {
            var method = MethodRegistry.Find("pixelate")!;
            var settings = new MethodSettings<MosaicCommon>();
            settings.ParamsFor("pixelate")["block"] = 20;
            var p = ParamValues.ForMosaic(method, settings, 1.0);
            settings.ParamsFor("pixelate")["block"] = 30;
            Assert.Equal(20, p.Raw("block"));
            Assert.False(settings.Params["pixelate"].ContainsKey(CommonParams.Blur)); // the common values went into the copy only
        }

        [Fact]
        public void ForGenerated_SeedAndBaseColorAsFractions_OpacityOne()
        {
            var method = MethodRegistry.Find("block")!;
            var settings = new MethodSettings<GeneratedCommon> { Method = "block" };
            settings.ParamsFor("block")["cellSize"] = 30;
            settings.ParamsFor("noise")["scale"] = 9;
            settings.Common.Seed = 4321;
            settings.Common.BaseColor = "#FF8000";
            var p = ParamValues.ForGenerated(method, settings);
            Assert.Equal(30, p.Raw("cellSize"));
            Assert.Equal(0, p.Raw("scale"));
            Assert.Equal(4321, p.Raw(CommonParams.Seed));
            Assert.Equal(1.0, p.Raw(CommonParams.BaseColorR));
            Assert.Equal(128 / 255.0, p.Raw(CommonParams.BaseColorG));
            Assert.Equal(0.0, p.Raw(CommonParams.BaseColorB));
            Assert.Equal(1.0, p.Opacity);
            Assert.False(settings.Params["block"].ContainsKey(CommonParams.Seed));
        }

        [Fact]
        public void ForGenerated_CommonValuesHaveNoSpec_KeptAsStored()
        {
            // The seed is not clamped here: LatticeHash.SeedDigits wraps it. An invalid color is the default fill color.
            var method = MethodRegistry.Find("noise")!;
            var settings = new MethodSettings<GeneratedCommon>();
            settings.Common.Seed = 70000;
            settings.Common.BaseColor = "red";
            var p = ParamValues.ForGenerated(method, settings);
            Assert.Equal(70000, p.Raw(CommonParams.Seed));
            Assert.Equal(70000, p.Effective(CommonParams.Seed));
            Assert.Equal(0x68 / 255.0, p.Raw(CommonParams.BaseColorR));
            Assert.Equal(0x76 / 255.0, p.Raw(CommonParams.BaseColorG));
            Assert.Equal(0x8A / 255.0, p.Raw(CommonParams.BaseColorB));
            Assert.Equal(4, p.Raw("scale"));
            Assert.Equal(6, p.Raw("levels"));
        }
    }
}
