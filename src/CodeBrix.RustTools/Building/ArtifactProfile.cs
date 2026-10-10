namespace CodeBrix.RustTools.Building;

/// <summary>
/// The compiler settings an artifact was built with (the <c>profile</c> object of cargo's
/// <c>compiler-artifact</c> message).
/// </summary>
public sealed class ArtifactProfile
{
    /// <summary>
    /// The optimisation level (<c>0</c>, <c>1</c>, <c>2</c>, <c>3</c>, <c>s</c> or <c>z</c>).
    /// </summary>
    public string OptLevel { get; init; } = string.Empty;

    /// <summary>
    /// The debug-info setting as cargo reports it (<c>0</c>, <c>1</c>, <c>2</c>,
    /// <c>line-tables-only</c>, ...).
    /// </summary>
    public string DebugInfo { get; init; } = string.Empty;

    /// <summary>
    /// True when <c>debug_assert!</c> checks are compiled in.
    /// </summary>
    public bool DebugAssertions { get; init; }

    /// <summary>
    /// True when integer overflow checks are compiled in.
    /// </summary>
    public bool OverflowChecks { get; init; }

    /// <summary>
    /// True when the artifact is a test harness binary (built by <c>cargo test</c>).
    /// </summary>
    public bool Test { get; init; }
}
