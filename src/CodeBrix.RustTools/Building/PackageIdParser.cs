using System;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Splits a cargo package id into a name and version. Handles the current format
/// (<c>registry+https://...#serde@1.0.0</c>, <c>path+file:///src/app#0.1.0</c>) and the legacy
/// one (<c>serde 1.0.0 (registry+https://...)</c>).
/// </summary>
internal static class PackageIdParser
{
    public static (string Name, string Version) Parse(string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
        {
            return (null, null);
        }

        var hash = packageId.LastIndexOf('#');
        if (hash >= 0)
        {
            var fragment = packageId.Substring(hash + 1);
            var url = packageId.Substring(0, hash);
            var at = fragment.IndexOf('@');
            if (at >= 0)
            {
                return (fragment.Substring(0, at), fragment.Substring(at + 1));
            }

            if (fragment.Length > 0 && !char.IsDigit(fragment[0]))
            {
                return (fragment, null);
            }

            return (LastSegment(url), fragment.Length == 0 ? null : fragment);
        }

        var parts = packageId.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return (parts[0], parts.Length > 1 ? parts[1] : null);
    }

    private static string LastSegment(string url)
    {
        var query = url.IndexOf('?');
        if (query >= 0)
        {
            url = url.Substring(0, query);
        }

        url = url.TrimEnd('/');
        var slash = url.LastIndexOf('/');
        var segment = slash >= 0 ? url.Substring(slash + 1) : url;
        return segment.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segment.Substring(0, segment.Length - 4) : segment;
    }
}
