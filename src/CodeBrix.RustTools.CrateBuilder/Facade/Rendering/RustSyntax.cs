using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// Spelling helpers for the generated Rust, C and TOML text: identifiers, keywords, string
/// literals and comment lines.
/// </summary>
internal static class RustSyntax
{
    private static readonly HashSet<string> RustKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "as", "break", "const", "continue", "crate", "else", "enum", "extern", "false", "fn", "for", "if", "impl", "in",
        "let", "loop", "match", "mod", "move", "mut", "pub", "ref", "return", "self", "Self", "static", "struct", "super",
        "trait", "true", "type", "unsafe", "use", "where", "while", "async", "await", "dyn", "abstract", "become", "box",
        "do", "final", "macro", "override", "priv", "typeof", "unsized", "virtual", "yield", "try", "gen",
    };

    private static readonly HashSet<string> NotRawable = new HashSet<string>(StringComparer.Ordinal) { "self", "Self", "crate", "super", "_" };

    private static readonly HashSet<string> CKeywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "auto", "break", "case", "char", "const", "continue", "default", "do", "double", "else", "enum", "extern", "float",
        "for", "goto", "if", "inline", "int", "long", "register", "restrict", "return", "short", "signed", "sizeof", "static",
        "struct", "switch", "typedef", "union", "unsigned", "void", "volatile", "while", "bool", "true", "false", "_Bool",
        "_Complex", "_Imaginary", "_Alignas", "_Alignof", "_Atomic", "_Generic", "_Noreturn", "_Static_assert",
        "_Thread_local", "alignas", "alignof", "nullptr", "static_assert", "thread_local", "typeof", "constexpr",
    };

    /// <summary>Whether <paramref name="name"/> is an ASCII identifier ([A-Za-z_][A-Za-z0-9_]*,
    /// not "_").</summary>
    public static bool IsIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || name == "_" || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
        {
            return false;
        }

        return name.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
    }

    /// <summary>Whether <paramref name="name"/> can be an exported C symbol or C type name: an
    /// identifier that is neither a C nor a Rust keyword.</summary>
    public static bool IsCSymbol(string name) => IsIdentifier(name) && !CKeywords.Contains(name) && !RustKeywords.Contains(name);

    /// <summary>A Rust identifier for <paramref name="name"/>: keywords become raw identifiers
    /// (<c>r#type</c>); the four that cannot be raw get a trailing underscore. A name already
    /// written as <c>r#x</c> is kept.</summary>
    public static string Ident(string name)
    {
        if (name.StartsWith("r#", StringComparison.Ordinal) && IsIdentifier(name.Substring(2)))
        {
            return name;
        }

        if (!IsIdentifier(name) && name != "_")
        {
            throw new FacadeGenerationException($"'{name}' is not a usable Rust identifier in the mapping plan.");
        }

        if (NotRawable.Contains(name))
        {
            return name + "_";
        }

        return RustKeywords.Contains(name) ? "r#" + name : name;
    }

    /// <summary>The facade local that holds the upstream-typed value of a Rust parameter:
    /// <c>this</c> for the receiver (<c>self</c>), else the parameter's own name.</summary>
    public static string Local(string rustName) => rustName == "self" ? FacadeConventions.ReceiverLocal : Ident(rustName);

    /// <summary>A C parameter name: keywords of C get a trailing underscore.</summary>
    public static string CParameter(string name) => CKeywords.Contains(name) ? name + "_" : name;

    /// <summary>A Rust string literal (double quotes, escapes).</summary>
    public static string RustString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text ?? string.Empty)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        sb.Append("\\u{").Append(((int)c).ToString("x", System.Globalization.CultureInfo.InvariantCulture)).Append('}');
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

    /// <summary>A TOML basic string (double quotes, escapes).</summary>
    public static string TomlString(string text)
    {
        var sb = new StringBuilder("\"");
        foreach (var c in text ?? string.Empty)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20 || c == 0x7f)
                    {
                        sb.Append("\\u").Append(((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
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

    /// <summary>Documentation text as comment lines with <paramref name="marker"/> ("///" for
    /// Rust, "//" for C): one line per text line, trailing blanks removed, leading and trailing
    /// empty lines dropped; empty for no text.</summary>
    public static IReadOnlyList<string> CommentLines(string doc, string marker)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return Array.Empty<string>();
        }

        var lines = doc.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines.Select(l => l.Length == 0 ? marker : marker + " " + l).ToList();
    }

    /// <summary>One line of text with control characters (line breaks included) replaced by
    /// spaces, for single-line comments.</summary>
    public static string OneLine(string text)
        => new string((text ?? string.Empty).Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();

    /// <summary>The upstream path of a plan path ("Encoder", "shapes::Rect", "crate::Vec2"):
    /// <c>up::</c> + the path without a leading <c>crate::</c> / <c>::</c>.</summary>
    public static string Upstream(string rustPath)
    {
        if (string.IsNullOrWhiteSpace(rustPath))
        {
            throw new FacadeGenerationException("A mapping-plan item has no Rust path.");
        }

        var path = rustPath.Trim();
        if (path.StartsWith("::", StringComparison.Ordinal))
        {
            path = path.Substring(2);
        }

        if (path.StartsWith("crate::", StringComparison.Ordinal))
        {
            path = path.Substring("crate::".Length);
        }

        return "up::" + path;
    }
}
