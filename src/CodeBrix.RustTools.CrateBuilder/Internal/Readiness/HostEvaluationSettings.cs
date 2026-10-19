using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// What the host-mode target evaluation reads besides the detected toolchain (the folders a
/// consumer named; everything else - LLVM_MINGW_HOME, PATH - is read by CodeBrix.RustTools).
/// </summary>
internal sealed class HostEvaluationSettings
{
    /// <summary><see cref="CrateBuilderOptions.LlvmMingwDirectory"/>.</summary>
    public string LlvmMingwDirectory { get; set; }

    /// <summary><see cref="CrateBuilderOptions.MacOSSysrootDirectory"/>.</summary>
    public string MacOSSysrootDirectory { get; set; }

    /// <summary>Where the GNU cross tools are; <c>null</c> = PATH (as CodeBrix.RustTools'
    /// LinuxLinkProfile.ForTriple without a bin folder).</summary>
    public string LinuxCrossToolDirectory { get; set; }

    /// <summary>The host's operating system; <c>null</c> = the detected toolchain's.</summary>
    public OperatingSystemKind? HostOperatingSystem { get; set; }

    public static HostEvaluationSettings FromOptions(CrateBuilderOptions options) => new HostEvaluationSettings
    {
        LlvmMingwDirectory = options?.LlvmMingwDirectory,
        MacOSSysrootDirectory = options?.MacOSSysrootDirectory,
    };
}
