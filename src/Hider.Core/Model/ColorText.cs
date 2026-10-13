namespace Hider.Core.Model
{
    /// <summary>"#RRGGBB" color strings as stored in areas.json.</summary>
    public static class ColorText
    {
        public static bool IsValid(string? _text)
        {
            if (_text is null || _text.Length != 7 || _text[0] != '#')
            {
                return false;
            }
            for (int i = 1; i < 7; i++)
            {
                if (!char.IsAsciiHexDigit(_text[i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>The color in upper case, or <paramref name="_fallback"/> when it is not "#RRGGBB".</summary>
        public static string OrDefault(string? _text, string _fallback = AreaDefaults.FillColor)
        {
            return IsValid(_text) ? _text!.ToUpperInvariant() : _fallback;
        }

        /// <summary>Red, green and blue in 0..255.</summary>
        public static (byte R, byte G, byte B) Parse(string _text)
        {
            string color = OrDefault(_text);
            return (Convert.ToByte(color.Substring(1, 2), 16), Convert.ToByte(color.Substring(3, 2), 16), Convert.ToByte(color.Substring(5, 2), 16));
        }
    }
}
