using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// The C-ABI mapping plan of one crate, written by <see cref="MappingPlanner"/>: which types
/// become opaque handles, by-value mirrors or enums, which C functions the facade exports and
/// how each one calls the upstream crate, and what was not wrapped and why. Plain data: the
/// facade renderer and the wrapper writer walk it as it is (every list is already in its final,
/// deterministic order).
/// </summary>
internal sealed class MappingPlan
{
    /// <summary>The upstream package name as written in its Cargo.toml (<c>my-crate</c>).</summary>
    public string CrateName { get; set; }

    /// <summary>The upstream cargo library name, <c>_</c> form (<c>my_crate</c>); also the base
    /// of the facade's native file name.</summary>
    public string LibName { get; set; }

    /// <summary>The generated facade package: <c>&lt;LibName&gt;_facade</c>.</summary>
    public string FacadeCrateName { get; set; }

    /// <summary>The prefix of every generated C symbol: <c>&lt;LibName&gt;_</c>.</summary>
    public string SymbolPrefix { get; set; }

    /// <summary>The wrapper's root namespace.</summary>
    public string ManagedNamespace { get; set; }

    /// <summary>The wrapper's assembly name.</summary>
    public string ManagedAssemblyName { get; set; }

    /// <summary>The upstream crate is already a cdylib with C exports (pass-through mode): its own
    /// <c>extern "C"</c> functions are the exports, no facade crate is generated and there are no
    /// handles or mirrors.</summary>
    public bool UpstreamIsCdylib { get; set; }

    /// <summary>The build links the CodeBrix WebGPU engine.</summary>
    public bool LinkToCodeBrixWebGpu { get; set; }

    /// <summary>Opaque handle types, ordinal by <see cref="HandleType.RustPath"/>.</summary>
    public IList<HandleType> Handles { get; set; } = new List<HandleType>();

    /// <summary>By-value mirror structs in dependency order (a mirror after every mirror it
    /// contains), ties ordinal by <see cref="MirrorStruct.RustPath"/>.</summary>
    public IList<MirrorStruct> Mirrors { get; set; } = new List<MirrorStruct>();

    /// <summary>C-like enums, ordinal by <see cref="EnumMapping.RustPath"/>.</summary>
    public IList<EnumMapping> Enums { get; set; } = new List<EnumMapping>();

    /// <summary>Every C export of the facade (constructors, methods, destroy and release functions
    /// included), ordinal by <see cref="ExportedFunction.CName"/>. The last-error accessor is not
    /// listed: it belongs to the facade's support code.</summary>
    public IList<ExportedFunction> Functions { get; set; } = new List<ExportedFunction>();

    /// <summary>What was not wrapped, ordinal by <see cref="SkippedItem.RustPath"/>.</summary>
    public IList<SkippedItem> Skipped { get; set; } = new List<SkippedItem>();

    /// <summary>Public constants with a plain literal value, declared as C# constants by the
    /// wrapper.</summary>
    public IList<ConstantMapping> Constants { get; set; } = new List<ConstantMapping>();

    /// <summary>Things the consumer should know, ordinal and distinct.</summary>
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>Units, ranges, conventions and thread rules lifted from the crate's own doc
    /// comments.</summary>
    public IList<DocNote> DocNotes { get; set; } = new List<DocNote>();

    /// <summary>Rust files of the consumer's own to copy into the facade crate (from the
    /// WrapPlan, in its order).</summary>
    public IList<string> AdditionalFacadeFiles { get; set; } = new List<string>();

    /// <summary>Hand-written exports from the WrapPlan, ordinal by RustPath.</summary>
    public IList<FacadeOverrideEntry> Overrides { get; set; } = new List<FacadeOverrideEntry>();

    /// <summary>ADDED (WP6): the WrapPlan's free-text notes, copied into the wrap report;
    /// <c>null</c> when there are none.</summary>
    public string WrapPlanNotes { get; set; }
}
