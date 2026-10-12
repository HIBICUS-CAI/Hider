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
    }
}
