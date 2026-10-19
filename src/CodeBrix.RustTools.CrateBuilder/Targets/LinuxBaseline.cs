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
    /// architecture, for a lower glibc floor (binaries that load on older distributions):
    /// glibc 2.28 for linux-x64 and linux-arm64, 2.39 for linux-riscv64. Linux targets only.
    /// An image of another architecture than the Docker engine's own (linux-arm64 and
    /// linux-riscv64 on an x86-64 engine) runs under qemu-user emulation: the engine's host
    /// needs a qemu-user binfmt handler for it (the build probes for it and reports the target
    /// as not buildable, with guidance, when it is missing), and the build is many times slower
    /// than a native one.</summary>
    ManyLinux,
}
