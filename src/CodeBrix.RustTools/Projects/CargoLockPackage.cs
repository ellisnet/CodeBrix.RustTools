using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One <c>[[package]]</c> entry of a Cargo.lock file.
/// </summary>
public sealed class CargoLockPackage
{
    /// <summary>The package name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The exact locked version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>The source (<c>registry+https://...</c>, <c>git+https://...#rev</c>), or
    /// <c>null</c> for workspace members and path dependencies.</summary>
    public string Source { get; init; }

    /// <summary>The registry checksum, or <c>null</c>.</summary>
    public string Checksum { get; init; }

    /// <summary>The dependency entries as written: <c>name</c>, <c>name version</c>, or
    /// <c>name version (source)</c> when the name alone is ambiguous.</summary>
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>True when the package comes from a registry.</summary>
    public bool IsRegistry => Source != null && (Source.StartsWith("registry+", StringComparison.Ordinal) || Source.StartsWith("sparse+", StringComparison.Ordinal));

    /// <summary>True when the package comes from a git repository.</summary>
    public bool IsGit => Source != null && Source.StartsWith("git+", StringComparison.Ordinal);

    /// <summary>True for workspace members and path dependencies (no source).</summary>
    public bool IsLocal => Source == null;

    /// <summary>
    /// Returns the names of the dependencies (the first word of each entry).
    /// </summary>
    public IReadOnlyList<string> GetDependencyNames()
    {
        var names = new List<string>();
        foreach (var entry in Dependencies)
        {
            var space = entry.IndexOf(' ');
            names.Add(space < 0 ? entry : entry.Substring(0, space));
        }

        return names;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} v{Version}";
}
