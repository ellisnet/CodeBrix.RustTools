namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// One <c>path = "..."</c> value the stager changed in a STAGED Cargo.toml because the path
/// pointed outside the copied tree and its target was copied (vendored) beneath the staging
/// root. The upstream manifest is never changed.
/// </summary>
internal sealed class StagedPathRewrite
{
    /// <summary>The absolute path of the staged Cargo.toml that was rewritten.</summary>
    public string ManifestPath { get; set; }

    /// <summary>The dependency key (the name the manifest uses, for example <c>gamma</c>; for a
    /// <c>[replace]</c> entry the package specification).</summary>
    public string DependencyName { get; set; }

    /// <summary>The path exactly as the upstream manifest wrote it.</summary>
    public string OldPath { get; set; }

    /// <summary>The path written into the staged manifest: relative to the manifest's folder,
    /// with forward slashes.</summary>
    public string NewPath { get; set; }

    /// <summary>"manifest: name old -> new".</summary>
    public override string ToString() => ManifestPath + ": " + DependencyName + " " + OldPath + " -> " + NewPath;
}
