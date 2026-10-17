namespace Hider.Core.Tests
{
    public class NumberTextTests
    {
        [Theory]
        [InlineData("２０", "20")]
        [InlineData("　－１．５　", "-1.5")]
        [InlineData("１２３４５６７８９０", "1234567890")]
        [InlineData("12", "12")]
        [InlineData(null, "")]
        public void Normalize_FullWidthToAscii(string? _text, string _expected)
        {
            Assert.Equal(_expected, NumberText.Normalize(_text));
        }

        [Theory]
        [InlineData('０', '0')]
        [InlineData('１', '1')]
        [InlineData('２', '2')]
        [InlineData('３', '3')]
        [InlineData('４', '4')]
        [InlineData('５', '5')]
        [InlineData('６', '6')]
        [InlineData('７', '7')]
        [InlineData('８', '8')]
        [InlineData('９', '9')]
        [InlineData('．', '.')] // full-width full stop
        [InlineData('。', '.')] // ideographic full stop (an IME's "." in Japanese mode)
        [InlineData('－', '-')] // full-width hyphen-minus
        [InlineData('ー', '-')] // prolonged sound mark (an IME's "-" in Japanese mode)
        [InlineData('−', '-')] // minus sign
        [InlineData('＋', '+')]
        public void CT135_Normalize_EachMappedCharacter(char _from, char _to)
        {
            Assert.Equal(_to.ToString(), NumberText.Normalize("" + _from));
            Assert.Equal("1" + _to + "2", NumberText.Normalize("1" + _from + "2"));
        }

        [Fact]
        public void CT135_Normalize_IdeographicSpace_IsASpace_AndTrimmed()
        {
            Assert.Equal("1 2", NumberText.Normalize("1　2"));
            Assert.Equal("", NumberText.Normalize("　"));
            Assert.Equal("3", NumberText.Normalize("　 3 　"));
        }

        [Theory]
        [InlineData("", "")]
        [InlineData("   ", "")]
        [InlineData("abc", "abc")]
        [InlineData("ａｂｃ", "ａｂｃ")] // full-width letters are not part of a number: unchanged
        [InlineData("1,000", "1,000")]
        [InlineData("１，０００", "1，000")] // the full-width comma is not mapped
        [InlineData("\t5\n", "5")]
        [InlineData("/", "/")] // just below '0'
        [InlineData(":", ":")] // just above '9'
        [InlineData("／", "／")] // just below '０'
        [InlineData("：", "：")] // just above '９'
        public void CT135_Normalize_OtherCharactersUnchanged_Trimmed(string _text, string _expected)
        {
            Assert.Equal(_expected, NumberText.Normalize(_text));
        }

        [Fact]
        public void CT135_Normalize_MixedWidths()
        {
            Assert.Equal("+12.5", NumberText.Normalize("＋1２。5"));
            Assert.Equal("-0.25", NumberText.Normalize("ー０．２５"));
            Assert.True(double.TryParse(NumberText.Normalize("−３．５"), System.Globalization.CultureInfo.InvariantCulture, out double value));
            Assert.Equal(-3.5, value);
        }
    }
}
