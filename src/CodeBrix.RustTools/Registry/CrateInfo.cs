using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// A crate's summary and version history from crates.io.
/// </summary>
public sealed class CrateInfo
{
    /// <summary>The crate name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The crate description, or <c>null</c>.</summary>
    public string Description { get; init; }

    /// <summary>The source repository URL, or <c>null</c>.</summary>
    public string Repository { get; init; }

    /// <summary>The home page URL, or <c>null</c>.</summary>
    public string Homepage { get; init; }

    /// <summary>The documentation URL, or <c>null</c>.</summary>
    public string Documentation { get; init; }

    /// <summary>The highest non-yanked, non-pre-release version, or <c>null</c> when the crate
    /// has only pre-releases.</summary>
    public string MaxStableVersion { get; init; }

    /// <summary>The most recently published version (may be a pre-release).</summary>
    public string NewestVersion { get; init; }

    /// <summary>The highest version including pre-releases.</summary>
    public string MaxVersion { get; init; }

    /// <summary>All-time download count.</summary>
    public long Downloads { get; init; }

    /// <summary>When the crate was last updated, or <c>null</c>.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>The published versions, newest first, as crates.io lists them.</summary>
    public IReadOnlyList<CrateVersionInfo> Versions { get; init; } = Array.Empty<CrateVersionInfo>();

    /// <summary>The version a dependency update would normally move to:
    /// <see cref="MaxStableVersion"/>, or <see cref="MaxVersion"/> when there is no stable one.</summary>
    public string LatestVersion => MaxStableVersion ?? MaxVersion ?? NewestVersion;

    /// <summary>
    /// Returns the listed version with this exact number, or <c>null</c>.
    /// </summary>
    /// <param name="number">The version text, for example <c>1.0.18</c>.</param>
    public CrateVersionInfo GetVersion(string number)
        => string.IsNullOrWhiteSpace(number)
            ? null
            : Versions.FirstOrDefault(v => string.Equals(v.Number, number.Trim(), StringComparison.Ordinal));

    /// <summary>
    /// Whether the given version is yanked: <c>true</c> or <c>false</c> when the version is
    /// listed, <c>null</c> when it is not.
    /// </summary>
    /// <param name="number">The version text, for example <c>1.0.18</c>.</param>
    public bool? IsYanked(string number) => GetVersion(number)?.IsYanked;
}
