namespace CodeBrix.RustTools.Building;

/// <summary>
/// One test from a <c>cargo test</c> run.
/// </summary>
public sealed class TestOutcome
{
    /// <summary>
    /// The test name as libtest prints it (for example <c>tests::parses_input</c>, or
    /// <c>src/lib.rs - add (line 12)</c> for a doc test).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The outcome.
    /// </summary>
    public TestStatus Status { get; init; }

    /// <summary>
    /// For an ignored test, the reason given with <c>#[ignore = "..."]</c>; otherwise
    /// <c>null</c>.
    /// </summary>
    public string IgnoreReason { get; init; }

    /// <summary>
    /// For a failed test, the captured output libtest printed for it (panic message, prints);
    /// otherwise <c>null</c>.
    /// </summary>
    public string Output { get; internal set; }

    /// <summary>
    /// True when <see cref="Status"/> is <see cref="TestStatus.Passed"/>.
    /// </summary>
    public bool Passed => Status == TestStatus.Passed;

    /// <summary>
    /// True when <see cref="Status"/> is <see cref="TestStatus.Failed"/>.
    /// </summary>
    public bool Failed => Status == TestStatus.Failed;

    /// <summary>
    /// True when <see cref="Status"/> is <see cref="TestStatus.Ignored"/>.
    /// </summary>
    public bool Ignored => Status == TestStatus.Ignored;

    /// <summary>
    /// Returns <c>name: status</c>.
    /// </summary>
    public override string ToString() => $"{Name}: {Status}";
}
