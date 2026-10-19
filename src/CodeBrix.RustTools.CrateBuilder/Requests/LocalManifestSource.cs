using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// A crate on this machine, named by its Cargo.toml (or the folder that holds it). The folder
/// is only read: the build runs in a staged copy.
/// </summary>
public sealed class LocalManifestSource : CrateSource
{
    /// <summary>Creates an empty source; set <see cref="ManifestPath"/>.</summary>
    public LocalManifestSource()
    {
    }

    /// <summary>Creates a source for a Cargo.toml or its folder.</summary>
    /// <param name="manifestPath">The path of the Cargo.toml, or of its folder.</param>
    public LocalManifestSource(string manifestPath)
    {
        ManifestPath = manifestPath;
    }

    /// <summary>The path of the crate's Cargo.toml, or of the folder that holds it
    /// (REQUIRED). A workspace member's manifest may be named; its workspace is staged with
    /// it.</summary>
    public string ManifestPath { get; set; }

    /// <summary>"local: " and the manifest path.</summary>
    public override string ToString() => "local: " + ManifestPath;

    internal override void Validate()
    {
        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            throw new ArgumentException("LocalManifestSource.ManifestPath is required: the path of a Cargo.toml or of its folder.");
        }
    }

    internal override CrateSource Clone() => new LocalManifestSource(ManifestPath);
}
