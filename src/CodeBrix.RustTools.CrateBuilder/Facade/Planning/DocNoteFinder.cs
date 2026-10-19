using System.Collections.Generic;
using System.Text.RegularExpressions;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Finds the sentences of a doc comment that state a unit, range, convention or thread rule
/// (cues such as "per second", "in milliseconds", "units", "ticks", "in bytes", "must", "thread",
/// "premultiplied", "row-major"). Fenced code blocks are ignored.
/// </summary>
internal static class DocNoteFinder
{
    private static readonly Regex Cue = new Regex(
        @"\b(per \w+|in (nano|micro|milli)?seconds|(nano|micro|milli)seconds|in (bytes|bits|pixels|radians|degrees|meters|metres|hz|hertz|samples|frames|points)|units|unit of|ticks|sample rate|number of bytes|percent|premultiplied|srgb|linear (light|color|colour|space)|gamma|row-major|column-major|little-endian|big-endian|utf-8|normali[sz]ed|inclusive|exclusive|in the range|between \S+ and \S+|must|must not|thread|threads|thread-safe|panics?|clamped|wrapping|saturat\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex SentenceEnd = new Regex(@"(?<=[.!?])\s+(?=[A-Z0-9`\[(""'])", RegexOptions.CultureInvariant);

    /// <summary>The cue sentences of <paramref name="docs"/>, in order, distinct.</summary>
    public static IReadOnlyList<string> Find(string docs)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(docs))
        {
            return result;
        }

        foreach (var paragraph in RustDocText.Paragraphs(StripCodeBlocks(docs)))
        {
            foreach (var raw in SentenceEnd.Split(paragraph))
            {
                var sentence = raw.Trim();
                if (sentence.Length > 0 && Cue.IsMatch(sentence) && !result.Contains(sentence))
                {
                    result.Add(sentence);
                }
            }
        }

        return result;
    }

    private static string StripCodeBlocks(string docs)
    {
        var lines = docs.Replace("\r\n", "\n").Split('\n');
        var kept = new List<string>();
        var inside = false;
        foreach (var line in lines)
        {
            if (line.TrimStart().StartsWith("```", System.StringComparison.Ordinal))
            {
                inside = !inside;
                kept.Add(string.Empty);
                continue;
            }

            kept.Add(inside ? string.Empty : line);
        }

        return string.Join("\n", kept);
    }
}
