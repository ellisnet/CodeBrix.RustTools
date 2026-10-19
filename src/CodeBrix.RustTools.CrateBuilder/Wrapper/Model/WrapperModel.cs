using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// Everything the wrapper templates render: names, packaging, the native files per RID, and
/// the generated C# per type (as ready-indented lines). Property names reach the templates in
/// snake_case (the template engine's default member renamer).
/// </summary>
internal sealed class WrapperModel
{
    /// <summary>The wrapper's assembly and project name.</summary>
    public string AssemblyName { get; set; }

    /// <summary>The namespace of every generated type.</summary>
    public string RootNamespace { get; set; }

    /// <summary>The native library name used in <c>LibraryImport</c> (the cargo lib name).</summary>
    public string LibName { get; set; }

    /// <summary>The public static class holding the free functions.</summary>
    public string LibClassName { get; set; }

    /// <summary>Why <see cref="LibClassName"/> is not simply the Pascal-cased library name (it
    /// got the "Library" suffix), or <c>null</c> when it is.</summary>
    public string LibClassNote { get; set; }

    /// <summary>The first static method of <see cref="LibClassName"/> (for the documents' first
    /// example), or <c>null</c> when the class has none.</summary>
    public string FirstEntryMethodName { get; set; }

    /// <summary>The C# signature of <see cref="FirstEntryMethodName"/>, or <c>null</c>.</summary>
    public string FirstEntrySignature { get; set; }

    /// <summary>The internal static class holding the native declarations and the loader.</summary>
    public string NativeClassName { get; set; }

    /// <summary>The public exception type.</summary>
    public string ExceptionClassName { get; set; }

    /// <summary>The C symbol of the last-error accessor.</summary>
    public string LastErrorFunction { get; set; }

    /// <summary>The C symbol that releases an owned byte buffer.</summary>
    public string FreeBufferFunction { get; set; }

    /// <summary>The C symbol that releases an owned string buffer.</summary>
    public string FreeStringFunction { get; set; }

    /// <summary>The Rust package name.</summary>
    public string CrateName { get; set; }

    /// <summary>The Rust package version.</summary>
    public string CrateVersion { get; set; }

    /// <summary>The crate's licence expression from Cargo.toml, or <c>null</c>.</summary>
    public string CrateLicense { get; set; }

    /// <summary>Where the crate came from, one line (repository URL + commit, or "a local
    /// Cargo.toml").</summary>
    public string SourceDescription { get; set; }

    /// <summary>Pass-through mode: the crate already exports C functions; no handles,
    /// mirrors or status codes.</summary>
    public bool IsPassThrough { get; set; }

    /// <summary>Whether the objects' public fields have generated getters and setters
    /// (<see cref="WrapperOptions.GenerateFieldAccessors"/>).</summary>
    public bool HasFieldAccessors { get; set; } = true;

    /// <summary>Whether a member passes a list of strings (the native class then declares the
    /// StrRef struct).</summary>
    public bool UsesStrRef { get; set; }

    /// <summary>The crate links the CodeBrix WebGPU engine.</summary>
    public bool LinksWebGpu { get; set; }

    /// <summary>The CodeBrix.WebGpu.Native package version to reference.</summary>
    public string WebGpuPackageVersion { get; set; }

    /// <summary>Package metadata, or <c>null</c> for a plain class library.</summary>
    public PackagingModel Packaging { get; set; }

    /// <summary>Whether <see cref="Packaging"/> is set.</summary>
    public bool IsPackable { get; set; }

    /// <summary>Whether Tests/ holds a smoke-test file (excluded from compilation).</summary>
    public bool HasSmokeTests { get; set; }

    /// <summary>The native files included, per RID.</summary>
    public List<RuntimeModel> Runtimes { get; set; } = new List<RuntimeModel>();

    /// <summary>Requested targets without a native file.</summary>
    public List<MissingTargetModel> MissingTargets { get; set; } = new List<MissingTargetModel>();

    /// <summary>The RIDs of <see cref="Runtimes"/>, comma-separated.</summary>
    public string RuntimeIdentifiersText { get; set; }

    /// <summary>The C# literal list of the RIDs (for the loader's error message).</summary>
    public string RuntimeIdentifiersLiteral { get; set; }

    /// <summary>The native declarations (indented lines).</summary>
    public List<string> NativeDeclarationLines { get; set; } = new List<string>();

    /// <summary>The blittable mirror structs inside the native class (indented lines).</summary>
    public List<string> NativeStructLines { get; set; } = new List<string>();

    /// <summary>The C# switch arms of the loader's file-name table (indented lines).</summary>
    public List<string> FileNameArmLines { get; set; } = new List<string>();

    /// <summary>The free functions (indented lines).</summary>
    public List<string> FreeFunctionLines { get; set; } = new List<string>();

    /// <summary>The mirrors.</summary>
    public List<TypeFileModel> Mirrors { get; set; } = new List<TypeFileModel>();

    /// <summary>The inline arrays used by fixed-size array fields and parameters.</summary>
    public List<TypeFileModel> Arrays { get; set; } = new List<TypeFileModel>();

    /// <summary>The enums.</summary>
    public List<TypeFileModel> Enums { get; set; } = new List<TypeFileModel>();

    /// <summary>The handle classes.</summary>
    public List<TypeFileModel> Handles { get; set; } = new List<TypeFileModel>();

    /// <summary>Hand-written C# from the WrapPlan overrides.</summary>
    public List<OverrideModel> Overrides { get; set; } = new List<OverrideModel>();

    /// <summary>Every exported C symbol the wrapper declares.</summary>
    public List<string> Exports { get; set; } = new List<string>();

    /// <summary>C# expressions that create a handle object without arguments (smoke test).</summary>
    public List<string> ParameterlessConstructions { get; set; } = new List<string>();

    /// <summary>The generated API reference.</summary>
    public List<ApiSectionModel> ApiSections { get; set; } = new List<ApiSectionModel>();

    /// <summary>Items that were not wrapped.</summary>
    public List<SkippedModel> Skipped { get; set; } = new List<SkippedModel>();

    /// <summary>Sentences from the crate's documentation about units and conventions.</summary>
    public List<string> DocNotes { get; set; } = new List<string>();

    /// <summary>The planner's warnings.</summary>
    public List<string> Warnings { get; set; } = new List<string>();

    /// <summary>The licence files copied to licenses/.</summary>
    public List<string> LicenseFiles { get; set; } = new List<string>();

    /// <summary>The handle class names (for the disposal rule in the docs).</summary>
    public List<string> HandleNames { get; set; } = new List<string>();
}
