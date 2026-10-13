using System.Text;

namespace Hider.Core
{
    /// <summary>Text typed into number boxes. The UI is Japanese (FR-70), so an IME may give full-width characters.</summary>
    public static class NumberText
    {
        /// <summary>Full-width digits, point, signs and spaces as their ASCII forms; everything else unchanged.</summary>
        public static string Normalize(string? _text)
        {
            if (string.IsNullOrEmpty(_text))
            {
                return string.Empty;
            }
            var result = new StringBuilder(_text.Length);
            foreach (char c in _text)
            {
                result.Append(c switch
                {
                    >= '０' and <= '９' => (char)('0' + (c - '０')),
                    '．' or '。' => '.',
                    '－' or 'ー' or '−' => '-',
                    '＋' => '+',
                    '　' => ' ',
                    _ => c,
                });
            }
            return result.ToString().Trim();
        }
    }
}
