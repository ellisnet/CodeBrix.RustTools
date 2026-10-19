namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The severity of a <see cref="RustDiagnostic"/> (JSON: snake_case).
/// </summary>
internal enum RustDiagnosticSeverity
{
    /// <summary>For information (for example an item macro that was not expanded).</summary>
    Info,

    /// <summary>Something was skipped or could not be resolved.</summary>
    Warning,

    /// <summary>A file could not be read or parsed; its items are missing.</summary>
    Error,
}
