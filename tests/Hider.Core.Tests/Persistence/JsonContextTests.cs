using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    /// <summary>HiderJsonContext (NFR-06: source generated) and LenientInt32Converter.</summary>
    public class JsonContextTests
    {
        private static readonly string[] settingsKeys = { "method", "common", "params" };
        private static readonly string[] mosaicCommonKeys = { "blur", "levels", "intervalMs", "stripPx" };
        private static readonly string[] generatedCommonKeys = { "baseColor", "seed" };
        private static readonly string[] fillKeys = { "color" };

        private static JsonTypeInfo<T> Info<T>()
        {
            return (JsonTypeInfo<T>)HiderJsonContext.Default.GetTypeInfo(typeof(T))!;
        }

        private static T RoundTrip<T>(T _value)
        {
            string json = JsonSerializer.Serialize(_value, Info<T>());
            return JsonSerializer.Deserialize(json, Info<T>())!;
        }

        [Fact]
        public void Options_CamelCase_Indented_LenientIntegers()
        {
            var options = HiderJsonContext.Default.Options;
            Assert.Same(JsonNamingPolicy.CamelCase, options.PropertyNamingPolicy);
            Assert.True(options.WriteIndented);
            Assert.Contains(options.Converters, _c => _c is LenientInt32Converter);
            Assert.False(options.PropertyNameCaseInsensitive);
        }

        [Fact]
        public void EveryStoredType_HasGeneratedMetadata()
        {
            foreach (var type in new[]
                     {
                         typeof(AppState), typeof(AreaState), typeof(List<AreaState>), typeof(PatternKind), typeof(FillSettings),
                         typeof(MethodSettings<MosaicCommon>), typeof(MethodSettings<GeneratedCommon>), typeof(MosaicCommon), typeof(GeneratedCommon),
                         typeof(Dictionary<string, Dictionary<string, double>>), typeof(Dictionary<string, double>),
                         typeof(int), typeof(double), typeof(string),
                     })
            {
                Assert.NotNull(HiderJsonContext.Default.GetTypeInfo(type));
            }
        }

        [Fact]
        public void AppState_RoundTrip_EveryField()
        {
            var area = new AreaState
            {
                Id = "0f3b8d2e", X = -1200, Y = 340, Width = 667.33, Height = 450.5, Opacity = 0.25, Pattern = PatternKind.Fill,
            };
            area.Fill.Color = "#2B2F36";
            area.Mosaic.Method = "hexcell";
            area.Mosaic.Common.Blur = 3.5;
            area.Mosaic.Common.Levels = 16;
            area.Mosaic.Common.IntervalMs = 250;
            area.Mosaic.Common.StripPx = 4;
            area.Mosaic.ParamsFor("hexcell")["radius"] = 22;
            area.Mosaic.ParamsFor("future")["k"] = -0.125;
            area.Generated.Method = "noise";
            area.Generated.Common.BaseColor = "#010203";
            area.Generated.Common.Seed = 4321;
            area.Generated.ParamsFor("noise")["scale"] = 9;
            var state = new AppState { Areas = { area, new AreaState { Id = "1a2b3c4d" } } };

            var back = RoundTrip(state);

            Assert.Equal(1, back.Version);
            Assert.Equal(2, back.Areas.Count);
            var a = back.Areas[0];
            Assert.Equal(("0f3b8d2e", -1200, 340, 667.33, 450.5, 0.25, PatternKind.Fill), (a.Id, a.X, a.Y, a.Width, a.Height, a.Opacity, a.Pattern));
            Assert.Equal("#2B2F36", a.Fill.Color);
            Assert.Equal("hexcell", a.Mosaic.Method);
            Assert.Equal((3.5, 16, 250, 4), (a.Mosaic.Common.Blur, a.Mosaic.Common.Levels, a.Mosaic.Common.IntervalMs, a.Mosaic.Common.StripPx));
            Assert.Equal(22, a.Mosaic.Params["hexcell"]["radius"]);
            Assert.Equal(-0.125, a.Mosaic.Params["future"]["k"]);
            Assert.Equal("noise", a.Generated.Method);
            Assert.Equal(("#010203", 4321), (a.Generated.Common.BaseColor, a.Generated.Common.Seed));
            Assert.Equal(9, a.Generated.Params["noise"]["scale"]);
            Assert.Equal("1a2b3c4d", back.Areas[1].Id);
            Assert.Equal(AreasFile.ToJson(state), AreasFile.ToJson(back));
        }

        [Fact]
        public void EachType_RoundTripsOnItsOwn()
        {
            Assert.Equal("#ABCDEF", RoundTrip(new FillSettings { Color = "#ABCDEF" }).Color);
            var mosaic = RoundTrip(new MosaicCommon { Blur = 1.5, Levels = 3, IntervalMs = 7, StripPx = 9 });
            Assert.Equal((1.5, 3, 7, 9), (mosaic.Blur, mosaic.Levels, mosaic.IntervalMs, mosaic.StripPx));
            var generated = RoundTrip(new GeneratedCommon { BaseColor = "#000001", Seed = 2 });
            Assert.Equal(("#000001", 2), (generated.BaseColor, generated.Seed));
            var settings = RoundTrip(new MethodSettings<GeneratedCommon> { Method = "block", Params = new() { ["block"] = new() { ["gap"] = 3 } } });
            Assert.Equal("block", settings.Method);
            Assert.Equal(3, settings.Params["block"]["gap"]);
            Assert.Equal(PatternKind.Generated, RoundTrip(PatternKind.Generated));
            var area = RoundTrip(new AreaState { Id = "deadbeef", X = 5 });
            Assert.Equal(("deadbeef", 5), (area.Id, area.X));
            Assert.Equal(-17, RoundTrip(-17));
        }

        [Fact]
        public void PropertyNames_AreCamelCase()
        {
            var area = new AreaState { Id = "0f3b8d2e" };
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(area, Info<AreaState>()));
            var mosaic = doc.RootElement.GetProperty("mosaic");
            Assert.Equal(settingsKeys, mosaic.EnumerateObject().Select(_p => _p.Name));
            Assert.Equal(mosaicCommonKeys, mosaic.GetProperty("common").EnumerateObject().Select(_p => _p.Name));
            Assert.Equal(generatedCommonKeys, doc.RootElement.GetProperty("generated").GetProperty("common").EnumerateObject().Select(_p => _p.Name));
            Assert.Equal(fillKeys, doc.RootElement.GetProperty("fill").EnumerateObject().Select(_p => _p.Name));
        }

        [Fact]
        public void DictionaryKeys_AreWrittenAsGiven()
        {
            // The naming policy applies to properties, not to method ids or parameter keys.
            var settings = new MethodSettings<MosaicCommon>();
            settings.ParamsFor("MyMethod")["SomeKey"] = 1;
            string json = JsonSerializer.Serialize(settings, Info<MethodSettings<MosaicCommon>>());
            Assert.Contains("\"MyMethod\"", json, StringComparison.Ordinal);
            Assert.Contains("\"SomeKey\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void IntegerFields_UseTheLenientConverter()
        {
            var common = JsonSerializer.Deserialize("""{ "levels": 15.5, "intervalMs": -0.5, "stripPx": 1e3 }""", Info<MosaicCommon>())!;
            Assert.Equal((16, -1, 1000), (common.Levels, common.IntervalMs, common.StripPx));
        }

        private static int ReadInt(string _json)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(_json));
            Assert.True(reader.Read());
            return new LenientInt32Converter().Read(ref reader, typeof(int), HiderJsonContext.Default.Options);
        }

        [Theory]
        [InlineData("0", 0)]
        [InlineData("12", 12)]
        [InlineData("-7", -7)]
        [InlineData("2147483647", int.MaxValue)]
        [InlineData("-2147483648", int.MinValue)]
        [InlineData("16.0", 16)]
        [InlineData("12.4", 12)]
        [InlineData("12.5", 13)]
        [InlineData("-12.5", -13)]
        [InlineData("0.5", 1)]
        [InlineData("-0.4", 0)]
        [InlineData("1e3", 1000)]
        [InlineData("2147483648", int.MaxValue)]
        [InlineData("-2147483649", int.MinValue)]
        [InlineData("1e300", int.MaxValue)]
        [InlineData("-1e300", int.MinValue)]
        public void LenientInt32_AnyFiniteNumber_RoundedAwayFromZeroAndClamped(string _json, int _expected)
        {
            Assert.Equal(_expected, ReadInt(_json));
        }

        [Theory]
        [InlineData("\"12\"", "String")]
        [InlineData("true", "True")]
        [InlineData("false", "False")]
        [InlineData("null", "Null")]
        [InlineData("[1]", "StartArray")]
        [InlineData("{}", "StartObject")]
        public void LenientInt32_NotANumber_Throws(string _json, string _tokenType)
        {
            var e = Assert.Throws<JsonException>(() => ReadInt(_json));
            Assert.Equal($"expected a number, got {_tokenType}", e.Message);
        }

        [Theory]
        [InlineData("1e400")]
        [InlineData("-1e400")]
        public void LenientInt32_BeyondDouble_Throws(string _json)
        {
            var e = Assert.Throws<JsonException>(() => ReadInt(_json));
            Assert.Equal("number out of range", e.Message);
        }

        [Theory]
        [InlineData(0, "0")]
        [InlineData(-42, "-42")]
        [InlineData(int.MaxValue, "2147483647")]
        [InlineData(int.MinValue, "-2147483648")]
        public void LenientInt32_WritesAPlainInteger(int _value, string _expected)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                new LenientInt32Converter().Write(writer, _value, HiderJsonContext.Default.Options);
            }
            Assert.Equal(_expected, Encoding.UTF8.GetString(buffer.WrittenSpan));
        }
    }
}
