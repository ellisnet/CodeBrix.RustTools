namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// A runtime identifier a crate can be built for. <see cref="CrateTargets"/> maps each value to
/// its .NET runtime identifier, its Rust target triple and its native file name.
/// </summary>
public enum CrateTarget
{
    /// <summary>Windows on x86-64 (<c>win-x64</c>, <c>x86_64-pc-windows-gnullvm</c>).</summary>
    WinX64,

    /// <summary>Windows on ARM64 (<c>win-arm64</c>, <c>aarch64-pc-windows-gnullvm</c>).</summary>
    WinArm64,

    /// <summary>macOS on Intel (<c>osx-x64</c>, <c>x86_64-apple-darwin</c>).</summary>
    OsxX64,

    /// <summary>macOS on Apple silicon (<c>osx-arm64</c>, <c>aarch64-apple-darwin</c>).</summary>
    OsxArm64,

    /// <summary>glibc-based Linux on x86-64 (<c>linux-x64</c>,
    /// <c>x86_64-unknown-linux-gnu</c>).</summary>
    LinuxX64,

    /// <summary>glibc-based Linux on ARM64 (<c>linux-arm64</c>,
    /// <c>aarch64-unknown-linux-gnu</c>).</summary>
    LinuxArm64,

    /// <summary>glibc-based Linux on RISC-V 64 (<c>linux-riscv64</c>,
    /// <c>riscv64gc-unknown-linux-gnu</c>).</summary>
    LinuxRiscV64,

    /// <summary>Android on x86-64 (<c>android-x64</c>, <c>x86_64-linux-android</c>).</summary>
    AndroidX64,

    /// <summary>Android on ARM64 (<c>android-arm64</c>, <c>aarch64-linux-android</c>).</summary>
    AndroidArm64,
}
