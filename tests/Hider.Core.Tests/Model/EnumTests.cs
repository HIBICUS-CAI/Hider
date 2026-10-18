using Hider.Core.Model;
using Hider.Core.Persistence;

namespace Hider.Core.Tests.Model
{
    /// <summary>PatternKind (its JSON names, spec §4.7) and ResizeHandle (its flags).</summary>
    public class EnumTests
    {
        [Fact]
        public void CT112_PatternKind_ThreeKindsInOrder()
        {
            Assert.Equal(new[] { PatternKind.Fill, PatternKind.Mosaic, PatternKind.Generated }, Enum.GetValues<PatternKind>());
            Assert.Equal(0, (int)PatternKind.Fill);
            Assert.Equal(1, (int)PatternKind.Mosaic);
            Assert.Equal(2, (int)PatternKind.Generated);
        }

        [Theory]
        [InlineData(PatternKind.Fill, "fill")]
        [InlineData(PatternKind.Mosaic, "mosaic")]
        [InlineData(PatternKind.Generated, "pattern")]
        public void CT112_PatternKind_JsonName_WrittenAndRead(PatternKind _pattern, string _name)
        {
            var state = new AppState { Areas = { new AreaState { Id = "0f3b8d2e", Pattern = _pattern } } };
            string json = AreasFile.ToJson(state);
            Assert.Contains($"\"pattern\": \"{_name}\"", json, StringComparison.Ordinal);
            var back = AreasFile.FromJson($$"""{ "version": 1, "areas": [ { "id": "0f3b8d2e", "pattern": "{{_name}}" } ] }""")!;
            Assert.Equal(_pattern, back.Areas[0].Pattern);
        }

        [Theory]
        [InlineData(0, PatternKind.Fill)]
        [InlineData(1, PatternKind.Mosaic)]
        [InlineData(2, PatternKind.Generated)]
        public void CT112_PatternKind_IntegerIsReadAsTheValue(int _value, PatternKind _expected)
        {
            var back = AreasFile.FromJson($$"""{ "version": 1, "areas": [ { "id": "0f3b8d2e", "pattern": {{_value}} } ] }""")!;
            Assert.Equal(_expected, back.Areas[0].Pattern);
        }

        [Theory]
        [InlineData("\"generated\"")] // the C# name is not the JSON name
        [InlineData("\"bogus\"")]
        public void CT112_PatternKind_UnknownName_IsMosaic(string _value)
        {
            // Proposal P-12: an unknown name is read as mosaic, like an unknown number, instead of failing the file.
            var back = AreasFile.FromJson($$"""{ "version": 1, "areas": [ { "id": "0f3b8d2e", "pattern": {{_value}} } ] }""")!;
            Assert.Equal(PatternKind.Mosaic, back.Areas[0].Pattern);
        }

        [Theory]
        [InlineData("null")]
        [InlineData("true")]
        public void CT112_PatternKind_NotAStringOrInteger_IsAJsonError(string _value)
        {
            Assert.ThrowsAny<System.Text.Json.JsonException>(() =>
                AreasFile.FromJson($$"""{ "version": 1, "areas": [ { "id": "0f3b8d2e", "pattern": {{_value}} } ] }"""));
        }

        [Fact]
        public void CT109_ResizeHandle_Values()
        {
            Assert.Equal(0, (int)ResizeHandle.None);
            Assert.Equal(1, (int)ResizeHandle.Left);
            Assert.Equal(2, (int)ResizeHandle.Top);
            Assert.Equal(4, (int)ResizeHandle.Right);
            Assert.Equal(8, (int)ResizeHandle.Bottom);
        }

        [Fact]
        public void CT109_ResizeHandle_CornersAreTheirTwoEdges()
        {
            Assert.Equal(ResizeHandle.Top | ResizeHandle.Left, ResizeHandle.TopLeft);
            Assert.Equal(ResizeHandle.Top | ResizeHandle.Right, ResizeHandle.TopRight);
            Assert.Equal(ResizeHandle.Bottom | ResizeHandle.Left, ResizeHandle.BottomLeft);
            Assert.Equal(ResizeHandle.Bottom | ResizeHandle.Right, ResizeHandle.BottomRight);
            Assert.True(ResizeHandle.TopLeft.HasFlag(ResizeHandle.Left));
            Assert.False(ResizeHandle.TopLeft.HasFlag(ResizeHandle.Right));
            Assert.Equal(9, Enum.GetValues<ResizeHandle>().Length); // the body (None), four edges, four corners
        }
    }
}
