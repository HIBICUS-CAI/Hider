using System.Text.Json.Serialization;

namespace Hider.Core.Model
{
    /// <summary>How an area is drawn. JSON values follow the spec §4.7 ("fill" | "mosaic" | "pattern").</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<PatternKind>))]
    public enum PatternKind
    {
        [JsonStringEnumMemberName("fill")]
        Fill,

        [JsonStringEnumMemberName("mosaic")]
        Mosaic,

        [JsonStringEnumMemberName("pattern")]
        Generated,
    }
}
