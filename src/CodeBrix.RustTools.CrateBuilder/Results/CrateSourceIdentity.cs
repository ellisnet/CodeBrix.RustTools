using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Exactly which source was built: where it came from, which commit, which manifest, and a
/// hash of the files that were staged. Recorded in the build record.
/// </summary>
public sealed class CrateSourceIdentity
{
    /// <summary>The value of <see cref="Kind"/> for a <see cref="LocalManifestSource"/>.</summary>
    public const string LocalKind = "local";

    /// <summary>The value of <see cref="Kind"/> for a <see cref="GitRepositorySource"/>.</summary>
    public const string GitKind = "git";

    /// <summary><c>local</c> or <c>git</c>.</summary>
    public string Kind { get; set; }

    /// <summary>The repository URL (git), else <c>null</c>.</summary>
    public string Url { get; set; }

    /// <summary>The full SHA of the commit that was checked out (git), else <c>null</c>.</summary>
    public string Commit { get; set; }

    /// <summary>The branch that was asked for (git), else <c>null</c>.</summary>
    public string Branch { get; set; }

    /// <summary>The tag that was asked for (git), else <c>null</c>.</summary>
    public string Tag { get; set; }

    /// <summary>Git: true when <see cref="GitRepositorySource.CloneDirectory"/> already held a
    /// clone of the same remote, which was fetched and checked out again instead of cloned;
    /// false for a fresh clone and for a local source.</summary>
    public bool CloneReused { get; set; }

    /// <summary>The manifest that was asked for: relative to the repository root (git), or
    /// the absolute path (local).</summary>
    public string ManifestPath { get; set; }

    /// <summary>The absolute path of the Cargo.toml that was actually read.</summary>
    public string LocalManifestPath { get; set; }

    /// <summary>The SHA-256 (64 lowercase hex digits) over the staged crate's files: each
    /// file's path relative to the staging root and its content, in ordinal path
    /// order.</summary>
    public string SourceHash { get; set; }

    /// <summary>The crate's package name.</summary>
    public string CrateName { get; set; }

    /// <summary>The crate's package version.</summary>
    public string CrateVersion { get; set; }

    /// <summary>The crate types declared in the manifest's <c>[lib] crate-type</c> (for
    /// example <c>cdylib</c>, <c>rlib</c>); empty = cargo's default, an rlib.</summary>
    public IReadOnlyList<string> CrateTypes { get; set; } = Array.Empty<string>();
}
