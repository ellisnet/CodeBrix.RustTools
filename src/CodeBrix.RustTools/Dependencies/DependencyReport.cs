using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Dependencies;

/// <summary>
/// The dependency-health picture of a project, as returned by
/// <see cref="RustWorkbench.InspectDependenciesAsync"/>: the resolved graph, a
/// <c>cargo tree</c>-style listing, crates present in several versions and, when requested,
/// outdated or yanked crates.
/// </summary>
public sealed class DependencyReport
{
    /// <summary>The <c>cargo metadata</c> output the report was built from.</summary>
    public CargoMetadata Metadata { get; init; }

    /// <summary>An inspector over <see cref="Metadata"/>, for further questions (for example a
    /// shallower tree).</summary>
    public DependencyInspector Inspector { get; init; }

    /// <summary>Crates that appear in more than one version in the graph.</summary>
    public IReadOnlyList<DuplicateCrate> DuplicateVersions { get; init; } = Array.Empty<DuplicateCrate>();

    /// <summary>crates.io crates with a newer release or whose installed version was yanked.
    /// Empty when <see cref="OutdatedChecked"/> is false.</summary>
    public IReadOnlyList<OutdatedCrate> Outdated { get; init; } = Array.Empty<OutdatedCrate>();

    /// <summary>True when crates.io was asked about every registry crate (the
    /// <c>checkOutdated</c> argument was true).</summary>
    public bool OutdatedChecked { get; init; }

    /// <summary>The full dependency tree (dev-dependencies excluded), one line per
    /// entry, joined with <c>\n</c>.</summary>
    public string TreeText { get; init; } = string.Empty;

    /// <summary>
    /// Returns a one-line summary: package, duplicate and outdated counts.
    /// </summary>
    public override string ToString()
        => $"{Metadata?.Packages.Count ?? 0} package(s), {DuplicateVersions.Count} duplicated, "
            + (OutdatedChecked ? $"{Outdated.Count} outdated or yanked" : "outdated not checked");
}
