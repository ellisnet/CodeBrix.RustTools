namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The base image of a Docker-mode build for the Linux targets. Host-mode builds use the host's
/// own toolchain and ignore it (a request with <see cref="ManyLinux"/> in host mode is
/// rejected).
/// </summary>
public enum LinuxBaseline
{
    /// <summary>The official <c>rust</c> image (Debian): the binaries need the glibc of the
    /// Debian release the image is built on, or a newer one.</summary>
    RustImage,

    /// <summary>The manylinux Containerfiles built into CrateBuilder, one per Linux
    /// architecture, for a lower glibc floor (binaries that load on older distributions).</summary>
    ManyLinux,
}
