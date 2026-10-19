using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The WrapPlan pattern rule: <c>*</c> matches any run of characters (also <c>::</c>), every
/// other character matches itself (ordinal, case-sensitive). A pattern is tried against every
/// path form of an item: its canonical path and each public path, each with and without the
/// leading <c>crate::</c>.
/// </summary>
internal static class PathGlob
{
    /// <summary>Whether <paramref name="pattern"/> matches <paramref name="text"/> as a whole.</summary>
    public static bool IsMatch(string pattern, string text)
    {
        if (pattern == null || text == null)
        {
            return false;
        }

        var p = 0;
        var t = 0;
        var star = -1;
        var mark = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                mark = t;
            }
            else if (p < pattern.Length && pattern[p] == text[t])
            {
                p++;
                t++;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++mark;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
        {
            p++;
        }

        return p == pattern.Length;
    }

    /// <summary>Whether any pattern matches any of the path forms.</summary>
    public static bool AnyMatch(IEnumerable<string> patterns, IReadOnlyCollection<string> forms)
        => (patterns ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Any(p => forms.Any(f => IsMatch(p.Trim(), f)));

    /// <summary>Every path form of an item: the given paths, each with and without
    /// <c>crate::</c>, distinct, in a stable order.</summary>
    public static IReadOnlyList<string> Forms(IEnumerable<string> paths)
    {
        var result = new List<string>();
        foreach (var path in paths.Where(p => !string.IsNullOrEmpty(p)))
        {
            Add(result, path);
            Add(result, PlannerNames.WithoutCrate(path));
            if (!path.StartsWith("crate::", StringComparison.Ordinal) && path != "crate")
            {
                Add(result, "crate::" + path);
            }
        }

        return result;
    }

    private static void Add(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }
}
