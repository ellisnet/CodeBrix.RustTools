namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// What happened to one requested target.
/// </summary>
public enum CrateTargetOutcome
{
    /// <summary>cargo built the target and its native files were copied to the output
    /// folder.</summary>
    Compiled,

    /// <summary>cargo ran and failed, or its output could not be retrieved or verified; see
    /// <see cref="CrateTargetResult.Message"/> and <see cref="CrateTargetResult.CargoResult"/>.</summary>
    Failed,

    /// <summary>The readiness check found that this machine cannot build the target in the
    /// requested mode (a missing standard library, cross toolchain, NDK or engine capability);
    /// <see cref="CrateTargetResult.Guidance"/> says what to install. Nothing was run for
    /// it.</summary>
    NotBuildableInThisMode,

    /// <summary>The target was not attempted because an earlier stage failed or the build
    /// stopped before reaching it.</summary>
    Skipped,
}
