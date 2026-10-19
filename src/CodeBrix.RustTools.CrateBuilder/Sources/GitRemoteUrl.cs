using System;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Staging;

namespace CodeBrix.RustTools.CrateBuilder.Sources;

/// <summary>
/// Compares Git remote URLs the way the reuse rule needs: a trailing "/" and a trailing
/// ".git" are ignored, the scheme and host are compared case-insensitively, the path
/// case-sensitively. Works for URLs (https://, ssh://, file://), scp-like addresses
/// (<c>git@host:owner/repo</c>) and local paths.
/// </summary>
internal static class GitRemoteUrl
{
    /// <summary>Whether two remote URLs name the same repository.</summary>
    public static bool AreSame(string a, string b)
        => a != null && b != null && string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    /// <summary>The comparison form of a remote URL.</summary>
    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        var text = TrimEnd(url.Trim());
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme) && text.Contains("://", StringComparison.Ordinal))
        {
            if (uri.IsFile)
            {
                return "file://" + TrimEnd(Path.GetFullPath(uri.LocalPath));
            }

            var userInfo = string.IsNullOrEmpty(uri.UserInfo) ? string.Empty : uri.UserInfo + "@";
            var port = uri.IsDefaultPort ? string.Empty : ":" + uri.Port;
            return uri.Scheme.ToLowerInvariant() + "://" + userInfo + uri.Host.ToLowerInvariant() + port + TrimEnd(uri.AbsolutePath);
        }

        var colon = text.IndexOf(':');
        if (colon > 1 && !text.Contains('\\') && !Path.IsPathRooted(text))
        {
            var hostPart = text.Substring(0, colon);
            var at = hostPart.LastIndexOf('@');
            var host = at < 0 ? hostPart.ToLowerInvariant() : hostPart.Substring(0, at + 1) + hostPart.Substring(at + 1).ToLowerInvariant();
            return "scp:" + host + ":" + TrimEnd(text.Substring(colon + 1));
        }

        return "file://" + TrimEnd(Path.GetFullPath(text));
    }

    /// <summary>A folder name for a clone of <paramref name="url"/>: the last path segment
    /// without ".git", made safe for every file system; "repository" when there is none.</summary>
    public static string RepositoryName(string url)
    {
        var text = TrimEnd((url ?? string.Empty).Trim());
        var last = text.Split('/', '\\', ':').LastOrDefault(s => !string.IsNullOrWhiteSpace(s));
        return StagingFileSystem.SanitizeFolderName(last, "repository");
    }

    private static string TrimEnd(string text)
    {
        var result = text;
        while (true)
        {
            var before = result;
            result = result.TrimEnd('/', '\\');
            if (result.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            {
                result = result.Substring(0, result.Length - 4);
            }

            if (result == before)
            {
                return result;
            }
        }
    }
}
