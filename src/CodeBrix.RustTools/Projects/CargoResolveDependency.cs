using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One resolved dependency edge of a <see cref="CargoResolveNode"/>.
/// </summary>
public sealed class CargoResolveDependency
{
    /// <summary>The name the depending crate uses for the dependency (its library name, after
    /// any rename).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The package id of the resolved dependency (cargo's <c>pkg</c> field).</summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>How the edge is used (normal / dev / build, per platform).</summary>
    public IReadOnlyList<CargoResolveDependencyKind> DepKinds { get; init; } = Array.Empty<CargoResolveDependencyKind>();

    /// <summary>True when any use of the edge is of the given kind.</summary>
    /// <param name="kind">The kind to test.</param>
    public bool HasKind(CargoDependencyKind kind) => DepKinds.Count == 0 ? kind == CargoDependencyKind.Normal : DepKinds.Any(k => k.Kind == kind);

    /// <summary>True when the edge is used only as a dev-dependency.</summary>
    public bool IsDevOnly => DepKinds.Count > 0 && DepKinds.All(k => k.Kind == CargoDependencyKind.Dev);

    /// <inheritdoc />
    public override string ToString() => $"{Name} -> {PackageId}";
}
