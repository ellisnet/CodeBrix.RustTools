using System.Collections.Generic;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// Whether one target is buildable in one mode on this machine, and why not.
/// </summary>
internal sealed class TargetEvaluation
{
    public CrateTarget Target { get; set; }

    public bool IsBuildable { get; set; }

    /// <summary>The non-blocking issues that make the target unbuildable (Target set).</summary>
    public List<BuildReadinessIssue> Issues { get; } = new List<BuildReadinessIssue>();

    /// <summary>The Rust install guidance that would make it buildable.</summary>
    public List<InstallGuidance> Guidance { get; } = new List<InstallGuidance>();
}
