using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu;

/// <summary>
/// The names and the Rust lines of the direct-link route (the CodeBrix.WebGpu.Native
/// AGENT-README's NATIVE MODULES recipe, route 1) as CrateBuilder stages it: the link folder
/// inside the staged workspace, the two environment variables, the rustflag, and the exact
/// lines a crate uses. A generated facade emits <see cref="ModuleLines"/> and
/// <see cref="BuildScriptLines"/>; a hand-written crate copies them.
/// </summary>
internal static class WebGpuLinkRecipe
{
    /// <summary>The link folder under the staged workspace root (inside the staged tree, so a
    /// Docker build's copy-in carries it).</summary>
    public const string LinkFolderName = ".codebrix-wgpu";

    /// <summary>The token a build driver replaces with the staged workspace root of its
    /// mode.</summary>
    public const string WorkspaceToken = "{workspace}";

    /// <summary>The variable naming the staged copy of the package's <c>include/</c>.</summary>
    public const string IncludeDirVariable = "CODEBRIX_WGPU_INCLUDE_DIR";

    /// <summary>The variable naming the folder that holds the target's engine library.</summary>
    public const string LibDirVariable = "CODEBRIX_WGPU_LIB_DIR";

    /// <summary>The cfg that enables the <c>direct</c> module (the extern block with the
    /// <c>#[link]</c> attribute) of <c>codebrix_wgpu_procs.rs</c>.</summary>
    public const string DirectCfg = "codebrix_wgpu_direct";

    /// <summary>The human-readable recipe file written into the link folder.</summary>
    public const string HowToLinkFileName = "HOW-TO-LINK.txt";

    /// <summary>The lowest Android API level the packaged Android engine supports.</summary>
    public const int AndroidApiFloor = 33;

    /// <summary>The Rust lines (src/lib.rs) that bring in the whole C ABI with no crates. The
    /// <c>#[path]</c> attribute takes only a literal, so the file is pulled in with
    /// <c>include!</c> from the variable.</summary>
    public static IReadOnlyList<string> ModuleLines { get; } = Array.AsReadOnly(new[]
    {
        "#[allow(non_camel_case_types, non_snake_case, non_upper_case_globals, dead_code)]",
        "mod wgpu_ffi {",
        "    include!(concat!(env!(\"" + IncludeDirVariable + "\"), \"/codebrix_wgpu_procs.rs\"));",
        "}",
    });

    /// <summary>The build script (build.rs) lines: enable the <c>direct</c> module, whose
    /// extern block carries <c>#[link(name = "codebrix_wgpu")]</c> (Windows:
    /// <c>kind = "raw-dylib"</c>), and add the library folder to the link search path.</summary>
    public static IReadOnlyList<string> BuildScriptLines { get; } = Array.AsReadOnly(new[]
    {
        "fn main() {",
        "    println!(\"cargo::rerun-if-env-changed=" + IncludeDirVariable + "\");",
        "    println!(\"cargo::rerun-if-env-changed=" + LibDirVariable + "\");",
        "    println!(\"cargo::rustc-check-cfg=cfg(" + DirectCfg + ")\");",
        "    println!(\"cargo::rustc-cfg=" + DirectCfg + "\");",
        "    if std::env::var_os(\"" + IncludeDirVariable + "\").is_none() {",
        "        panic!(\"" + IncludeDirVariable + " is not set: it must name the include/ folder of the CodeBrix.WebGpu.Native package\");",
        "    }",
        "    if let Some(dir) = std::env::var_os(\"" + LibDirVariable + "\") {",
        "        println!(\"cargo::rustc-link-search=native={}\", dir.to_string_lossy());",
        "    }",
        "}",
    });

    /// <summary>A call through the direct declarations.</summary>
    public static string ExampleCallLine { get; } = "let api = unsafe { wgpu_ffi::direct::wgpuCodebrixGetApiVersion() };";

    /// <summary>The value of <see cref="IncludeDirVariable"/>.</summary>
    public static string IncludeDirValue => WorkspaceToken + "/" + LinkFolderName + "/include";

    /// <summary>The value of <see cref="LibDirVariable"/> for a target.</summary>
    /// <param name="target">The target.</param>
    public static string LibDirValue(CrateTarget target) => WorkspaceToken + "/" + LinkFolderName + "/" + CrateTargets.Rid(target);

    /// <summary>The rustflag that puts the target's library folder on the linker search path
    /// (Linux, Android, macOS; Windows links through raw-dylib and needs none).</summary>
    /// <param name="target">The target.</param>
    public static string LinkSearchRustFlag(CrateTarget target) => "-L native=" + LibDirValue(target);

    /// <summary>The text of <see cref="HowToLinkFileName"/>.</summary>
    /// <param name="version">The CodeBrix.WebGpu.Native version staged.</param>
    public static string HowToLinkText(string version)
    {
        var text = new StringBuilder();
        void Line(string value = "") => text.Append(value).Append('\n');

        Line("HOW TO LINK A RUST CRATE TO THE CODEBRIX WEBGPU ENGINE (codebrix_wgpu)");
        Line("======================================================================");
        Line("Staged by CodeBrix.RustTools.CrateBuilder from CodeBrix.WebGpu.Native " + version + ".");
        Line("The recipe is route 1 (direct dynamic linking) of NATIVE MODULES in that package's");
        Line("AGENT-README.txt.");
        Line();
        Line("THIS FOLDER (" + LinkFolderName + "/ under the staged workspace root)");
        Line("  include/          the package's include/ files: webgpu.h, wgpu.h, codebrix_wgpu.h,");
        Line("                    codebrix_wgpu_procs.h, codebrix_wgpu_procs.rs, LICENSE-WgpuNative.txt");
        Line("  <rid>/            the codebrix_wgpu library of each staged target (link input only:");
        Line("                    the module's package must NOT ship it - it depends on");
        Line("                    CodeBrix.WebGpu.Native.MitLicenseForever instead)");
        Line();
        Line("ENVIRONMENT CrateBuilder sets for cargo");
        Line("  " + IncludeDirVariable + " = <workspace>/" + LinkFolderName + "/include");
        Line("  " + LibDirVariable + "     = <workspace>/" + LinkFolderName + "/<rid>");
        Line("  RUSTFLAGS += -L native=<workspace>/" + LinkFolderName + "/<rid>   (not for Windows targets)");
        Line();
        Line("src/lib.rs - the whole C ABI with no crates (#[path] accepts only a literal, so the file");
        Line("is pulled in with include! from the variable):");
        foreach (var line in ModuleLines)
        {
            Line("    " + line);
        }

        Line();
        Line("build.rs - enable the `direct` module (its extern block carries");
        Line("#[link(name = \"codebrix_wgpu\")], on Windows kind = \"raw-dylib\", so no import library");
        Line("is needed) and add the library folder to the link search path:");
        foreach (var line in BuildScriptLines)
        {
            Line("    " + line);
        }

        Line();
        Line("Calling the engine:");
        Line("    " + ExampleCallLine);
        Line("Compare it with wgpu_ffi::WGPU_CODEBRIX_API_VERSION and refuse a mismatch.");
        Line();
        Line("RULES");
        Line("  * Never bundle or statically link wgpu (the wgpu / wgpu-core crates, another");
        Line("    wgpu-native build) or a second codebrix_wgpu: the process loads codebrix_wgpu once.");
        Line("  * .NET loads the engine FIRST: the wrapper's static constructor calls");
        Line("    CodeBrixWebGpuNative.EnsureLoaded() before the module's library is loaded; the");
        Line("    loader then reuses that copy (Linux/Android: DT_NEEDED libcodebrix_wgpu.so;");
        Line("    macOS: @rpath/libcodebrix_wgpu.dylib; Windows: codebrix_wgpu.dll by base name).");
        Line("  * Android: the packaged engine needs Android 13 (API " + AndroidApiFloor.ToString(System.Globalization.CultureInfo.InvariantCulture) + ") or later; link the module at");
        Line("    API level " + AndroidApiFloor.ToString(System.Globalization.CultureInfo.InvariantCulture) + " or higher.");
        Line("  * Build against the headers of the SAME package version the wrapper depends on.");
        return text.ToString();
    }
}
