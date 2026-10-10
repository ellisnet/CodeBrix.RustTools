using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Dependencies;

/// <summary>
/// A crate that appears in the dependency graph in more than one version.
/// </summary>
public sealed class DuplicateCrate
{
    /// <summary>The crate name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The versions present, lowest first.</summary>
    public IReadOnlyList<string> Versions { get; init; } = Array.Empty<string>();

    /// <inheritdoc />
    public override string ToString() => $"{Name}: {string.Join(", ", Versions)}";
}
