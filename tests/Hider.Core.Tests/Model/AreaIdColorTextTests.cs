using Hider.Core.Model;

namespace Hider.Core.Tests.Model
{
    public class AreaIdColorTextTests
    {
        [Theory]
        [InlineData("0f3b8d2e", true)]
        [InlineData("00000000", true)]
        [InlineData("ffffffff", true)]
        [InlineData("12345678", true)]
        [InlineData("abcdef01", true)]
        [InlineData("0F3B8D2E", false)] // upper case is not an id we write
        [InlineData("0f3b8d2g", false)]
        [InlineData("0f3b8d2", false)]
        [InlineData("0f3b8d2e0", false)]
        [InlineData("", false)]
        [InlineData("0f3b-d2e", false)]
        [InlineData("０f3b8d2e", false)] // a full-width digit is not ASCII
        [InlineData(null, false)]
        public void AreaId_IsValid_EightLowerCaseHexDigits(string? _id, bool _valid)
        {
            Assert.Equal(_valid, AreaId.IsValid(_id));
        }

        [Fact]
        public void AreaId_New_WithoutRandom_UsesTheSharedOne()
        {
            var ids = new HashSet<string>();
            for (int i = 0; i < 100; i++)
            {
                string id = AreaId.New(ids.Contains);
                Assert.True(AreaId.IsValid(id), id);
                Assert.True(ids.Add(id));
            }
        }

        [Fact]
        public void AreaId_New_FreeAtOnce_TakesTheFirstDraw()
        {
            var checkedIds = new List<string>();
            string id = AreaId.New(_id =>
            {
                checkedIds.Add(_id);
                return false;
            }, new Random(42));
            Assert.Equal(id, Assert.Single(checkedIds));
            Assert.Equal(((uint)new Random(42).NextInt64(0, 1L << 32)).ToString("x8"), id);
        }

        [Fact]
        public void AreaId_New_PadsSmallNumbersToEightDigits()
        {
            // A source that always draws 0x1f: the id keeps its leading zeros.
            Assert.Equal("0000001f", AreaId.New(_ => false, new FixedRandom(0x1f)));
        }

        [Theory]
        [InlineData("#68768A", true)]
        [InlineData("#68768a", true)]
        [InlineData("#000000", true)]
        [InlineData("#FFFFFF", true)]
        [InlineData("68768A", false)]
        [InlineData("#68768", false)]
        [InlineData("#68768AA", false)]
        [InlineData("#68768G", false)]
        [InlineData("#G8768A", false)]
        [InlineData("$68768A", false)]
        [InlineData("#６8768A", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ColorText_IsValid_HashAndSixHexDigits(string? _text, bool _valid)
        {
            Assert.Equal(_valid, ColorText.IsValid(_text));
        }

        [Theory]
        [InlineData("#abcdef", "#ABCDEF")]
        [InlineData("#ABCDEF", "#ABCDEF")]
        [InlineData("#aBc123", "#ABC123")]
        public void ColorText_OrDefault_ValidIsUpperCased(string _text, string _expected)
        {
            Assert.Equal(_expected, ColorText.OrDefault(_text));
            Assert.Equal(_expected, ColorText.OrDefault(_text, "#000000"));
        }

        [Fact]
        public void ColorText_OrDefault_InvalidGivesTheFallback()
        {
            Assert.Equal("#68768A", ColorText.OrDefault("red"));
            Assert.Equal("#010203", ColorText.OrDefault("red", "#010203"));
            Assert.Equal("anything", ColorText.OrDefault(null, "anything")); // the fallback is returned as given
        }

        [Fact]
        public void ColorText_Parse_ComponentsInOrder()
        {
            Assert.Equal(((byte)0x12, (byte)0x34, (byte)0x56), ColorText.Parse("#123456"));
            Assert.Equal(((byte)0, (byte)0, (byte)0), ColorText.Parse("#000000"));
            Assert.Equal(((byte)255, (byte)255, (byte)255), ColorText.Parse("#ffffff"));
        }

        [Fact]
        public void ColorText_Parse_InvalidIsTheDefaultColor()
        {
            var expected = ((byte)0x68, (byte)0x76, (byte)0x8A);
            Assert.Equal(expected, ColorText.Parse("nope"));
            Assert.Equal(expected, ColorText.Parse(""));
            Assert.Equal(expected, ColorText.Parse(null!));
        }

        /// <summary>A random source whose 64-bit draws are always the same value.</summary>
        private sealed class FixedRandom(long _value) : Random
        {
            public override long NextInt64(long _minValue, long _maxValue)
            {
                return _value;
            }
        }
    }
}
