using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One declared dependency of a package as reported by <c>cargo metadata</c> (the requirement,
/// not the resolved version; see <see cref="CargoResolveNode"/> for that).
/// </summary>
public sealed class CargoMetadataDependency
{
    /// <summary>The package name of the dependency.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The version requirement (<c>^1.0</c>, <c>*</c>, ...).</summary>
    public string Requirement { get; init; }

    /// <summary>The dependency kind.</summary>
    public CargoDependencyKind Kind { get; init; }

    /// <summary>True when the dependency is optional.</summary>
    public bool Optional { get; init; }

    /// <summary>False when default features are disabled.</summary>
    public bool UsesDefaultFeatures { get; init; } = true;

    /// <summary>The features enabled on the dependency.</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>The platform condition, or <c>null</c>.</summary>
    public string Target { get; init; }

    /// <summary>The local name of a renamed dependency, or <c>null</c>.</summary>
    public string Rename { get; init; }

    /// <summary>The source of the dependency (<c>registry+...</c>, <c>git+...</c>), or
    /// <c>null</c> for path dependencies.</summary>
    public string Source { get; init; }

    /// <summary>The folder of a path dependency, or <c>null</c>.</summary>
    public string Path { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{Name} {Requirement}";
}
