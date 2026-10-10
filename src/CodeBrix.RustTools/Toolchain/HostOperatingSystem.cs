using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Identifies the operating system this process runs on, including whether a Linux host is
/// Debian-based (so apt commands apply).
/// </summary>
public static class HostOperatingSystem
{
    private const string OsReleasePath = "/etc/os-release";
    private const string LegacyOsReleasePath = "/usr/lib/os-release";

    /// <summary>
    /// Detects the operating-system family of this machine. On Linux this reads
    /// <c>/etc/os-release</c> (falling back to <c>/usr/lib/os-release</c>).
    /// </summary>
    public static OperatingSystemKind Detect()
    {
        if (OperatingSystem.IsWindows())
        {
            return OperatingSystemKind.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return OperatingSystemKind.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return ClassifyOsRelease(ReadOsRelease());
        }

        return OperatingSystemKind.Unknown;
    }

    /// <summary>
    /// A human-readable name of this operating system: the os-release <c>PRETTY_NAME</c> on
    /// Linux, otherwise the runtime's description.
    /// </summary>
    public static string Describe()
    {
        if (OperatingSystem.IsLinux())
        {
            var values = ParseOsRelease(ReadOsRelease());
            if (values.TryGetValue("PRETTY_NAME", out var pretty) && !string.IsNullOrWhiteSpace(pretty))
            {
                return pretty;
            }
        }

        return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// Classifies the contents of an os-release file: <see cref="OperatingSystemKind.DebianLinux"/>
    /// when <c>ID</c> or <c>ID_LIKE</c> contains <c>debian</c>, otherwise
    /// <see cref="OperatingSystemKind.OtherLinux"/>. Empty or <c>null</c> text also gives
    /// <see cref="OperatingSystemKind.OtherLinux"/>.
    /// </summary>
    /// <param name="osReleaseText">The text of an os-release file.</param>
    public static OperatingSystemKind ClassifyOsRelease(string osReleaseText)
    {
        var values = ParseOsRelease(osReleaseText);
        foreach (var key in new[] { "ID", "ID_LIKE" })
        {
            if (values.TryGetValue(key, out var value)
                && value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Contains("debian", StringComparer.OrdinalIgnoreCase))
            {
                return OperatingSystemKind.DebianLinux;
            }
        }

        return OperatingSystemKind.OtherLinux;
    }

    /// <summary>
    /// Parses os-release <c>KEY=value</c> lines (quotes removed; comments and blank lines
    /// skipped) into a case-sensitive dictionary.
    /// </summary>
    /// <param name="osReleaseText">The text of an os-release file; <c>null</c> gives an empty
    /// dictionary.</param>
    public static IReadOnlyDictionary<string, string> ParseOsRelease(string osReleaseText)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(osReleaseText))
        {
            return result;
        }

        foreach (var rawLine in osReleaseText.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line.Substring(0, equals).Trim();
            var value = line.Substring(equals + 1).Trim();
            if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
            {
                value = value.Substring(1, value.Length - 2);
            }

            result[key] = value;
        }

        return result;
    }

    private static string ReadOsRelease()
    {
        foreach (var path in new[] { OsReleasePath, LegacyOsReleasePath })
        {
            try
            {
                if (File.Exists(path))
                {
                    return File.ReadAllText(path);
                }
            }
            catch (IOException)
            {
                // Try the next location.
            }
            catch (UnauthorizedAccessException)
            {
                // Try the next location.
            }
        }

        return null;
    }
}
