using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace RTLC.TheChildrenOfClay.RussianTranslate;

/// <summary>
/// Minimal self-contained JSON parser so the plugin has zero external NuGet/Newtonsoft runtime dependencies.
/// </summary>
internal sealed class SimpleJson
{
    private readonly string _src;
    private int _pos;

    private SimpleJson(string src)
    {
        _src = src ?? string.Empty;
        _pos = 0;
    }

    public static object Parse(string json)
    {
        var parser = new SimpleJson(json);
        parser.SkipWhitespace();
        if (parser._pos >= parser._src.Length)
        {
            return null;
        }
        return parser.ParseValue();
    }

    private object ParseValue()
    {
        SkipWhitespace();
        if (_pos >= _src.Length)
        {
            throw new FormatException("Unexpected end of JSON.");
        }

        char c = _src[_pos];
        return c switch
        {
            '{' => ParseObject(),
            '[' => ParseArray(),
            '"' => ParseString(),
            't' => ParseLiteral("true", true),
            'f' => ParseLiteral("false", false),
            'n' => ParseLiteral("null", null),
            _ when c == '-' || (c >= '0' && c <= '9') => ParseNumber(),
            _ => throw new FormatException($"Unexpected JSON character '{c}' at position {_pos}.")
        };
    }

    private Dictionary<string, object> ParseObject()
    {
        var dict = new Dictionary<string, object>(StringComparer.Ordinal);
        _pos++; // '{'
        SkipWhitespace();
        if (Peek('}'))
        {
            _pos++;
            return dict;
        }

        while (true)
        {
            SkipWhitespace();
            string key = ParseString();
            SkipWhitespace();
            Expect(':');
            object val = ParseValue();
            dict[key] = val;

            SkipWhitespace();
            if (Peek(','))
            {
                _pos++;
                continue;
            }
            if (Peek('}'))
            {
                _pos++;
                break;
            }
            throw new FormatException($"Expected ',' or '}}' at position {_pos}.");
        }

        return dict;
    }

    private List<object> ParseArray()
    {
        var list = new List<object>();
        _pos++; // '['
        SkipWhitespace();
        if (Peek(']'))
        {
            _pos++;
            return list;
        }

        while (true)
        {
            list.Add(ParseValue());
            SkipWhitespace();
            if (Peek(','))
            {
                _pos++;
                continue;
            }
            if (Peek(']'))
            {
                _pos++;
                break;
            }
            throw new FormatException($"Expected ',' or ']' at position {_pos}.");
        }

        return list;
    }

    private string ParseString()
    {
        Expect('"');
        var sb = new StringBuilder();
        while (_pos < _src.Length)
        {
            char c = _src[_pos++];
            if (c == '"')
            {
                return sb.ToString();
            }
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (_pos >= _src.Length)
            {
                throw new FormatException("Unterminated escape sequence in JSON string.");
            }
            char esc = _src[_pos++];
            switch (esc)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (_pos + 4 > _src.Length)
                    {
                        throw new FormatException("Invalid \\u escape in JSON string.");
                    }
                    string hex = _src.Substring(_pos, 4);
                    _pos += 4;
                    sb.Append((char)ushort.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    break;
                default:
                    throw new FormatException($"Unsupported escape '\\{esc}' at position {_pos}.");
            }
        }
        throw new FormatException("Unterminated JSON string.");
    }

    private object ParseLiteral(string literal, object value)
    {
        if (_pos + literal.Length > _src.Length || string.CompareOrdinal(_src, _pos, literal, 0, literal.Length) != 0)
        {
            throw new FormatException($"Expected '{literal}' at position {_pos}.");
        }
        _pos += literal.Length;
        return value;
    }

    private object ParseNumber()
    {
        int start = _pos;
        if (_src[_pos] == '-') _pos++;
        while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
        bool isFloat = false;
        if (_pos < _src.Length && _src[_pos] == '.')
        {
            isFloat = true;
            _pos++;
            while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
        }
        if (_pos < _src.Length && (_src[_pos] == 'e' || _src[_pos] == 'E'))
        {
            isFloat = true;
            _pos++;
            if (_pos < _src.Length && (_src[_pos] == '+' || _src[_pos] == '-')) _pos++;
            while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
        }

        string token = _src.Substring(start, _pos - start);
        if (!isFloat && long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out long lval))
        {
            return lval;
        }
        return double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private void SkipWhitespace()
    {
        while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos]))
        {
            _pos++;
        }
    }

    private bool Peek(char expected) => _pos < _src.Length && _src[_pos] == expected;

    private void Expect(char expected)
    {
        if (_pos >= _src.Length || _src[_pos] != expected)
        {
            throw new FormatException($"Expected '{expected}' at position {_pos}.");
        }
        _pos++;
    }
}
