using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// One error, warning or note from rustc, clippy, a build script or cargo, parsed from cargo's
/// <c>--message-format=json</c> output (or from cargo's standard error for build-script and
/// cargo-level messages).
/// </summary>
public sealed class CompilerDiagnostic
{
    /// <summary>
    /// The severity.
    /// </summary>
    public DiagnosticLevel Level { get; init; }

    /// <summary>
    /// The lint or error code (<c>E0425</c>, <c>unused_imports</c>, <c>clippy::needless_return</c>),
    /// or <c>null</c> when the message has none.
    /// </summary>
    public string Code { get; init; }

    /// <summary>
    /// The one-line message (for example <c>unused variable: `x`</c>).
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The full human-readable text exactly as cargo would print it (plain text, no colour
    /// codes), including the source excerpt; empty for child diagnostics, which rustc renders
    /// as part of their parent.
    /// </summary>
    public string Rendered { get; init; } = string.Empty;

    /// <summary>
    /// The source regions the diagnostic points at (may be empty).
    /// </summary>
    public IReadOnlyList<CodeSpan> Spans { get; init; } = Array.Empty<CodeSpan>();

    /// <summary>
    /// Attached notes and help messages.
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> Children { get; init; } = Array.Empty<CompilerDiagnostic>();

    /// <summary>
    /// The cargo package id of the crate being compiled (for example
    /// <c>path+file:///src/app#0.1.0</c>), or <c>null</c> for cargo-level messages.
    /// </summary>
    public string PackageId { get; init; }

    /// <summary>
    /// The package name (from the package id or the build-script warning prefix), or <c>null</c>.
    /// </summary>
    public string PackageName { get; init; }

    /// <summary>
    /// The name of the target being compiled (the lib, bin, test or <c>build-script-build</c>),
    /// or <c>null</c>.
    /// </summary>
    public string TargetName { get; init; }

    /// <summary>
    /// Where the diagnostic came from.
    /// </summary>
    public DiagnosticOrigin Source { get; init; }

    /// <summary>
    /// The first primary span (or the first span when none is marked primary); <c>null</c> when
    /// there are no spans.
    /// </summary>
    public CodeSpan PrimarySpan => Spans.FirstOrDefault(s => s.IsPrimary) ?? Spans.FirstOrDefault();

    /// <summary>
    /// True for <see cref="DiagnosticLevel.Error"/> and <see cref="DiagnosticLevel.Ice"/>.
    /// </summary>
    public bool IsError => Level == DiagnosticLevel.Error || Level == DiagnosticLevel.Ice;

    /// <summary>
    /// True for <see cref="DiagnosticLevel.Warning"/>.
    /// </summary>
    public bool IsWarning => Level == DiagnosticLevel.Warning;

    /// <summary>
    /// Returns <see cref="Rendered"/>, or <c>level: message</c> when nothing was rendered.
    /// </summary>
    public override string ToString()
        => string.IsNullOrEmpty(Rendered) ? $"{LevelText(Level)}: {Message}" : Rendered;

    internal static string LevelText(DiagnosticLevel level) => level switch
    {
        DiagnosticLevel.Error => "error",
        DiagnosticLevel.Warning => "warning",
        DiagnosticLevel.Note => "note",
        DiagnosticLevel.Help => "help",
        DiagnosticLevel.FailureNote => "failure-note",
        DiagnosticLevel.Ice => "error: internal compiler error",
        _ => level.ToString().ToLowerInvariant(),
    };
}
