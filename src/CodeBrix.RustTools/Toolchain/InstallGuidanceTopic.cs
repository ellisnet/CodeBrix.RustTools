namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// What a piece of <see cref="InstallGuidance"/> helps the user do.
/// </summary>
public enum InstallGuidanceTopic
{
    /// <summary>Install Rust (rustup, cargo and rustc) from scratch.</summary>
    InstallRust,

    /// <summary>Update an existing rustup-managed installation (<c>rustup update</c>).</summary>
    UpdateRust,

    /// <summary>Replace or supplement an operating-system-packaged Rust that cannot be updated
    /// independently with a rustup-managed installation.</summary>
    InstallRustup,

    /// <summary>Add missing rustup components such as clippy and rustfmt.</summary>
    InstallComponents,

    /// <summary>Install the C compiler and linker that rustc needs (Visual Studio Build Tools,
    /// Xcode Command Line Tools, or build-essential).</summary>
    InstallCToolchain,

    /// <summary>Add missing compilation targets (<c>rustup target add</c>).</summary>
    AddTargets,

    /// <summary>Install a specific toolchain requested by a <c>rust-toolchain.toml</c>
    /// (<c>rustup toolchain install</c>).</summary>
    InstallToolchain,

    /// <summary>Install the Android NDK (Android Studio's SDK Manager or <c>sdkmanager</c>) and
    /// point <c>ANDROID_NDK_HOME</c> at it.</summary>
    InstallAndroidNdk,

    /// <summary>Install the GNU cross toolchain (cross <c>gcc</c>, <c>g++</c>, binutils and
    /// the target's glibc headers) and the target's standard library for a Linux target of
    /// another processor architecture.</summary>
    InstallLinuxCrossTools,

    /// <summary>Unpack an llvm-mingw release (clang, lld and the mingw-w64 runtime) and the
    /// Windows gnullvm targets' standard libraries, for building Windows targets
    /// (<c>WindowsGnullvmLinkProfile</c>), and name its folder with <c>LLVM_MINGW_HOME</c>.</summary>
    InstallLlvmMingw,
}
