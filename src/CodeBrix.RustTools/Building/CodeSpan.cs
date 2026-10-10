namespace CodeBrix.RustTools.Building;

/// <summary>
/// A region of source code that a <see cref="CompilerDiagnostic"/> points at. Line and column
/// numbers are 1-based, as rustc reports them.
/// </summary>
public sealed class CodeSpan
{
    /// <summary>
    /// The file, as rustc reports it: usually relative to the package folder
    /// (<c>src/main.rs</c>), absolute for files outside it.
    /// </summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>
    /// The first line of the span (1-based).
    /// </summary>
    public int LineStart { get; init; }

    /// <summary>
    /// The first column of the span (1-based, in characters).
    /// </summary>
    public int ColumnStart { get; init; }

    /// <summary>
    /// The last line of the span (1-based).
    /// </summary>
    public int LineEnd { get; init; }

    /// <summary>
    /// The column just after the span ends (1-based, exclusive).
    /// </summary>
    public int ColumnEnd { get; init; }

    /// <summary>
    /// True for the span the diagnostic is mainly about (rustc's caret <c>^</c>); false for
    /// secondary spans (rustc's dashes <c>-</c>).
    /// </summary>
    public bool IsPrimary { get; init; }

    /// <summary>
    /// The label rustc prints beside the span, or <c>null</c>.
    /// </summary>
    public string Label { get; init; }

    /// <summary>
    /// For a help diagnostic, the replacement text rustc suggests for this span, or <c>null</c>.
    /// </summary>
    public string SuggestedReplacement { get; init; }

    /// <summary>
    /// Returns <c>file:line:column</c>.
    /// </summary>
    public override string ToString() => $"{FileName}:{LineStart}:{ColumnStart}";
}
