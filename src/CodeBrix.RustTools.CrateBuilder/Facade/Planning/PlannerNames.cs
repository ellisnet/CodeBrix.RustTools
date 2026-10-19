using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Name conversions of the planner: snake_case for C symbols, PascalCase / camelCase for the
/// managed side, keyword escaping, de-duplication.
/// </summary>
internal static class PlannerNames
{
    private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    private static readonly HashSet<string> CAndRustKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        // C (C23 included)
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern",
        "float", "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed",
        "sizeof", "static", "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "bool",
        "true", "false", "alignas", "alignof", "constexpr", "nullptr", "static_assert", "thread_local", "typeof",
        // Rust (strict and reserved)
        "as", "async", "await", "crate", "dyn", "fn", "impl", "in", "let", "loop", "match", "mod", "move", "mut",
        "pub", "ref", "self", "Self", "super", "trait", "type", "unsafe", "use", "where", "abstract", "become",
        "box", "final", "gen", "macro", "override", "priv", "try", "virtual", "yield",
    };

    /// <summary>Removes a raw-identifier prefix (<c>r#type</c> -&gt; <c>type</c>).</summary>
    public static string Bare(string rustName) => rustName != null && rustName.StartsWith("r#", StringComparison.Ordinal) ? rustName.Substring(2) : rustName;

    /// <summary>snake_case of an identifier (<c>EncoderConfig</c> -&gt; <c>encoder_config</c>,
    /// <c>HTTPServer</c> -&gt; <c>http_server</c>, <c>Vec2</c> -&gt; <c>vec2</c>).</summary>
    public static string Snake(string name)
    {
        name = Bare(name) ?? string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var previous = i > 0 ? name[i - 1] : '_';
                var next = i + 1 < name.Length ? name[i + 1] : '_';
                if (i > 0 && previous != '_' && (char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && char.IsLower(next))))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>PascalCase of a Rust identifier: every <c>_</c>-separated part starts upper-case,
    /// the rest is kept (<c>from_seconds_f64</c> -&gt; <c>FromSecondsF64</c>, <c>Vec2</c> -&gt;
    /// <c>Vec2</c>).</summary>
    public static string Pascal(string name)
    {
        var parts = (Bare(name) ?? string.Empty).Split('_', StringSplitOptions.RemoveEmptyEntries);
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            sb.Append(char.ToUpperInvariant(part[0])).Append(part, 1, part.Length - 1);
        }

        var result = sb.Length == 0 ? "Item" : sb.ToString();
        return char.IsDigit(result[0]) ? "N" + result : result;
    }

    /// <summary>camelCase of a Rust identifier with C# keywords escaped (<c>frame_size</c> -&gt;
    /// <c>frameSize</c>, <c>in</c> -&gt; <c>@in</c>).</summary>
    public static string Camel(string name)
    {
        var pascal = Pascal(name);
        var camel = char.ToLowerInvariant(pascal[0]) + pascal.Substring(1);
        return EscapeCSharp(camel);
    }

    /// <summary>Prefixes a C# keyword with <c>@</c>.</summary>
    public static string EscapeCSharp(string name) => CSharpKeywords.Contains(name) ? "@" + name : name;

    /// <summary>A name that is safe as a C and Rust identifier (a keyword gets a trailing
    /// <c>_</c>).</summary>
    public static string SafeC(string name)
    {
        name = Bare(name);
        return CAndRustKeywords.Contains(name) ? name + "_" : name;
    }

    /// <summary>Whether <paramref name="name"/> is a plain C# identifier (letters, digits,
    /// underscores, not starting with a digit, not a keyword).</summary>
    public static bool IsCSharpIdentifier(string name)
        => !string.IsNullOrEmpty(name)
            && (char.IsLetter(name[0]) || name[0] == '_')
            && name.All(c => char.IsLetterOrDigit(c) || c == '_')
            && !CSharpKeywords.Contains(name);

    /// <summary>Returns <paramref name="name"/>, or <c>name</c> + <paramref name="separator"/> +
    /// 2, 3 ... when it is taken; records the result in <paramref name="taken"/>.</summary>
    public static string Unique(string name, ISet<string> taken, string separator)
    {
        var candidate = name;
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = name + separator + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        taken.Add(candidate);
        return candidate;
    }

    /// <summary>A Rust path without its leading <c>crate::</c>.</summary>
    public static string WithoutCrate(string path)
        => path != null && path.StartsWith("crate::", StringComparison.Ordinal) ? path.Substring("crate::".Length) : path;
}
