using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One package of the graph reported by <c>cargo metadata</c>: a workspace member, a path
/// dependency, or a registry / git crate.
/// </summary>
public sealed class CargoMetadataPackage
{
    /// <summary>cargo's opaque package id (unique within the graph).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The package name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The exact version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>The SPDX license expression, or <c>null</c> when the package declares none.</summary>
    public string License { get; init; }

    /// <summary>The <c>license-file</c> value as declared (relative to the package folder), or
    /// <c>null</c>.</summary>
    public string LicenseFile { get; init; }

    /// <summary>The full path of the package's Cargo.toml.</summary>
    public string ManifestPath { get; init; } = string.Empty;

    /// <summary>The source (<c>registry+https://github.com/rust-lang/crates.io-index</c>,
    /// <c>git+...</c>), or <c>null</c> for workspace members and path dependencies.</summary>
    public string Source { get; init; }

    /// <summary>The description, or <c>null</c>.</summary>
    public string Description { get; init; }

    /// <summary>The repository URL, or <c>null</c>.</summary>
    public string Repository { get; init; }

    /// <summary>The Rust edition.</summary>
    public string Edition { get; init; }

    /// <summary>The minimum supported Rust version, or <c>null</c>.</summary>
    public string RustVersion { get; init; }

    /// <summary>The build targets.</summary>
    public IReadOnlyList<CargoMetadataTarget> Targets { get; init; } = Array.Empty<CargoMetadataTarget>();

    /// <summary>The declared dependencies.</summary>
    public IReadOnlyList<CargoMetadataDependency> Dependencies { get; init; } = Array.Empty<CargoMetadataDependency>();

    /// <summary>The features table (feature name to what it enables).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Features { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>The folder containing <see cref="ManifestPath"/>.</summary>
    public string ManifestDirectory => string.IsNullOrEmpty(ManifestPath) ? null : Path.GetDirectoryName(ManifestPath);

    /// <summary>The full path of the license file, or <c>null</c> when none is declared.</summary>
    public string LicenseFilePath => LicenseFile == null || ManifestDirectory == null
        ? null
        : Path.GetFullPath(Path.Combine(ManifestDirectory, LicenseFile));

    /// <summary>True for workspace members and path dependencies (no source).</summary>
    public bool IsLocal => Source == null;

    /// <summary>True for packages from a registry (crates.io or another).</summary>
    public bool IsRegistry => Source != null && (Source.StartsWith("registry+", StringComparison.Ordinal) || Source.StartsWith("sparse+", StringComparison.Ordinal));

    /// <summary>True for packages from crates.io.</summary>
    public bool IsCratesIo => Source != null
        && (string.Equals(Source, CargoMetadata.CratesIoSource, StringComparison.Ordinal)
            || string.Equals(Source, CargoMetadata.CratesIoSparseSource, StringComparison.Ordinal));

    /// <summary>True for packages from a git repository.</summary>
    public bool IsGit => Source != null && Source.StartsWith("git+", StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => $"{Name} v{Version}";
}
