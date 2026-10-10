using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One dependency declared in a Cargo.toml dependency table (<c>[dependencies]</c>,
/// <c>[dev-dependencies]</c>, <c>[build-dependencies]</c>, their
/// <c>[target.'cfg(...)'.*]</c> forms, or <c>[workspace.dependencies]</c>).
/// </summary>
public sealed class CargoDependency
{
    /// <summary>The key the dependency is declared under (the name the code uses).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The real package name: the <c>package</c> key of a renamed dependency, otherwise
    /// <see cref="Name"/>.</summary>
    public string PackageName { get; init; } = string.Empty;

    /// <summary>The version requirement (<c>"1.0"</c>, <c>"^0.4"</c>, ...), or <c>null</c> when
    /// none is given (path or git dependency, or inherited from the workspace).</summary>
    public string VersionRequirement { get; init; }

    /// <summary>The <c>path</c> of a local path dependency (as written), or <c>null</c>.</summary>
    public string Path { get; init; }

    /// <summary>The <c>git</c> repository URL, or <c>null</c>.</summary>
    public string Git { get; init; }

    /// <summary>The git <c>branch</c>, or <c>null</c>.</summary>
    public string Branch { get; init; }

    /// <summary>The git <c>tag</c>, or <c>null</c>.</summary>
    public string Tag { get; init; }

    /// <summary>The git <c>rev</c>, or <c>null</c>.</summary>
    public string Rev { get; init; }

    /// <summary>The alternative <c>registry</c> name, or <c>null</c> for crates.io.</summary>
    public string Registry { get; init; }

    /// <summary>The features enabled on the dependency.</summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>True when the dependency is <c>optional = true</c> (enabled by a feature).</summary>
    public bool Optional { get; init; }

    /// <summary>False when the dependency sets <c>default-features = false</c>.</summary>
    public bool DefaultFeatures { get; init; } = true;

    /// <summary>The dependency table the entry comes from.</summary>
    public CargoDependencyKind Kind { get; init; }

    /// <summary>The platform condition of a <c>[target.'...'.dependencies]</c> entry
    /// (for example <c>cfg(windows)</c> or a target triple), or <c>null</c>.</summary>
    public string Target { get; init; }

    /// <summary>True when the entry is <c>name.workspace = true</c> / <c>{ workspace = true }</c>,
    /// meaning the details come from <c>[workspace.dependencies]</c>.</summary>
    public bool IsWorkspaceInherited { get; init; }

    /// <summary>True for a local path dependency.</summary>
    public bool IsPath => Path != null;

    /// <summary>True for a git dependency.</summary>
    public bool IsGit => Git != null;

    /// <summary>True for a dependency resolved from a registry (no path, git or workspace
    /// inheritance).</summary>
    public bool IsRegistry => Path == null && Git == null && !IsWorkspaceInherited;

    /// <inheritdoc />
    public override string ToString()
    {
        var detail = IsWorkspaceInherited ? "workspace"
            : Path != null ? "path " + Path
            : Git != null ? "git " + Git
            : VersionRequirement ?? "*";
        return $"{Name} ({detail})";
    }
}
