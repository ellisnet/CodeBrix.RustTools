using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One node of the resolved dependency graph (<c>resolve.nodes</c> of <c>cargo metadata</c>).
/// </summary>
public sealed class CargoResolveNode
{
    /// <summary>The package id of this node.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The resolved dependency edges.</summary>
    public IReadOnlyList<CargoResolveDependency> Deps { get; init; } = Array.Empty<CargoResolveDependency>();

    /// <summary>The features enabled on this package.</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <inheritdoc />
    public override string ToString() => Id;
}
