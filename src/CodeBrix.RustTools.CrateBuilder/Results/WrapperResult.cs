using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The wrapper project that was written.
/// </summary>
public sealed class WrapperResult
{
    /// <summary>The path of the wrapper's .csproj.</summary>
    public string ProjectPath { get; set; }

    /// <summary>The folder that holds the wrapper project.</summary>
    public string ProjectDirectory { get; set; }

    /// <summary>The wrapper's entry class: the public static class that holds the crate's free
    /// functions (the Pascal-cased library name, or that name plus "Library" when the root
    /// namespace ends in it or the crate has a type of that name).</summary>
    public string EntryClassName { get; set; }

    /// <summary>The wrap report: what was wrapped, what was skipped and why.</summary>
    public string WrapReportPath { get; set; }

    /// <summary>Every file that was written.</summary>
    public IReadOnlyList<string> GeneratedFiles { get; set; } = Array.Empty<string>();
}
