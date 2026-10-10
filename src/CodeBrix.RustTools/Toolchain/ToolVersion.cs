using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// A version reported by a Rust tool, parsed from output such as
/// <c>cargo 1.94.1 (29ea6fb6a 2026-03-24)</c>, <c>rustc 1.95.0-nightly (abc123 2026-04-01)</c>,
/// <c>clippy 0.1.94 (e408947bfd 2026-03-25)</c> or a bare <c>1.29.1</c>.
/// </summary>
/// <remarks>
/// Equality and ordering use only <see cref="Major"/>, <see cref="Minor"/>, <see cref="Patch"/>
/// and <see cref="PreRelease"/> (semantic-version precedence), so a version read from a manifest
/// equals the same version read from <c>--version</c> output even though <see cref="Raw"/>
/// differs. The <c>-stable</c> suffix that rustfmt prints is treated as no pre-release.
/// </remarks>
public sealed partial record ToolVersion : IComparable<ToolVersion>, IComparable
{
    /// <summary>
    /// Creates a version from its numeric parts.
    /// </summary>
    /// <param name="major">The major version number.</param>
    /// <param name="minor">The minor version number.</param>
    /// <param name="patch">The patch version number.</param>
    /// <param name="preRelease">The pre-release label (for example <c>nightly</c> or
    /// <c>beta.3</c>), or <c>null</c> for a release.</param>
    public ToolVersion(int major, int minor, int patch, string preRelease = null)
    {
        if (major < 0 || minor < 0 || patch < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(major), "Version numbers cannot be negative.");
        }

        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = NormalizePreRelease(preRelease);
        Channel = ChannelFromPreRelease(PreRelease);
        Raw = ToString();
    }

    /// <summary>The major version number (the 1 in 1.94.1).</summary>
    public int Major { get; init; }

    /// <summary>The minor version number (the 94 in 1.94.1).</summary>
    public int Minor { get; init; }

    /// <summary>The patch version number (the 1 in 1.94.1; 0 when the text had only two parts).</summary>
    public int Patch { get; init; }

    /// <summary>The pre-release label after the hyphen (<c>nightly</c>, <c>beta.3</c>, ...), or
    /// <c>null</c> for a release version.</summary>
    public string PreRelease { get; init; }

    /// <summary>The release channel implied by <see cref="PreRelease"/>: a release version is
    /// <see cref="RustChannel.Stable"/>.</summary>
    public RustChannel Channel { get; init; }

    /// <summary>The tool name that preceded the version in the text (<c>cargo</c>,
    /// <c>rustc</c>, ...), or <c>null</c> when there was none.</summary>
    public string ToolName { get; init; }

    /// <summary>The abbreviated git commit hash in the parentheses, or <c>null</c>.</summary>
    public string CommitHash { get; init; }

    /// <summary>The commit date in the parentheses, or <c>null</c>.</summary>
    public DateOnly? CommitDate { get; init; }

    /// <summary>The trimmed first line of the text the version was parsed from.</summary>
    public string Raw { get; init; }

    /// <summary>
    /// Parses tool output or a bare version string.
    /// </summary>
    /// <param name="text">The text to parse; only the first line that contains a version is used.</param>
    /// <exception cref="FormatException">No version number was found in the text.</exception>
    public static ToolVersion Parse(string text)
    {
        if (!TryParse(text, out var version))
        {
            throw new FormatException($"No version number was found in '{text}'.");
        }

        return version;
    }

    /// <summary>
    /// Parses tool output or a bare version string without throwing.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="version">The parsed version, or <c>null</c> when the text has none.</param>
    /// <returns><c>true</c> when a version was found.</returns>
    public static bool TryParse(string text, out ToolVersion version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var match = StructuredPattern().Match(line);
            if (match.Success)
            {
                version = FromMatch(match, line, match.Groups["name"].Success ? match.Groups["name"].Value : null);
                return version != null;
            }

            var loose = LoosePattern().Match(line);
            if (loose.Success)
            {
                version = FromMatch(loose, line, null);
                return version != null;
            }
        }

        return false;
    }

    /// <summary>
    /// True when this version is at least the given version (pre-release labels ignored).
    /// </summary>
    /// <param name="major">The minimum major version.</param>
    /// <param name="minor">The minimum minor version.</param>
    /// <param name="patch">The minimum patch version.</param>
    public bool IsAtLeast(int major, int minor, int patch = 0)
        => (Major, Minor, Patch).CompareTo((major, minor, patch)) >= 0;

    /// <summary>
    /// Compares by semantic-version precedence: numbers first, then a release outranks any
    /// pre-release of the same numbers, then pre-release identifiers are compared piecewise.
    /// A <c>null</c> other version sorts first.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    public int CompareTo(ToolVersion other)
    {
        if (other is null)
        {
            return 1;
        }

        var numeric = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (numeric != 0)
        {
            return numeric;
        }

        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    /// <inheritdoc />
    int IComparable.CompareTo(object obj)
    {
        if (obj is null)
        {
            return 1;
        }

        if (obj is ToolVersion other)
        {
            return CompareTo(other);
        }

        throw new ArgumentException("Object is not a ToolVersion.", nameof(obj));
    }

    /// <summary>
    /// Version equality: same numbers and same pre-release label (case-insensitive). Tool name,
    /// commit and raw text are ignored.
    /// </summary>
    /// <param name="other">The version to compare with.</param>
    public bool Equals(ToolVersion other)
        => other is not null && CompareTo(other) == 0;

    /// <summary>
    /// A hash consistent with <see cref="Equals(ToolVersion)"/>.
    /// </summary>
    public override int GetHashCode()
        => HashCode.Combine(Major, Minor, Patch, PreRelease == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(PreRelease));

    /// <summary>
    /// The canonical version text, for example <c>1.94.1</c> or <c>1.95.0-nightly</c>.
    /// </summary>
    public override string ToString()
        => PreRelease == null
            ? string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}")
            : string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}-{PreRelease}");

    /// <summary>True when <paramref name="left"/> sorts before <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    public static bool operator <(ToolVersion left, ToolVersion right) => Compare(left, right) < 0;

    /// <summary>True when <paramref name="left"/> sorts after <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    public static bool operator >(ToolVersion left, ToolVersion right) => Compare(left, right) > 0;

    /// <summary>True when <paramref name="left"/> sorts before or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    public static bool operator <=(ToolVersion left, ToolVersion right) => Compare(left, right) <= 0;

    /// <summary>True when <paramref name="left"/> sorts after or equal to <paramref name="right"/>.</summary>
    /// <param name="left">The first version.</param>
    /// <param name="right">The second version.</param>
    public static bool operator >=(ToolVersion left, ToolVersion right) => Compare(left, right) >= 0;

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append(ToString());
        return true;
    }

    private static int Compare(ToolVersion left, ToolVersion right)
    {
        if (left is null)
        {
            return right is null ? 0 : -1;
        }

        return left.CompareTo(right);
    }

    private static ToolVersion FromMatch(Match match, string line, string toolName)
    {
        if (!int.TryParse(match.Groups["maj"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(match.Groups["min"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return null;
        }

        var patch = 0;
        if (match.Groups["pat"].Success
            && !int.TryParse(match.Groups["pat"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out patch))
        {
            return null;
        }

        DateOnly? date = null;
        if (match.Groups["date"].Success
            && DateOnly.TryParseExact(match.Groups["date"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            date = parsedDate;
        }

        var preRelease = match.Groups["pre"].Success ? match.Groups["pre"].Value : null;
        return new ToolVersion(major, minor, patch, preRelease)
        {
            ToolName = toolName,
            CommitHash = match.Groups["hash"].Success ? match.Groups["hash"].Value : null,
            CommitDate = date,
            Raw = line,
        };
    }

    private static string NormalizePreRelease(string preRelease)
    {
        if (string.IsNullOrWhiteSpace(preRelease))
        {
            return null;
        }

        var trimmed = preRelease.Trim();
        return string.Equals(trimmed, "stable", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static RustChannel ChannelFromPreRelease(string preRelease)
    {
        if (preRelease == null)
        {
            return RustChannel.Stable;
        }

        if (preRelease.StartsWith("nightly", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Nightly;
        }

        if (preRelease.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Beta;
        }

        if (preRelease.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Dev;
        }

        return RustChannel.Unknown;
    }

    private static int ComparePreRelease(string left, string right)
    {
        if (left == null)
        {
            return right == null ? 0 : 1;
        }

        if (right == null)
        {
            return -1;
        }

        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var i = 0; i < Math.Min(leftParts.Length, rightParts.Length); i++)
        {
            var leftNumeric = long.TryParse(leftParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var leftNumber);
            var rightNumeric = long.TryParse(rightParts[i], NumberStyles.None, CultureInfo.InvariantCulture, out var rightNumber);
            int result;
            if (leftNumeric && rightNumeric)
            {
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftNumeric)
            {
                result = -1;
            }
            else if (rightNumeric)
            {
                result = 1;
            }
            else
            {
                result = string.Compare(leftParts[i], rightParts[i], StringComparison.OrdinalIgnoreCase);
            }

            if (result != 0)
            {
                return result;
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }

    [GeneratedRegex(@"^(?:(?<name>[A-Za-z][A-Za-z0-9_\-]*)\s+)?v?(?<maj>\d+)\.(?<min>\d+)(?:\.(?<pat>\d+))?(?:-(?<pre>[0-9A-Za-z][0-9A-Za-z.\-]*))?(?:\+[0-9A-Za-z.\-]+)?(?:\s+\((?:(?<hash>[0-9a-fA-F]{4,40})(?=[\s)])\s*)?(?<date>\d{4}-\d{2}-\d{2})?[^)]*\))?(?:\s|$)", RegexOptions.CultureInvariant)]
    private static partial Regex StructuredPattern();

    [GeneratedRegex(@"(?<![\d.])(?<maj>\d+)\.(?<min>\d+)(?:\.(?<pat>\d+))?", RegexOptions.CultureInvariant)]
    private static partial Regex LoosePattern();
}
