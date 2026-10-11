using Avalonia.Media;

namespace Hider
{
    /// <summary>Fonts shared by the UI (code and axaml through x:Static).</summary>
    public static class UiFonts
    {
        /// <summary>Numbers and color codes (UI canvas: monospaced): Windows, then Linux fallbacks.</summary>
        public static readonly FontFamily Mono = new("Cascadia Mono, Consolas, DejaVu Sans Mono, monospace");
    }
}
