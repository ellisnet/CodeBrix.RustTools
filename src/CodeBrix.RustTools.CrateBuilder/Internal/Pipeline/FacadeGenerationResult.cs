using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// The output of <see cref="IFacadeGenerator"/>. Later work packages may add members.
/// </summary>
internal sealed class FacadeGenerationResult
{
    /// <summary>Whether a facade crate was written (false for a crate that is already a
    /// cdylib with C exports).</summary>
    public bool Generated { get; set; }

    /// <summary>The facade crate's folder, when <see cref="Generated"/>.</summary>
    public string FacadeDirectory { get; set; }

    /// <summary>The facade crate's Cargo.toml; <c>null</c> = <c>Cargo.toml</c> in
    /// <see cref="FacadeDirectory"/>.</summary>
    public string ManifestPath { get; set; }

    /// <summary>The facade crate's PACKAGE name (what <c>cargo build -p</c> takes), or
    /// <c>null</c> when no facade was written.</summary>
    public string CrateName { get; set; }

    /// <summary>The name of the library that will be built (<c>[lib] name</c>, the base of the
    /// native file name: <c>lib&lt;LibName&gt;.so</c>, <c>&lt;LibName&gt;.dll</c>) and the
    /// library name the wrapper imports from; <c>null</c> keeps the staged crate's.</summary>
    public string LibName { get; set; }

    /// <summary>The wrap report written so far, or <c>null</c>.</summary>
    public string WrapReportPath { get; set; }

    /// <summary>The C functions the built library will export.</summary>
    public IList<FacadeExport> ExportedFunctions { get; set; } = new List<FacadeExport>();

    /// <summary>The mapping plan the facade (or, for a crate that is already a cdylib, the
    /// pass-through declarations) was generated from; the wrapper writer renders the managed
    /// side from it. <c>null</c> when no plan was made.</summary>
    public MappingPlan Plan { get; set; }
}
