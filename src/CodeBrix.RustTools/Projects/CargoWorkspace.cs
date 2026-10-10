using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// The <c>[workspace]</c> table of a workspace root's Cargo.toml.
/// </summary>
public sealed class CargoWorkspace
{
    /// <summary>The <c>members</c> entries as written (paths, possibly with <c>*</c> globs).</summary>
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();

    /// <summary>The <c>default-members</c> entries as written.</summary>
    public IReadOnlyList<string> DefaultMembers { get; init; } = Array.Empty<string>();

    /// <summary>The <c>exclude</c> entries as written.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = Array.Empty<string>();

    /// <summary>The <c>resolver</c> version (<c>"1"</c>, <c>"2"</c>, <c>"3"</c>), or <c>null</c>.</summary>
    public string Resolver { get; init; }

    /// <summary>The <c>[workspace.package]</c> table members can inherit from, or <c>null</c>.</summary>
    public CargoPackage Package { get; init; }

    /// <summary>The <c>[workspace.dependencies]</c> entries members can inherit with
    /// <c>name.workspace = true</c>.</summary>
    public IReadOnlyList<CargoDependency> Dependencies { get; init; } = Array.Empty<CargoDependency>();
}
