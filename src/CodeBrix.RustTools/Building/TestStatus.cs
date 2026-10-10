namespace CodeBrix.RustTools.Building;

/// <summary>
/// The outcome of one test reported by the libtest harness.
/// </summary>
public enum TestStatus
{
    /// <summary>The test passed (<c>... ok</c>).</summary>
    Passed,

    /// <summary>The test failed (<c>... FAILED</c>).</summary>
    Failed,

    /// <summary>The test was not run (<c>... ignored</c>).</summary>
    Ignored,

    /// <summary>A benchmark was measured (<c>... bench:</c>).</summary>
    Measured,
}
