using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Helpers for Rust doc comment text (<see cref="RustItem.Docs"/>), the source of the
/// wrapper's XML doc comments.
/// </summary>
internal static class RustDocText
{
    /// <summary>The paragraphs (separated by blank lines), each joined into one line.</summary>
    public static IReadOnlyList<string> Paragraphs(string docs)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(docs))
        {
            return result;
        }

        var current = new List<string>();
        foreach (var raw in docs.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush(current, result);
            }
            else
            {
                current.Add(line);
            }
        }

        Flush(current, result);
        return result;
    }

    /// <summary>The first paragraph, or an empty string.</summary>
    public static string Summary(string docs) => Paragraphs(docs).FirstOrDefault() ?? string.Empty;

    /// <summary>Whether the docs mention <paramref name="text"/> (ordinal, case-insensitive).</summary>
    public static bool Mentions(string docs, string text)
        => !string.IsNullOrEmpty(docs) && docs.Contains(text, StringComparison.OrdinalIgnoreCase);

    private static void Flush(List<string> current, List<string> result)
    {
        if (current.Count > 0)
        {
            result.Add(string.Join(" ", current));
            current.Clear();
        }
    }
}
