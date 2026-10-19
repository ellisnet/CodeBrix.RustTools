namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// One problem found by <see cref="CrateBuilder.CheckReadinessAsync"/>. (Many are made from a
/// CodeBrix.RustTools.Toolchain.ReadinessIssue, the toolchain-level issue; the two names differ,
/// so both namespaces can be imported together.)
/// </summary>
public sealed class BuildReadinessIssue
{
    /// <summary>A stable, dotted code for the kind of problem, for example
    /// <c>toolchain.tool-missing</c>, <c>target.target-missing</c>,
    /// <c>windows.llvm-mingw-missing</c> or <c>docker.engine-unavailable</c>.</summary>
    public string Code { get; set; }

    /// <summary>A description for a person to read.</summary>
    public string Message { get; set; }

    /// <summary>True when the problem prevents any build in this mode (cargo, rustc or the C
    /// toolchain missing in host mode, an unusable engine in Docker mode, no buildable
    /// target); false when it only makes <see cref="Target"/> unbuildable, or is a
    /// warning.</summary>
    public bool IsBlocking { get; set; }

    /// <summary>The target concerned, or <c>null</c> for the whole request.</summary>
    public CrateTarget? Target { get; set; }

    /// <summary>"Blocking:" or "Warning:", the target's runtime identifier when set, and the
    /// message.</summary>
    public override string ToString()
        => (IsBlocking ? "Blocking: " : "Warning: ") + (Target.HasValue ? "[" + CrateTargets.Rid(Target.Value) + "] " : string.Empty) + Message;
}
