using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The ready-to-fill stubs in <see cref="Model.SkippedItem.OverrideHint"/>: Rust
/// <c>extern "C"</c> skeletons for a FacadeOverride's RustSource (C# declarations in
/// pass-through mode). Lines end with <c>\n</c>.
/// </summary>
internal static class OverrideHints
{
    /// <summary>A stub for a function or method.</summary>
    /// <param name="cName">The C name the stub suggests.</param>
    /// <param name="rustPath">The upstream path (<c>Names::first</c>).</param>
    /// <param name="signature">The upstream signature, verbatim.</param>
    public static string ForFunction(string cName, string rustPath, string signature)
    {
        var sb = new StringBuilder();
        sb.Append("// FacadeOverride for ").Append(rustPath).Append('\n');
        if (!string.IsNullOrEmpty(signature))
        {
            sb.Append("// Upstream: ").Append(signature).Append('\n');
        }

        sb.Append("#[unsafe(no_mangle)]\n");
        sb.Append("pub unsafe extern \"C\" fn ").Append(cName).Append("(/* C parameters: numbers, pointer + length pairs, handles, out-pointers */) -> i32 {\n");
        sb.Append("    // Call ").Append(MappingPlanner.UpstreamCrateAlias).Append("::").Append(rustPath).Append("(...) here and write any result through an out-pointer.\n");
        sb.Append("    // Return 0 on success or a negative status on failure.\n");
        sb.Append("    0\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>A stub for a type (struct, enum, union).</summary>
    public static string ForType(string cName, string rustPath, string kind)
    {
        var sb = new StringBuilder();
        sb.Append("// FacadeOverride for ").Append(rustPath).Append(" (").Append(kind).Append(")\n");
        sb.Append("// Write extern \"C\" functions that create, read or change the parts of it you need, for example:\n");
        sb.Append("#[unsafe(no_mangle)]\n");
        sb.Append("pub unsafe extern \"C\" fn ").Append(cName).Append("_new(/* C parameters */) -> *mut core::ffi::c_void {\n");
        sb.Append("    // let value: ").Append(MappingPlanner.UpstreamCrateAlias).Append("::").Append(rustPath).Append(" = ...;\n");
        sb.Append("    // Box::into_raw(Box::new(value)) as *mut core::ffi::c_void\n");
        sb.Append("    core::ptr::null_mut()\n");
        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>A stub for a constant: a function returning it.</summary>
    /// <param name="cName">The C name the stub suggests.</param>
    /// <param name="rustPath">The constant's path.</param>
    /// <param name="cReturnType">The Rust return type to declare when it is a plain number
    /// type, else <c>null</c>.</param>
    public static string ForConstant(string cName, string rustPath, string cReturnType)
    {
        var sb = new StringBuilder();
        sb.Append("// FacadeOverride for ").Append(rustPath).Append(" (constant)\n");
        sb.Append("#[unsafe(no_mangle)]\n");
        if (cReturnType != null)
        {
            sb.Append("pub extern \"C\" fn ").Append(cName).Append("() -> ").Append(cReturnType).Append(" {\n");
            sb.Append("    ").Append(MappingPlanner.UpstreamCrateAlias).Append("::").Append(rustPath).Append('\n');
        }
        else
        {
            sb.Append("pub unsafe extern \"C\" fn ").Append(cName).Append("(/* out-pointer for the value */) -> i32 {\n");
            sb.Append("    // Write ").Append(MappingPlanner.UpstreamCrateAlias).Append("::").Append(rustPath).Append(" (or the parts of it you need) through the out-pointer.\n");
            sb.Append("    0\n");
        }

        sb.Append("}\n");
        return sb.ToString();
    }

    /// <summary>A C# declaration stub for a pass-through export (a FacadeOverride's
    /// CSharpSource).</summary>
    public static string ForPassThrough(string libName, string exportName, string signature)
    {
        var sb = new StringBuilder();
        sb.Append("// FacadeOverride (CSharpSource) for the C export ").Append(exportName).Append('\n');
        if (!string.IsNullOrEmpty(signature))
        {
            sb.Append("// Upstream: ").Append(signature).Append('\n');
        }

        sb.Append("[LibraryImport(\"").Append(libName).Append("\", EntryPoint = \"").Append(exportName).Append("\")]\n");
        sb.Append("internal static partial int ").Append(PlannerNames.Pascal(exportName)).Append("(/* the C parameters as blittable C# types */);\n");
        return sb.ToString();
    }
}
