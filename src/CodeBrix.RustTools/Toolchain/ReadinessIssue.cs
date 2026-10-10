namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// One problem found by <see cref="ToolchainReadiness"/>.
/// </summary>
public sealed class ReadinessIssue
{
    /// <summary>The kind of problem.</summary>
    public ReadinessIssueKind Kind { get; init; }

    /// <summary>The tool concerned, or <c>null</c> for target and toolchain issues.</summary>
    public RustTool? Tool { get; init; }

    /// <summary>The target triple or toolchain name concerned, or <c>null</c>.</summary>
    public string Subject { get; init; }

    /// <summary>A human-readable description of the problem.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>True when the problem prevents building; false for warnings (for example a tool
    /// whose status is unknown).</summary>
    public bool IsBlocking { get; init; }

    /// <summary>
    /// The message, prefixed with <c>Blocking:</c> or <c>Warning:</c>.
    /// </summary>
    public override string ToString() => (IsBlocking ? "Blocking: " : "Warning: ") + Message;
}
