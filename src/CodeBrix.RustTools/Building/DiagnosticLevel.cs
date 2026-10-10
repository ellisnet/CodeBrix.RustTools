namespace CodeBrix.RustTools.Building;

/// <summary>
/// The severity of a <see cref="CompilerDiagnostic"/>, as reported by rustc or cargo.
/// </summary>
public enum DiagnosticLevel
{
    /// <summary>An error; the build fails.</summary>
    Error,

    /// <summary>A warning; the build continues.</summary>
    Warning,

    /// <summary>A note attached to another diagnostic.</summary>
    Note,

    /// <summary>A help message (often with a suggested fix) attached to another diagnostic.</summary>
    Help,

    /// <summary>A rustc "failure-note" (for example "For more information about this error,
    /// try `rustc --explain E0425`").</summary>
    FailureNote,

    /// <summary>An internal compiler error (a rustc bug).</summary>
    Ice,
}
