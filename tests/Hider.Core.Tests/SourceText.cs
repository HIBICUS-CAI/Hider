using System.Text;

namespace Hider.Core.Tests
{
    /// <summary>
    /// Leaves the comments out of source text, for the checks of the repository rules (CT150): every character of a comment
    /// becomes a space, line breaks stay, so line numbers and columns are kept. Strings stay (a forbidden file name or
    /// assembly name is in a string), and a comment marker inside a string is not a comment.
    /// </summary>
    internal static class SourceText
    {
        /// <summary>
        /// C#: line comments, block comments (also over several lines), outside regular, verbatim, interpolated (with code in
        /// the holes, which may hold strings and comments again) and raw strings, and character literals.
        /// </summary>
        public static string StripCSharp(string _text)
        {
            var output = new StringBuilder(_text);
            int i = 0;
            Code(_text, output, ref i, false);
            return output.ToString();
        }

        /// <summary>XML (project files, axaml): &lt;!-- --&gt; comments, also over several lines.</summary>
        public static string StripXml(string _text)
        {
            var output = new StringBuilder(_text);
            int i = 0;
            while (i < _text.Length)
            {
                if (string.CompareOrdinal(_text, i, "<!--", 0, 4) == 0)
                {
                    int end = _text.IndexOf("-->", i + 4, StringComparison.Ordinal);
                    end = end < 0 ? _text.Length : end + 3;
                    Blank(_text, output, i, end);
                    i = end;
                    continue;
                }
                i++;
            }
            return output.ToString();
        }

        private static void Blank(string _text, StringBuilder _output, int _from, int _to)
        {
            for (int k = _from; k < _to; k++)
            {
                if (_text[k] is not ('\n' or '\r'))
                {
                    _output[k] = ' ';
                }
            }
        }

        private static char At(string _text, int _i) => _i < _text.Length ? _text[_i] : '\0';

        /// <summary>Code up to the end, or (in a hole of an interpolated string) up to and past its closing brace.</summary>
        private static void Code(string _text, StringBuilder _output, ref int _i, bool _hole)
        {
            int depth = 0;
            while (_i < _text.Length)
            {
                char c = _text[_i];
                if (c == '/' && At(_text, _i + 1) == '/')
                {
                    int end = _i;
                    while (end < _text.Length && _text[end] is not ('\n' or '\r'))
                    {
                        end++;
                    }
                    Blank(_text, _output, _i, end);
                    _i = end;
                    continue;
                }
                if (c == '/' && At(_text, _i + 1) == '*')
                {
                    int end = _text.IndexOf("*/", _i + 2, StringComparison.Ordinal);
                    end = end < 0 ? _text.Length : end + 2;
                    Blank(_text, _output, _i, end);
                    _i = end;
                    continue;
                }
                if (c == '\'')
                {
                    _i++;
                    while (_i < _text.Length && _text[_i] is not ('\'' or '\n'))
                    {
                        _i += _text[_i] == '\\' ? 2 : 1;
                    }
                    _i++;
                    continue;
                }
                if ((c is '"' or '$' or '@') && TryString(_text, _output, ref _i))
                {
                    continue;
                }
                if (_hole)
                {
                    if (c == '{')
                    {
                        depth++;
                    }
                    else if (c == '}')
                    {
                        if (depth == 0)
                        {
                            _i++;
                            return;
                        }
                        depth--;
                    }
                }
                _i++;
            }
        }

        /// <summary>A string starting at <paramref name="_i"/> (with its $ / @ prefix): skips it, holes included.</summary>
        private static bool TryString(string _text, StringBuilder _output, ref int _i)
        {
            int j = _i, dollars = 0;
            bool verbatim = false;
            while (At(_text, j) is '$' or '@')
            {
                if (_text[j] == '$')
                {
                    dollars++;
                }
                else
                {
                    verbatim = true;
                }
                j++;
            }
            if (At(_text, j) != '"')
            {
                return false;
            }
            int quotes = 0;
            while (At(_text, j + quotes) == '"')
            {
                quotes++;
            }
            if (quotes >= 3 && !verbatim)
            {
                _i = j + quotes;
                Raw(_text, _output, ref _i, quotes, dollars);
                return true;
            }
            _i = j + 1;
            while (_i < _text.Length)
            {
                char c = _text[_i];
                if (verbatim && c == '"' && At(_text, _i + 1) == '"')
                {
                    _i += 2;
                }
                else if (c == '"')
                {
                    _i++;
                    return true;
                }
                else if (!verbatim && c == '\\')
                {
                    _i += 2;
                }
                else if (!verbatim && c == '\n')
                {
                    return true;
                }
                else if (dollars > 0 && (c is '{' or '}') && At(_text, _i + 1) == c)
                {
                    _i += 2;
                }
                else if (dollars > 0 && c == '{')
                {
                    _i++;
                    Code(_text, _output, ref _i, true);
                }
                else
                {
                    _i++;
                }
            }
            return true;
        }

        /// <summary>The body of a raw string of <paramref name="_quotes"/> quotes, holes opened by <paramref name="_dollars"/> braces.</summary>
        private static void Raw(string _text, StringBuilder _output, ref int _i, int _quotes, int _dollars)
        {
            while (_i < _text.Length)
            {
                if (_text[_i] == '"')
                {
                    int run = 0;
                    while (At(_text, _i + run) == '"')
                    {
                        run++;
                    }
                    _i += run;
                    if (run >= _quotes)
                    {
                        return;
                    }
                    continue;
                }
                if (_dollars > 0 && _text[_i] == '{')
                {
                    int run = 0;
                    while (At(_text, _i + run) == '{')
                    {
                        run++;
                    }
                    _i += run;
                    if (run >= _dollars)
                    {
                        Code(_text, _output, ref _i, true);
                        // The hole closes with as many braces as it opened with; the first one ended the code.
                        for (int k = 1; k < _dollars && At(_text, _i) == '}'; k++)
                        {
                            _i++;
                        }
                    }
                    continue;
                }
                _i++;
            }
        }
    }
}
