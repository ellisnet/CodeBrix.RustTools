namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Whether a built binary may be distributed. Recorded per target in the build record; nothing
/// else is enforced.
/// </summary>
public enum ArtifactProvenance
{
    /// <summary>For development and testing only: a macOS binary linked without Apple's
    /// toolchain and SDK (in a container, or on a host that is not a Mac through the macOS link
    /// profile). It must not be distributed or used in production.</summary>
    DevelopmentBuild,

    /// <summary>Built with the platform's own toolchain (for macOS: on a Mac), so it may be
    /// distributed under the crate's and its dependencies' licences.</summary>
    Distributable,
}
