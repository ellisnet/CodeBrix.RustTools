using System;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// The output of <see cref="ISourceResolver"/>: where the crate is on this machine. Later work
/// packages may add members.
/// </summary>
internal sealed class ResolvedSource
{
    /// <summary>The folder at the root of the source: the clone, or for a local source the
    /// folder that holds its Cargo.toml (or its workspace root).</summary>
    public string RepositoryDirectory { get; set; }

    /// <summary>The absolute path of the Cargo.toml to build, inside
    /// <see cref="RepositoryDirectory"/>.</summary>
    public string ManifestPath { get; set; }

    /// <summary>The identity known so far (kind, URL, commit, manifest); the stager completes
    /// it.</summary>
    public CrateSourceIdentity Identity { get; set; }

    /// <summary>Whether <see cref="RepositoryDirectory"/> is a clone made or updated for this
    /// build.</summary>
    public bool IsClone { get; set; }

    /// <summary>Run in the CleaningUp stage (deletes a clone when the request asked for it);
    /// <c>null</c> = nothing to clean.</summary>
    public Action CleanUp { get; set; }
}
