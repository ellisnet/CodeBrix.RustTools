namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Where a <see cref="CrateBuilder"/> compiles the crate. One mode applies to the whole request.
/// </summary>
public enum BuildMode
{
    /// <summary>With the Rust toolchain installed on this machine (CodeBrix.RustTools): the
    /// host's own target natively, other targets through the link profiles that package
    /// provides (GNU cross toolchains, the Android NDK, llvm-mingw, the macOS link profile).</summary>
    Host,

    /// <summary>Inside a Linux Docker container (CodeBrix.RustTools.Docker): the machine needs
    /// a reachable Docker engine and no Rust tools.</summary>
    Docker,
}
