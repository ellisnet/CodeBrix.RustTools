namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// The platforms a Rust project can be compiled for inside the Linux build container.
/// <see cref="CrossTargetInfo"/> describes each one: its Rust target triple, its .NET runtime
/// identifier, its file naming and what it needs.
/// </summary>
public enum CrossTarget
{
    /// <summary>The build container's own Linux architecture, whatever the engine is: resolved
    /// to <see cref="LinuxX64"/> on an x86-64 engine, <see cref="LinuxArm64"/> on an ARM64 engine
    /// (Docker Desktop on Apple Silicon) or <see cref="LinuxRiscV64"/> on a RISC-V engine, before
    /// anything is built; results and retrieved files carry the resolved target. The default
    /// target. Needs no extra image layer.</summary>
    LinuxNative,

    /// <summary>64-bit x86 Linux (<c>x86_64-unknown-linux-gnu</c>, RID <c>linux-x64</c>): the
    /// container's own target on an x86-64 engine; on any other engine it is cross-compiled
    /// with Debian's <c>gcc-x86-64-linux-gnu</c> cross toolchain (the Linux x86-64 image layer).</summary>
    LinuxX64,

    /// <summary>64-bit ARM Linux (<c>aarch64-unknown-linux-gnu</c>, RID <c>linux-arm64</c>): the
    /// container's own target on an ARM64 engine; on any other engine it is cross-compiled with
    /// Debian's <c>gcc-aarch64-linux-gnu</c> cross toolchain (the Linux ARM64 image layer).</summary>
    LinuxArm64,

    /// <summary>64-bit RISC-V Linux (<c>riscv64gc-unknown-linux-gnu</c>, RID
    /// <c>linux-riscv64</c>): cross-compiled with Debian's <c>gcc-riscv64-linux-gnu</c> cross
    /// toolchain (the Linux RISC-V image layer), or native on a RISC-V engine.</summary>
    LinuxRiscV64,

    /// <summary>64-bit x86 Windows (<c>x86_64-pc-windows-gnullvm</c>, RID <c>win-x64</c>), built
    /// with llvm-mingw (clang, lld and the mingw-w64 runtime, all open source) against Windows'
    /// Universal C Runtime: Windows 10 and later. No Microsoft SDK, compiler or runtime is
    /// involved. Needs the Windows image layer; works on any engine architecture.</summary>
    WindowsX64Gnullvm,

    /// <summary>64-bit ARM Windows (<c>aarch64-pc-windows-gnullvm</c>, RID <c>win-arm64</c>),
    /// built like <see cref="WindowsX64Gnullvm"/> with the same layer.</summary>
    WindowsArm64Gnullvm,

    /// <summary>64-bit ARM Android (<c>aarch64-linux-android</c>, ABI <c>arm64-v8a</c>),
    /// built with the Android NDK's clang. Needs the Android image layer and an x86-64 engine.</summary>
    AndroidArm64,

    /// <summary>64-bit x86 Android (<c>x86_64-linux-android</c>, ABI <c>x86_64</c>), used by
    /// emulators. Needs the Android image layer and an x86-64 engine.</summary>
    AndroidX64,

    /// <summary>Apple Silicon macOS (<c>aarch64-apple-darwin</c>), development and testing
    /// only; see <see cref="CrossTargetInfo.MacOSDevelopmentOnlyNotice"/>. Needs the macOS
    /// image layer.</summary>
    MacOSArm64,

    /// <summary>Intel macOS (<c>x86_64-apple-darwin</c>), development and testing only; see
    /// <see cref="CrossTargetInfo.MacOSDevelopmentOnlyNotice"/>. Needs the macOS image layer.</summary>
    MacOSX64,

    /// <summary>A universal (fat) macOS file holding both the <see cref="MacOSArm64"/> and the
    /// <see cref="MacOSX64"/> slice, made by building the two and joining them with
    /// <c>CodeBrix.RustTools.Building.MacOSUniversalBinary</c>. A retrieval target: it cannot be
    /// passed to a single cargo run. NuGet runtime identifier <c>osx</c>, the portable macOS RID
    /// that both <c>osx-arm64</c> and <c>osx-x64</c> fall back to. Development and testing only,
    /// like its slices.</summary>
    MacOSUniversal,
}
