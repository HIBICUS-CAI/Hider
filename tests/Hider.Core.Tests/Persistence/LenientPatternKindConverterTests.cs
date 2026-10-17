using System.Text;
using System.Text.Json;
using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Persistence
{
    /// <summary>LenientPatternKindConverter on its own (proposal P-12): names, integers, other tokens and writing.</summary>
    public class LenientPatternKindConverterTests
    {
        private static PatternKind Read(string _json)
        {
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(_json));
            reader.Read();
            return new LenientPatternKindConverter().Read(ref reader, typeof(PatternKind), new JsonSerializerOptions());
        }

        private static string Write(PatternKind _value)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                new LenientPatternKindConverter().Write(writer, _value, new JsonSerializerOptions());
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        [Theory]
        [InlineData("fill", PatternKind.Fill)]
        [InlineData("mosaic", PatternKind.Mosaic)]
        [InlineData("pattern", PatternKind.Generated)]
        [InlineData("Fill", PatternKind.Fill)]
        [InlineData("pAtTeRn", PatternKind.Generated)]
        [InlineData("  fill\t", PatternKind.Fill)]
        [InlineData("\nmosaic\n", PatternKind.Mosaic)]
        [InlineData("generated", PatternKind.Mosaic)]
        [InlineData("image", PatternKind.Mosaic)]
        [InlineData("", PatternKind.Mosaic)]
        [InlineData(" ", PatternKind.Mosaic)]
        [InlineData("ｆｉｌｌ", PatternKind.Mosaic)] // full-width letters are another name
        public void FromName_SpecNamesInAnyCase_OthersAreMosaic(string _name, PatternKind _expected)
        {
            Assert.Equal(_expected, LenientPatternKindConverter.FromName(_name));
        }

        [Theory]
        [InlineData(PatternKind.Fill, "fill")]
        [InlineData(PatternKind.Mosaic, "mosaic")]
        [InlineData(PatternKind.Generated, "pattern")]
        [InlineData((PatternKind)7, "mosaic")]
        [InlineData((PatternKind)(-1), "mosaic")]
        public void ToName_IsTheSpecsName_UndefinedIsMosaic(PatternKind _kind, string _expected)
        {
            Assert.Equal(_expected, LenientPatternKindConverter.ToName(_kind));
        }

        [Fact]
        public void Names_RoundTripForEveryPattern()
        {
            foreach (var kind in Enum.GetValues<PatternKind>())
            {
                Assert.Equal(kind, LenientPatternKindConverter.FromName(LenientPatternKindConverter.ToName(kind)));
            }
        }

        [Theory]
        [InlineData("\"fill\"", PatternKind.Fill)]
        [InlineData("\"FILL\"", PatternKind.Fill)]
        [InlineData("\"unknown\"", PatternKind.Mosaic)]
        [InlineData("0", PatternKind.Fill)]
        [InlineData("1", PatternKind.Mosaic)]
        [InlineData("2", PatternKind.Generated)]
        [InlineData("7", (PatternKind)7)]      // Normalize turns it into mosaic
        [InlineData("-3", (PatternKind)(-3))]
        public void Read_StringsAndIntegers(string _json, PatternKind _expected)
        {
            Assert.Equal(_expected, Read(_json));
        }

        [Theory]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("{}")]
        [InlineData("[]")]
        [InlineData("1.5")]
        [InlineData("1e3")]
        [InlineData("4294967296")]
        public void Read_OtherTokens_Throw(string _json)
        {
            Assert.Throws<JsonException>(() => Read(_json));
        }

        [Theory]
        [InlineData(PatternKind.Fill, "\"fill\"")]
        [InlineData(PatternKind.Mosaic, "\"mosaic\"")]
        [InlineData(PatternKind.Generated, "\"pattern\"")]
        [InlineData((PatternKind)9, "\"mosaic\"")]
        public void Write_TheName(PatternKind _value, string _expected)
        {
            Assert.Equal(_expected, Write(_value));
        }

        [Fact]
        public void Context_UsesThisConverter_ForThePatternOfAnArea()
        {
            // The context's converter list wins over the enum's own [JsonConverter] attribute.
            var state = JsonSerializer.Deserialize("""{ "version": 1, "areas": [ { "id": "0f3b8d2e", "pattern": "IMAGE" } ] }""", HiderJsonContext.Default.AppState)!;
            Assert.Equal(PatternKind.Mosaic, state.Areas[0].Pattern);
        }
    }
}
