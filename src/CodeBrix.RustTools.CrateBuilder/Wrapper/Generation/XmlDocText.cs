using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// Turns a Rust doc comment into the lines of a C# XML doc comment (without the
/// <c>///</c> prefix): the first paragraph is the summary, later paragraphs are remarks,
/// fenced code becomes <c>&lt;code&gt;</c>, inline code and intra-doc links become
/// <c>&lt;c&gt;</c>. Every line it returns is non-empty.
/// </summary>
internal static class XmlDocText
{
    private static readonly Regex LinkedCode = new Regex(@"\[`([^`\]]+)`\](\([^)]*\))?", RegexOptions.CultureInvariant);
    private static readonly Regex InlineCode = new Regex("`([^`]+)`", RegexOptions.CultureInvariant);
    private static readonly Regex PlainLink = new Regex(@"\[([^\]]+)\]\([^)]*\)", RegexOptions.CultureInvariant);

    /// <summary>The XML doc lines for <paramref name="doc"/>, or a summary made of
    /// <paramref name="fallback"/> (already XML) when the Rust item has no documentation.</summary>
    public static IReadOnlyList<string> Lines(string doc, string fallback)
    {
        var paragraphs = Paragraphs(doc);
        if (paragraphs.Count == 0)
        {
            return new[] { "<summary>" + fallback + "</summary>" };
        }

        var lines = new List<string> { "<summary>" };
        lines.AddRange(paragraphs[0]);
        lines.Add("</summary>");
        if (paragraphs.Count > 1)
        {
            lines.Add("<remarks>");
            foreach (var paragraph in paragraphs.Skip(1))
            {
                var isCode = paragraph.Count > 0 && paragraph[0] == "<code>";
                if (!isCode)
                {
                    lines.Add("<para>");
                }

                lines.AddRange(paragraph);
                if (!isCode)
                {
                    lines.Add("</para>");
                }
            }

            lines.Add("</remarks>");
        }

        return lines;
    }

    /// <summary>The first paragraph as one plain-text line (for the generated docs), or
    /// <paramref name="fallback"/>.</summary>
    public static string Summary(string doc, string fallback)
    {
        if (string.IsNullOrWhiteSpace(doc))
        {
            return fallback;
        }

        var first = new List<string>();
        foreach (var raw in Normalize(doc).Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                if (first.Count > 0)
                {
                    break;
                }

                continue;
            }

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                break;
            }

            first.Add(line);
        }

        if (first.Count == 0)
        {
            return fallback;
        }

        var text = string.Join(" ", first);
        text = LinkedCode.Replace(text, "$1");
        text = PlainLink.Replace(text, "$1");
        return text.Replace("`", string.Empty);
    }

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    public static string Escape(string text)
        => (text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static List<List<string>> Paragraphs(string doc)
    {
        var result = new List<List<string>>();
        if (string.IsNullOrWhiteSpace(doc))
        {
            return result;
        }

        var current = new List<string>();
        var inCode = false;
        foreach (var raw in Normalize(doc).Split('\n'))
        {
            var trimmed = raw.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    Flush(result, ref current);
                    current.Add("<code>");
                    inCode = true;
                }
                else
                {
                    current.Add("</code>");
                    Flush(result, ref current);
                    inCode = false;
                }

                continue;
            }

            if (inCode)
            {
                if (trimmed.Length > 0 && !trimmed.StartsWith("# ", StringComparison.Ordinal) && trimmed != "#")
                {
                    current.Add(Escape(raw.TrimEnd()));
                }

                continue;
            }

            if (trimmed.Length == 0)
            {
                Flush(result, ref current);
                continue;
            }

            current.Add(Inline(trimmed));
        }

        if (inCode)
        {
            current.Add("</code>");
        }

        Flush(result, ref current);
        return result;
    }

    private static string Inline(string line)
    {
        var escaped = Escape(line);
        escaped = LinkedCode.Replace(escaped, "<c>$1</c>");
        escaped = PlainLink.Replace(escaped, "$1");
        escaped = InlineCode.Replace(escaped, "<c>$1</c>");
        return escaped.Replace("`", string.Empty);
    }

    private static string Normalize(string doc) => doc.Replace("\r\n", "\n").Replace('\r', '\n');

    private static void Flush(List<List<string>> result, ref List<string> current)
    {
        if (current.Count > 0)
        {
            result.Add(current);
            current = new List<string>();
        }
    }
}
