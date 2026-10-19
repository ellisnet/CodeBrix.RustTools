using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// One progress report of <see cref="CrateBuilder.BuildAsync"/>: a stage change, a message
/// within a stage, or a forwarded cargo report.
/// </summary>
public sealed class CrateBuildProgress
{
    /// <summary>The stage that is running.</summary>
    public CrateBuildStage Stage { get; set; }

    /// <summary>What is happening, for a person to read.</summary>
    public string Message { get; set; }

    /// <summary>The target the report concerns, or <c>null</c> for the whole build.</summary>
    public CrateTarget? Target { get; set; }

    /// <summary>The cargo report this one forwards; <c>null</c> unless it comes from
    /// cargo.</summary>
    public CargoProgress CargoProgress { get; set; }

    /// <summary>"[Stage] message", with the target's runtime identifier when set.</summary>
    public override string ToString()
        => "[" + Stage + "]" + (Target.HasValue ? " [" + CrateTargets.Rid(Target.Value) + "]" : string.Empty) + " " + (Message ?? CargoProgress?.ToString() ?? string.Empty);
}
