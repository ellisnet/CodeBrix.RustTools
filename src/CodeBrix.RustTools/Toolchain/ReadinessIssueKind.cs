namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The kinds of problem <see cref="ToolchainReadiness"/> reports.
/// </summary>
public enum ReadinessIssueKind
{
    /// <summary>A required tool is not installed.</summary>
    ToolMissing,

    /// <summary>A tool is installed but older than <see cref="ToolchainRequirements.MinimumRustVersion"/>.</summary>
    ToolOutdated,

    /// <summary>A tool could not be probed (its status is undetermined); it may or may not work.
    /// Always non-blocking, so a build is still attempted.</summary>
    ToolStatusUnknown,

    /// <summary>A required compilation target is not installed.</summary>
    TargetMissing,

    /// <summary>The toolchain named by the project's <c>rust-toolchain.toml</c> is not installed.</summary>
    ToolchainNotInstalled,

    /// <summary>An Android NDK is required but none was found, or this machine cannot run one.</summary>
    AndroidNdkMissing,

    /// <summary>A GNU cross tool that a Linux cross build of another processor architecture
    /// links with (the <c>&lt;prefix&gt;-gcc</c> or <c>&lt;prefix&gt;-ar</c> of a
    /// <c>LinuxLinkProfile</c>) is not on PATH or in the profile's folder. The issue's
    /// <see cref="ReadinessIssue.Subject"/> is the target triple.</summary>
    LinuxCrossToolMissing,

    /// <summary>The llvm-mingw clang wrapper a Windows gnullvm target links with
    /// (<see cref="ToolchainRequirements.RequiredWindowsGnullvm"/>) was not found in the named
    /// folder, under <c>LLVM_MINGW_HOME</c> or on PATH. The issue's
    /// <see cref="ReadinessIssue.Subject"/> is the target triple.</summary>
    LlvmMingwMissing,
}
