using System.Text.Json.Serialization;

namespace Hider.Core.Persistence
{
    /// <summary>Source-generated serializer for areas.json (NFR-06: no reflection, trim safe).</summary>
    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
        Converters = new[] { typeof(LenientInt32Converter), typeof(LenientPatternKindConverter) })]
    [JsonSerializable(typeof(AppState))]
    internal sealed partial class HiderJsonContext : JsonSerializerContext
    {
    }
}
