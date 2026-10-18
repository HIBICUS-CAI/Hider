using System.Text.Json;
using System.Text.Json.Serialization;
using Hider.Core.Model;

namespace Hider.Core.Persistence
{
    /// <summary>
    /// Reads "pattern" without failing the whole file (proposal P-12): the spec's names ("fill" | "mosaic" | "pattern",
    /// §4.7) in any case and with surrounding spaces, an integer as its enum value (Normalize turns an undefined one into
    /// mosaic), and any other string, such as a pattern of a newer version, as mosaic, which keeps the content covered.
    /// Other tokens (null, true, objects) are not a pattern and still make the file broken (FR-64). Writes the spec's name.
    /// </summary>
    internal sealed class LenientPatternKindConverter : JsonConverter<PatternKind>
    {
        public override PatternKind Read(ref Utf8JsonReader _reader, Type _typeToConvert, JsonSerializerOptions _options)
        {
            switch (_reader.TokenType)
            {
                case JsonTokenType.String:
                    return FromName(_reader.GetString()!);
                case JsonTokenType.Number:
                    if (!_reader.TryGetInt32(out int value))
                    {
                        throw new JsonException("pattern number out of range");
                    }
                    return (PatternKind)value;
                default:
                    throw new JsonException($"expected a pattern name, got {_reader.TokenType}");
            }
        }

        public override void Write(Utf8JsonWriter _writer, PatternKind _value, JsonSerializerOptions _options)
        {
            _writer.WriteStringValue(ToName(_value));
        }

        /// <summary>The pattern a name in the file stands for; unknown names are mosaic.</summary>
        public static PatternKind FromName(string _name)
        {
            string name = _name.Trim();
            foreach (var kind in Enum.GetValues<PatternKind>())
            {
                if (string.Equals(name, ToName(kind), StringComparison.OrdinalIgnoreCase))
                {
                    return kind;
                }
            }
            return PatternKind.Mosaic;
        }

        /// <summary>The spec's name of a pattern (§4.7); an undefined value is written as mosaic.</summary>
        public static string ToName(PatternKind _kind) => _kind switch
        {
            PatternKind.Fill => "fill",
            PatternKind.Generated => "pattern",
            _ => "mosaic",
        };
    }
}
