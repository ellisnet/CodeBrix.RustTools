using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// C# identifier rules for the generated wrapper: keyword escaping, PascalCase / camelCase
/// conversion of Rust names, and the member names a generated type must not reuse.
/// </summary>
internal static class CSharpNames
{
    private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked", "class", "const",
        "continue", "decimal", "default", "delegate", "do", "double", "else", "enum", "event", "explicit", "extern",
        "false", "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit", "in", "int", "interface",
        "internal", "is", "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual", "void", "volatile", "while",
    };

    /// <summary>Member names every generated type inherits from <see cref="object"/> (or that
    /// a record struct synthesises); a Rust item with one of these names is renamed.</summary>
    public static readonly IReadOnlyCollection<string> InheritedMemberNames = new[]
    {
        "Equals", "GetHashCode", "GetType", "MemberwiseClone", "ReferenceEquals", "Finalize", "ToString", "PrintMembers", "Deconstruct",
    };

    /// <summary>Whether <paramref name="name"/> is a C# keyword.</summary>
    public static bool IsKeyword(string name) => Keywords.Contains(name);

    /// <summary>The identifier with <c>@</c> in front when it is a keyword.</summary>
    public static string Escape(string name) => IsKeyword(name) ? "@" + name : name;

    /// <summary>Replaces every character that cannot appear in an identifier with
    /// <c>_</c> and puts <c>_</c> in front of a leading digit.</summary>
    public static string Sanitize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        var builder = new StringBuilder(name.Length + 1);
        foreach (var c in name)
        {
            builder.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        }

        if (char.IsDigit(builder[0]))
        {
            builder.Insert(0, '_');
        }

        return builder.ToString();
    }

    /// <summary>PascalCase of a snake_case (or already Pascal) Rust name: <c>frame_size</c>
    /// -&gt; <c>FrameSize</c>, <c>test_project_13</c> -&gt; <c>TestProject13</c>.</summary>
    public static string Pascal(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return "_";
        }

        var parts = name.Split(new[] { '_', '-', ':', '.', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return "_";
        }

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(char.ToUpperInvariant(part[0]));
            builder.Append(part, 1, part.Length - 1);
        }

        return Sanitize(builder.ToString());
    }

    /// <summary>camelCase for a parameter name, keyword-escaped.</summary>
    public static string Camel(string name)
    {
        var pascal = Pascal(name);
        if (pascal.StartsWith('_'))
        {
            return Escape(pascal);
        }

        var leadingUpper = pascal.TakeWhile(char.IsUpper).Count();
        var cut = leadingUpper <= 1 || leadingUpper == pascal.Length ? Math.Max(1, leadingUpper) : leadingUpper - 1;
        return Escape(pascal.Substring(0, cut).ToLowerInvariant() + pascal.Substring(cut));
    }

    /// <summary>The last <c>::</c> segment of a Rust path.</summary>
    public static string LastSegment(string rustPath)
    {
        if (string.IsNullOrEmpty(rustPath))
        {
            return string.Empty;
        }

        var index = rustPath.LastIndexOf("::", StringComparison.Ordinal);
        return index < 0 ? rustPath : rustPath.Substring(index + 2);
    }

    /// <summary>The segment before the last one (<c>Vec2</c> in <c>Vec2::dot</c>), or
    /// <c>null</c>.</summary>
    public static string OwnerSegment(string rustPath)
    {
        if (string.IsNullOrEmpty(rustPath))
        {
            return null;
        }

        var segments = rustPath.Split("::");
        return segments.Length >= 2 ? segments[segments.Length - 2] : null;
    }
}
