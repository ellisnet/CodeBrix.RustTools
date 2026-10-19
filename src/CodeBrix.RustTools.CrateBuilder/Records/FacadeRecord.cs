using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The C interface of the built library in a <see cref="BuildRecord"/>: the generated facade
/// crate's exports, or - for a crate that is already a cdylib with C exports - the crate's own
/// exports that the wrapper declares.
/// </summary>
public sealed class FacadeRecord
{
    /// <summary>Whether CrateBuilder generated a facade crate (<c>false</c> when the crate was
    /// built as it is, because it already is a cdylib with C exports).</summary>
    public bool Generated { get; set; }

    /// <summary>The library name of the built file (<c>lib&lt;LibName&gt;.so</c>,
    /// <c>lib&lt;LibName&gt;.dylib</c>, <c>&lt;LibName&gt;.dll</c>); also the library name the
    /// wrapper imports from.</summary>
    public string LibName { get; set; }

    /// <summary>The facade crate's package name; <c>null</c> when no facade was
    /// generated.</summary>
    public string PackageName { get; set; }

    /// <summary>How many C functions the library exports (the length of
    /// <see cref="Exports"/>).</summary>
    public int ExportCount { get; set; }

    /// <summary>Why the facade stages ran, for example "a wrapper was requested", "the crate is
    /// not a cdylib", "FacadeMode.Always" or - for <see cref="FacadeMode.Auto"/>'s fallback -
    /// that the cdylib built as it is exported nothing, so the same build generated a facade.
    /// <c>null</c> in records written before the reason was recorded.</summary>
    public string Reason { get; set; }

    /// <summary>The exported C function names, in the facade's order.</summary>
    public IList<string> Exports { get; set; } = new List<string>();
}
