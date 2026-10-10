using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// One compiled target reported by cargo (a <c>compiler-artifact</c> message): a library, a
/// binary, an example, a test harness or a build script.
/// </summary>
public sealed class BuildArtifact
{
    /// <summary>
    /// The cargo package id (for example
    /// <c>registry+https://github.com/rust-lang/crates.io-index#serde@1.0.228</c>).
    /// </summary>
    public string PackageId { get; init; } = string.Empty;

    /// <summary>
    /// The package name.
    /// </summary>
    public string PackageName { get; init; } = string.Empty;

    /// <summary>
    /// The package version, or <c>null</c> when the id does not carry one.
    /// </summary>
    public string PackageVersion { get; init; }

    /// <summary>
    /// The path of the package's Cargo.toml, or <c>null</c>.
    /// </summary>
    public string ManifestPath { get; init; }

    /// <summary>
    /// The target name (for a binary, the executable's base name).
    /// </summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>
    /// The target kinds as cargo names them (<c>bin</c>, <c>lib</c>, <c>rlib</c>,
    /// <c>proc-macro</c>, <c>example</c>, <c>test</c>, <c>bench</c>, <c>custom-build</c>, ...).
    /// </summary>
    public IReadOnlyList<string> Kinds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The features enabled for this target.
    /// </summary>
    public IReadOnlyList<string> Features { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The full path of the runnable executable, or <c>null</c> for libraries and build
    /// scripts.
    /// </summary>
    public string Executable { get; init; }

    /// <summary>
    /// Every file cargo produced for the target (rlib, rmeta, executable, ...).
    /// </summary>
    public IReadOnlyList<string> Filenames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The compiler settings used.
    /// </summary>
    public ArtifactProfile Profile { get; init; } = new ArtifactProfile();

    /// <summary>
    /// True when cargo reused an up-to-date artifact instead of compiling it again.
    /// </summary>
    public bool Fresh { get; init; }

    /// <summary>
    /// True for a package's build script (kind <c>custom-build</c>).
    /// </summary>
    public bool IsBuildScript => Kinds.Contains("custom-build");

    /// <summary>
    /// True when the artifact is a program a user would run: it has an executable and is not a
    /// build script or a test harness.
    /// </summary>
    public bool IsRunnable => Executable != null && !IsBuildScript && !Profile.Test;

    /// <summary>
    /// Returns <c>package/target (kinds)</c>.
    /// </summary>
    public override string ToString() => $"{PackageName}/{TargetName} ({string.Join(", ", Kinds)})";
}
