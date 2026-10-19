namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Something the extractor could not read or chose to skip.
/// </summary>
internal sealed class RustDiagnostic
{
    /// <summary>Info, warning or error.</summary>
    public RustDiagnosticSeverity Severity { get; set; }

    /// <summary>A stable code (<c>parse_error</c>, <c>module_file_not_found</c>,
    /// <c>item_macro_not_expanded</c>, <c>unsupported_item</c>, <c>unresolved_reexport</c>,
    /// <c>file_unreadable</c>, <c>no_library_target</c>).</summary>
    public string Code { get; set; }

    /// <summary>Human-readable text.</summary>
    public string Message { get; set; }

    /// <summary>The file, relative; <c>null</c> when not tied to one.</summary>
    public string File { get; set; }

    /// <summary>The 1-based line, when known.</summary>
    public int? Line { get; set; }

    /// <summary>The 1-based column, when known.</summary>
    public int? Column { get; set; }

    /// <summary>"severity code: message (file:line)".</summary>
    public override string ToString()
    {
        var location = File == null ? string.Empty : " (" + File + (Line.HasValue ? ":" + Line.Value : string.Empty) + ")";
        return Severity.ToString().ToLowerInvariant() + " " + Code + ": " + Message + location;
    }
}
