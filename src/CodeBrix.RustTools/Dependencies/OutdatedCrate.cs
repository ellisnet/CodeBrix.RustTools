namespace CodeBrix.RustTools.Dependencies;

/// <summary>
/// A registry crate in the dependency graph that has a newer release on crates.io, or whose
/// installed version has been yanked.
/// </summary>
public sealed class OutdatedCrate
{
    /// <summary>The crate name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The version in the dependency graph.</summary>
    public string InstalledVersion { get; init; } = string.Empty;

    /// <summary>The latest version on crates.io (the highest stable one, or the highest
    /// pre-release when there is no stable release).</summary>
    public string LatestVersion { get; init; }

    /// <summary>True when the installed version has been yanked from crates.io.</summary>
    public bool IsYanked { get; init; }

    /// <summary>True when <see cref="LatestVersion"/> is newer than
    /// <see cref="InstalledVersion"/>.</summary>
    public bool IsOutdated { get; init; }

    /// <inheritdoc />
    public override string ToString()
        => $"{Name} {InstalledVersion} -> {LatestVersion}" + (IsYanked ? " (installed version yanked)" : string.Empty);
}
