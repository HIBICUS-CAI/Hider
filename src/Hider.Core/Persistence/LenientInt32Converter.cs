using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hider.Core.Persistence
{
    /// <summary>
    /// Reads an integer field written as any JSON number ("levels": 16.0, "x": 12.5 in a hand-edited file): rounded and
    /// clamped to the int range instead of failing the whole file. Range checks of each field are Normalize's.
    /// </summary>
    internal sealed class LenientInt32Converter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader _reader, Type _typeToConvert, JsonSerializerOptions _options)
        {
            if (_reader.TokenType != JsonTokenType.Number)
            {
                throw new JsonException($"expected a number, got {_reader.TokenType}");
            }
            if (_reader.TryGetInt32(out int value))
            {
                return value;
            }
            if (!_reader.TryGetDouble(out double d) || !double.IsFinite(d))
            {
                throw new JsonException("number out of range");
            }
            return (int)Math.Clamp(Math.Round(d, MidpointRounding.AwayFromZero), int.MinValue, int.MaxValue);
        }

        public override void Write(Utf8JsonWriter _writer, int _value, JsonSerializerOptions _options)
        {
            _writer.WriteNumberValue(_value);
        }
    }
}
