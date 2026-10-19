using System;
using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Staging;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// The output of <see cref="IWorkspaceStager"/>: the writable copy that is built. Later work
/// packages may add members.
/// </summary>
internal sealed class StagedWorkspace
{
    /// <summary>The staging root folder (under the run folder).</summary>
    public string Directory { get; set; }

    /// <summary>The Cargo.toml to BUILD: the staged crate's, or the facade crate's once one
    /// has been generated (the pipeline updates it).</summary>
    public string ManifestPath { get; set; }

    /// <summary>The staged copy of the upstream crate's Cargo.toml (unchanged by facade
    /// generation).</summary>
    public string UpstreamManifestPath { get; set; }

    /// <summary>The complete source identity.</summary>
    public CrateSourceIdentity Identity { get; set; }

    /// <summary>Whether <see cref="ManifestPath"/> is a generated facade crate.</summary>
    public bool IsFacade { get; set; }

    /// <summary>The name of the crate whose library is built (the facade's when
    /// <see cref="IsFacade"/>): the base of the native file name.</summary>
    public string CrateName { get; set; }

    /// <summary>The staged workspace root's Cargo.toml (the same file as the staged crate's
    /// when the crate is standalone). Build with <c>-p &lt;package&gt;</c> from its folder, or
    /// with <c>--manifest-path</c> <see cref="ManifestPath"/>.</summary>
    public string WorkspaceManifestPath { get; set; }

    /// <summary>Every <c>path = "..."</c> value the stager rewrote in a staged manifest
    /// because it pointed outside the copied tree (empty when none did).</summary>
    public IReadOnlyList<StagedPathRewrite> PathRewrites { get; set; } = Array.Empty<StagedPathRewrite>();

    /// <summary>The git dependencies of the staged manifests, as "name @ url#reference"
    /// ("#reference" only when a rev, tag or branch is named), in the order found. cargo
    /// fetches them itself, so a build needs the network or a cargo cache that already holds
    /// them.</summary>
    public IReadOnlyList<string> GitDependencies { get; set; } = Array.Empty<string>();
}
