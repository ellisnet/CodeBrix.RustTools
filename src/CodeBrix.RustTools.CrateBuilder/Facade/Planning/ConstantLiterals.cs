using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Converts the literal value of a Rust constant into the equal C# constant: integers (decimal,
/// hex, octal, binary, <c>_</c> separators, type suffixes, a leading minus) checked against the
/// range of their type, floats (kept as the decimal text rustc reads, so C# rounds them the same
/// way), <c>true</c> / <c>false</c>, and string literals (normal and raw, every Rust escape).
/// Anything else - an expression, a char, a byte string, a value out of range - is refused with
/// a reason.
/// </summary>
internal static class ConstantLiterals
{
    /// <summary>Tries to convert; on failure <paramref name="why"/> says why.</summary>
    /// <param name="rustType">The constant's type: a primitive name, or <c>&amp;str</c> /
    /// <c>&amp;'static str</c>.</param>
    /// <param name="value">The literal as written.</param>
    /// <param name="managedType">The C# type keyword.</param>
    /// <param name="literal">The C# literal.</param>
    /// <param name="why">The reason it cannot be converted.</param>
    public static bool TryConvert(string rustType, string value, out string managedType, out string literal, out string why)
    {
        managedType = null;
        literal = null;
        why = null;
        var type = (rustType ?? string.Empty).Replace(" ", string.Empty);
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            why = "its value is not a plain literal (an expression is computed by rustc, which CrateBuilder does not run)";
            return false;
        }

        if (type is "&str" or "&'staticstr")
        {
            managedType = "string";
            if (!TryParseString(text, out var s))
            {
                why = "its value is not a plain string literal";
                return false;
            }

            literal = CSharpString(s);
            return true;
        }

        if (type == "bool")
        {
            if (text is "true" or "false")
            {
                managedType = "bool";
                literal = text;
                return true;
            }

            why = "its value is not true or false";
            return false;
        }

        if (type is "f32" or "f64")
        {
            return TryFloat(type, text, out managedType, out literal, out why);
        }

        return TryInteger(type, text, out managedType, out literal, out why);
    }

    private static bool TryFloat(string type, string text, out string managedType, out string literal, out string why)
    {
        managedType = type == "f32" ? "float" : "double";
        literal = null;
        why = null;
        var t = text.Replace("_", string.Empty);
        foreach (var suffix in new[] { "f32", "f64" })
        {
            if (t.EndsWith(suffix, StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - suffix.Length);
            }
        }

        var negative = t.StartsWith('-');
        var body = negative ? t.Substring(1) : t;
        if (body.EndsWith('.'))
        {
            body += "0";
        }

        if (body.Length == 0 || !char.IsDigit(body[0]) || !double.TryParse(body, NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number)
            || double.IsInfinity(number) || (type == "f32" && float.IsInfinity((float)number)))
        {
            why = "its value is not a plain number literal";
            return false;
        }

        literal = (negative ? "-" : string.Empty) + body + (type == "f32" ? "f" : "d");
        return true;
    }

    private static bool TryInteger(string type, string text, out string managedType, out string literal, out string why)
    {
        managedType = null;
        literal = null;
        why = null;
        (string Cs, BigInteger Min, BigInteger Max) range;
        switch (type)
        {
            case "u8": range = ("byte", byte.MinValue, byte.MaxValue); break;
            case "i8": range = ("sbyte", sbyte.MinValue, sbyte.MaxValue); break;
            case "u16": range = ("ushort", ushort.MinValue, ushort.MaxValue); break;
            case "i16": range = ("short", short.MinValue, short.MaxValue); break;
            case "u32": range = ("uint", uint.MinValue, uint.MaxValue); break;
            case "i32": range = ("int", int.MinValue, int.MaxValue); break;
            case "u64": range = ("ulong", ulong.MinValue, ulong.MaxValue); break;
            case "i64": range = ("long", long.MinValue, long.MaxValue); break;
            // A C# nuint / nint constant must fit 32 bits (it is the same on every platform).
            case "usize": range = ("nuint", uint.MinValue, uint.MaxValue); break;
            case "isize": range = ("nint", int.MinValue, int.MaxValue); break;
            default:
                why = $"its type {type} has no C# constant form";
                return false;
        }

        var t = text.Replace("_", string.Empty);
        var negative = t.StartsWith('-');
        if (negative)
        {
            t = t.Substring(1);
        }

        foreach (var suffix in new[] { "usize", "isize", "u8", "u16", "u32", "u64", "i8", "i16", "i32", "i64" })
        {
            if (t.EndsWith(suffix, StringComparison.Ordinal))
            {
                t = t.Substring(0, t.Length - suffix.Length);
                break;
            }
        }

        var radix = 10;
        if (t.StartsWith("0x", StringComparison.Ordinal))
        {
            radix = 16;
            t = t.Substring(2);
        }
        else if (t.StartsWith("0o", StringComparison.Ordinal))
        {
            radix = 8;
            t = t.Substring(2);
        }
        else if (t.StartsWith("0b", StringComparison.Ordinal))
        {
            radix = 2;
            t = t.Substring(2);
        }

        if (t.Length == 0)
        {
            why = "its value is not a plain integer literal";
            return false;
        }

        BigInteger number = 0;
        foreach (var c in t)
        {
            var digit = c >= '0' && c <= '9' ? c - '0' : c >= 'a' && c <= 'f' ? c - 'a' + 10 : c >= 'A' && c <= 'F' ? c - 'A' + 10 : 99;
            if (digit >= radix)
            {
                why = "its value is not a plain integer literal";
                return false;
            }

            number = number * radix + digit;
        }

        if (negative)
        {
            number = -number;
        }

        if (number < range.Min || number > range.Max)
        {
            why = type is "usize" or "isize"
                ? $"its value does not fit 32 bits, and a C# {range.Cs} constant must (the same on every platform)"
                : $"its value does not fit {type}";
            return false;
        }

        managedType = range.Cs;
        literal = number.ToString(CultureInfo.InvariantCulture) + range.Cs switch { "uint" => "u", "ulong" => "ul", "long" => "L", _ => string.Empty };
        if (range.Cs is "byte" or "sbyte" or "ushort" or "short" or "nuint" or "nint")
        {
            literal = "(" + range.Cs + ")" + (number < 0 ? "(" + literal + ")" : literal);
        }

        return true;
    }

    /// <summary>Parses a Rust string literal (<c>"..."</c> with escapes, or <c>r"..."</c> /
    /// <c>r#"..."#</c>).</summary>
    internal static bool TryParseString(string text, out string result)
    {
        result = null;
        if (text.StartsWith('r'))
        {
            var hashes = 0;
            while (1 + hashes < text.Length && text[1 + hashes] == '#')
            {
                hashes++;
            }

            var open = 1 + hashes;
            var close = "\"" + new string('#', hashes);
            if (open >= text.Length || text[open] != '"' || !text.EndsWith(close, StringComparison.Ordinal) || text.Length < open + 1 + close.Length)
            {
                return false;
            }

            result = text.Substring(open + 1, text.Length - open - 1 - close.Length).Replace("\r\n", "\n");
            return true;
        }

        if (text.Length < 2 || text[0] != '"' || text[^1] != '"')
        {
            return false;
        }

        var sb = new StringBuilder();
        var body = text.Substring(1, text.Length - 2).Replace("\r\n", "\n");
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c != '\\')
            {
                if (c == '"')
                {
                    return false;
                }

                sb.Append(c);
                continue;
            }

            if (++i >= body.Length)
            {
                return false;
            }

            switch (body[i])
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case '\\': sb.Append('\\'); break;
                case '0': sb.Append('\0'); break;
                case '\'': sb.Append('\''); break;
                case '"': sb.Append('"'); break;
                case 'x':
                {
                    if (i + 2 >= body.Length || !int.TryParse(body.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b) || b > 0x7F)
                    {
                        return false;
                    }

                    sb.Append((char)b);
                    i += 2;
                    break;
                }

                case 'u':
                {
                    var end = body.IndexOf('}', i);
                    if (i + 1 >= body.Length || body[i + 1] != '{' || end < 0)
                    {
                        return false;
                    }

                    var hex = body.Substring(i + 2, end - i - 2).Replace("_", string.Empty);
                    if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var cp) || cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF))
                    {
                        return false;
                    }

                    sb.Append(char.ConvertFromUtf32(cp));
                    i = end;
                    break;
                }

                case '\n':
                    // line continuation: skip the newline and the next line's leading whitespace
                    while (i + 1 < body.Length && char.IsWhiteSpace(body[i + 1]))
                    {
                        i++;
                    }

                    break;
                default:
                    return false;
            }
        }

        result = sb.ToString();
        return true;
    }

    /// <summary>A C# regular string literal of <paramref name="text"/>.</summary>
    internal static string CSharpString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\0': sb.Append("\\0"); break;
                default:
                    if (char.IsControl(c) || char.IsSurrogate(c) || c > 0x7E)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.Append('"').ToString();
    }
}
