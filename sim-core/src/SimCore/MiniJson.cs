using System;
using System.Collections.Generic;
using System.Text;

namespace SimCore
{
    /// <summary>Thrown when a data file is malformed or holds an invalid value. The message names the place in the file.</summary>
    public sealed class GameDataException : Exception
    {
        public GameDataException(string message) : base(message) { }
    }

    /// <summary>
    /// Tiny JSON reader with no dependencies (works the same under Unity and plain .NET).
    /// Result types: Dictionary&lt;string, object&gt;, List&lt;object&gt;, string, long, bool, null.
    /// Numbers must be whole (no fractions or exponents): the simulation only uses integers, so no float can sneak into balance data.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            if (text == null) throw new GameDataException("JSON text is null");
            var p = new Parser(text);
            p.SkipSpace();
            object value = p.ReadValue();
            p.SkipSpace();
            if (!p.AtEnd) throw p.Error("unexpected text after the end of the data");
            return value;
        }

        private sealed class Parser
        {
            private readonly string _s;
            private int _i;

            public Parser(string s)
            {
                _s = s;
                if (_s.Length > 0 && _s[0] == '﻿') _i = 1; // byte order mark
            }

            public bool AtEnd => _i >= _s.Length;

            public GameDataException Error(string what)
            {
                int line = 1, col = 1;
                for (int k = 0; k < _i && k < _s.Length; k++)
                {
                    if (_s[k] == '\n') { line++; col = 1; } else col++;
                }
                return new GameDataException($"JSON error at line {line}, column {col}: {what}");
            }

            public void SkipSpace()
            {
                while (_i < _s.Length)
                {
                    char c = _s[_i];
                    if (c == ' ' || c == '\t' || c == '\r' || c == '\n') _i++;
                    else break;
                }
            }

            public object ReadValue()
            {
                if (AtEnd) throw Error("unexpected end of data");
                char c = _s[_i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                throw Error("unexpected character '" + c + "'");
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(_s, _i, word, 0, word.Length) != 0) return false;
                _i += word.Length;
                return true;
            }

            private Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>(StringComparer.Ordinal);
                _i++; // {
                SkipSpace();
                if (!AtEnd && _s[_i] == '}') { _i++; return map; }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || _s[_i] != '"') throw Error("expected a quoted name");
                    string key = ReadString();
                    SkipSpace();
                    if (AtEnd || _s[_i] != ':') throw Error("expected ':' after \"" + key + "\"");
                    _i++;
                    SkipSpace();
                    if (map.ContainsKey(key)) throw Error("duplicate name \"" + key + "\"");
                    map[key] = ReadValue();
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end of data inside an object");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == '}') { _i++; return map; }
                    throw Error("expected ',' or '}'");
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                _i++; // [
                SkipSpace();
                if (!AtEnd && _s[_i] == ']') { _i++; return list; }
                while (true)
                {
                    SkipSpace();
                    list.Add(ReadValue());
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end of data inside a list");
                    if (_s[_i] == ',') { _i++; continue; }
                    if (_s[_i] == ']') { _i++; return list; }
                    throw Error("expected ',' or ']'");
                }
            }

            private string ReadString()
            {
                _i++; // opening quote
                var sb = new StringBuilder();
                while (true)
                {
                    if (AtEnd) throw Error("text is not closed");
                    char c = _s[_i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (AtEnd) throw Error("text is not closed");
                    char e = _s[_i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 't': sb.Append('\t'); break;
                        case 'r': sb.Append('\r'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (_i + 4 > _s.Length) throw Error("bad \\u escape");
                            int code = 0;
                            for (int k = 0; k < 4; k++)
                            {
                                int d = HexValue(_s[_i + k]);
                                if (d < 0) throw Error("bad \\u escape");
                                code = code * 16 + d;
                            }
                            _i += 4;
                            sb.Append((char)code);
                            break;
                        default: throw Error("unknown escape \\" + e);
                    }
                }
            }

            private static int HexValue(char c)
            {
                if (c >= '0' && c <= '9') return c - '0';
                if (c >= 'a' && c <= 'f') return c - 'a' + 10;
                if (c >= 'A' && c <= 'F') return c - 'A' + 10;
                return -1;
            }

            private long ReadNumber()
            {
                int start = _i;
                bool negative = false;
                if (_s[_i] == '-') { negative = true; _i++; }
                if (AtEnd || _s[_i] < '0' || _s[_i] > '9') throw Error("bad number");
                long value = 0;
                while (!AtEnd && _s[_i] >= '0' && _s[_i] <= '9')
                {
                    int digit = _s[_i] - '0';
                    if (value > (long.MaxValue - digit) / 10) throw Error("number is too large");
                    value = value * 10 + digit;
                    _i++;
                }
                if (!AtEnd && (_s[_i] == '.' || _s[_i] == 'e' || _s[_i] == 'E'))
                    throw Error("only whole numbers are allowed (\"" + _s.Substring(start, Math.Min(12, _s.Length - start)) + "...\")");
                return negative ? -value : value;
            }
        }
    }
}
