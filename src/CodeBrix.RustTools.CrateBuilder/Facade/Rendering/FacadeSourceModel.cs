using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// The render model the facade templates walk (built from a mapping plan by
/// <see cref="FacadeSourceBuilder"/>). Every list is in its final order; the templates never
/// sort. Templates see the members in snake_case.
/// </summary>
internal sealed class FacadeSourceModel
{
    /// <summary>The upstream package name.</summary>
    public string PackageName { get; set; }

    /// <summary>The upstream (and facade) version.</summary>
    public string Version { get; set; }

    /// <summary>The facade package name.</summary>
    public string FacadeCrateName { get; set; }

    /// <summary>The library name (the built file's base name).</summary>
    public string LibName { get; set; }

    /// <summary>The symbol prefix (for example "datacrate_").</summary>
    public string Prefix { get; set; }

    /// <summary>The symbol prefix in upper case (C macro names).</summary>
    public string PrefixUpper { get; set; }

    /// <summary>The upstream dependency as a TOML inline table value.</summary>
    public string UpstreamDependency { get; set; }

    /// <summary>The upstream workspace's <c>[patch.*]</c> and <c>[replace]</c> tables, re-based
    /// on the facade folder (TOML text; empty when there are none).</summary>
    public string WorkspacePatches { get; set; } = string.Empty;

    /// <summary>The <c>[profile.*]</c> tables of the facade (TOML text): the upstream workspace's
    /// profiles with <c>panic = "unwind"</c> in dev and release; see
    /// <see cref="FacadeRenderer.WorkspaceProfiles"/>.</summary>
    public string WorkspaceProfiles { get; set; } = FacadeRenderer.WorkspaceProfiles(null);

    /// <summary>Whether the facade links the CodeBrix WebGPU engine (build.rs + module lines).</summary>
    public bool LinkToWebGpu { get; set; }

    /// <summary>Whether an export takes a list of strings (the header then declares the
    /// <c>&lt;prefix&gt;str_ref</c> struct).</summary>
    public bool UsesStrRef { get; set; }

    /// <summary>The WebGPU module lines (lib.rs).</summary>
    public IList<string> WebGpuModuleLines { get; set; } = new List<string>();

    /// <summary>The WebGPU build-script lines (build.rs).</summary>
    public IList<string> BuildScriptLines { get; set; } = new List<string>();

    /// <summary>Module names of the consumer's additional facade files.</summary>
    public IList<string> ExtraModules { get; set; } = new List<string>();

    /// <summary>Fixed-array wrapper structs.</summary>
    public IList<ArrayView> Arrays { get; set; } = new List<ArrayView>();

    /// <summary>C-like enums.</summary>
    public IList<EnumView> Enums { get; set; } = new List<EnumView>();

    /// <summary>Mirror structs (dependency order).</summary>
    public IList<MirrorView> Mirrors { get; set; } = new List<MirrorView>();

    /// <summary>Opaque handle types.</summary>
    public IList<HandleView> Handles { get; set; } = new List<HandleView>();

    /// <summary>Exported functions in plan order.</summary>
    public IList<FunctionView> Functions { get; set; } = new List<FunctionView>();

    /// <summary>Override sources pasted into <c>mod overrides</c>.</summary>
    public IList<OverrideView> Overrides { get; set; } = new List<OverrideView>();

    /// <summary>Every exported symbol (for the read-me).</summary>
    public IList<string> Exports { get; set; } = new List<string>();

    /// <summary>The C header file name.</summary>
    public string HeaderFileName { get; set; }
}

/// <summary>A fixed-array wrapper struct (<c>#[repr(C)] struct X { values: [[T; N]; M] }</c>).</summary>
internal sealed class ArrayView
{
    /// <summary>The struct name (C and Rust).</summary>
    public string CName { get; set; }

    /// <summary>The Rust array type of the <c>values</c> field.</summary>
    public string RustType { get; set; }

    /// <summary>The C declaration of the <c>values</c> field.</summary>
    public string CDeclaration { get; set; }

    /// <summary>The planned size in bytes (0 = unknown; no layout assertion).</summary>
    public int ByteSize { get; set; }
}

/// <summary>A C-like enum.</summary>
internal sealed class EnumView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The facade enum name (= the C typedef name).</summary>
    public string CName { get; set; }

    /// <summary>The upstream enum path.</summary>
    public string UpstreamType { get; set; }

    /// <summary>The variants.</summary>
    public IList<EnumVariantView> Variants { get; set; } = new List<EnumVariantView>();
}

/// <summary>One enum variant.</summary>
internal sealed class EnumVariantView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The Rust variant name.</summary>
    public string Name { get; set; }

    /// <summary>The C constant name.</summary>
    public string CName { get; set; }

    /// <summary>The discriminant as text.</summary>
    public string Value { get; set; }
}

/// <summary>A repr(C) mirror struct.</summary>
internal sealed class MirrorView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The struct name (C and Rust).</summary>
    public string CName { get; set; }

    /// <summary>The upstream struct path.</summary>
    public string UpstreamType { get; set; }

    /// <summary>Whether the conversion into the upstream struct can fail (TryFrom).</summary>
    public bool Fallible { get; set; }

    /// <summary>The fields.</summary>
    public IList<MirrorFieldView> Fields { get; set; } = new List<MirrorFieldView>();

    /// <summary>The body lines of <c>From&lt;upstream&gt; for mirror</c> (field initialisers).</summary>
    public IList<string> FromUpstreamLines { get; set; } = new List<string>();

    /// <summary>The upstream constructor expression lines (struct literal or tuple call).</summary>
    public IList<string> ToUpstreamLines { get; set; } = new List<string>();

    /// <summary>Compile-time layout assertions (size, alignment, offsets).</summary>
    public IList<string> LayoutAssertions { get; set; } = new List<string>();
}

/// <summary>One mirror field.</summary>
internal sealed class MirrorFieldView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The Rust field name.</summary>
    public string Name { get; set; }

    /// <summary>The Rust FFI field type.</summary>
    public string RustType { get; set; }

    /// <summary>The C field declaration ("double x").</summary>
    public string CDeclaration { get; set; }
}

/// <summary>An opaque handle type.</summary>
internal sealed class HandleView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The handle type name (C and Rust).</summary>
    public string CName { get; set; }

    /// <summary>The upstream type path.</summary>
    public string UpstreamType { get; set; }
}

/// <summary>One exported function.</summary>
internal sealed class FunctionView
{
    /// <summary>Rust doc lines.</summary>
    public IList<string> DocLines { get; set; } = new List<string>();

    /// <summary>C comment lines.</summary>
    public IList<string> CDocLines { get; set; } = new List<string>();

    /// <summary>The exported symbol.</summary>
    public string CName { get; set; }

    /// <summary>The Rust parameter list ("a: f64, out: *mut f64").</summary>
    public string RustParameters { get; set; }

    /// <summary>The Rust return clause (" -&gt; i32" or empty).</summary>
    public string RustReturn { get; set; }

    /// <summary>The body lines (without the function's own indentation).</summary>
    public IList<string> BodyLines { get; set; } = new List<string>();

    /// <summary>The C prototype ("int32_t f(double a, double* out);").</summary>
    public string CPrototype { get; set; }
}

/// <summary>One override from the WrapPlan.</summary>
internal sealed class OverrideView
{
    /// <summary>The Rust path the override replaces.</summary>
    public string RustPath { get; set; }

    /// <summary>The override source lines.</summary>
    public IList<string> Lines { get; set; } = new List<string>();
}
