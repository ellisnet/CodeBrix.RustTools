namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// Which built files to copy out of a build container, and where to put them on the host.
/// </summary>
public sealed class ArtifactRetrievalOptions
{
    /// <summary>
    /// The host folder that receives the files (created when missing). Required.
    /// </summary>
    public string HostDirectory { get; set; }

    /// <summary>How the files are arranged under <see cref="HostDirectory"/>. Default
    /// <see cref="ArtifactLayout.ByTarget"/>.</summary>
    public ArtifactLayout Layout { get; set; } = ArtifactLayout.ByTarget;

    /// <summary>Whether programs (<c>bin</c> and <c>example</c> targets) are copied. Default
    /// <c>true</c>.</summary>
    public bool IncludeExecutables { get; set; } = true;

    /// <summary>Whether shared libraries (<c>.so</c>, <c>.dll</c>) are copied. Default
    /// <c>true</c>.</summary>
    public bool IncludeSharedLibraries { get; set; } = true;

    /// <summary>Whether static libraries (<c>.a</c>, including the Windows GNU <c>.dll.a</c>
    /// import library) are copied. Default <c>false</c>.</summary>
    public bool IncludeStaticLibraries { get; set; }

    /// <summary>Whether Rust libraries (<c>.rlib</c>) are copied. Default
    /// <c>false</c>.</summary>
    public bool IncludeRlibs { get; set; }

    /// <summary>Whether files already in the host folder are replaced. Default <c>true</c>;
    /// when <c>false</c> an existing file makes the retrieval fail with
    /// <see cref="ArtifactRetrievalException"/>.</summary>
    public bool Overwrite { get; set; } = true;

    /// <summary>Returns a copy of these options.</summary>
    public ArtifactRetrievalOptions Clone() => (ArtifactRetrievalOptions)MemberwiseClone();
}
