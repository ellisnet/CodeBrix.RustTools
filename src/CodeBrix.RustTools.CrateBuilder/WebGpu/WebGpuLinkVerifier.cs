using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu;

/// <summary>
/// The post-build check of <see cref="CrateBuildRequest.LinkToCodeBrixWebGpu"/> (plan D3): the
/// artifact must link to the CodeBrix WebGPU engine BY NAME, so the loader reuses the copy
/// .NET loaded, and must bundle no engine of its own.
/// </summary>
/// <remarks>
/// Rules, per target:
/// <list type="number">
/// <item><description>Linux / Android: ELF, DT_NEEDED contains <c>libcodebrix_wgpu.so</c>;
/// macOS: Mach-O, an LC_LOAD_DYLIB is <c>@rpath/libcodebrix_wgpu.dylib</c>; Windows: PE,
/// the imports contain <c>codebrix_wgpu.dll</c> (any case). An artifact that references
/// no engine symbol at all (no undefined <c>wgpu*</c> symbol and no library with
/// <c>wgpu</c> in its name - the linker drops an unused library) passes with a
/// <see cref="VerificationWarningPrefix"/> note instead: the link was requested but is not
/// used.</description></item>
/// <item><description>No exported symbol starts with <c>wgpu</c> (this covers
/// <c>wgpuCodebrix*</c>): exporting the WebGPU API means the engine was bundled.</description></item>
/// <item><description>No other linked library has <c>wgpu</c> in its name (a differently
/// named engine, or the engine under an absolute path or another install
/// name).</description></item>
/// </list>
/// </remarks>
internal sealed class WebGpuLinkVerifier : Building.IArtifactVerifier
{
    /// <summary>The prefix of a note that is a warning, not a failure (the pipeline's Verifying
    /// stage copies such notes into the build's warnings).</summary>
    public const string VerificationWarningPrefix = "WARNING: ";

    /// <summary>How many offending export names a note lists.</summary>
    public const int ListedExportLimit = 5;

    /// <summary>The name the artifact must link to on a target: <c>libcodebrix_wgpu.so</c>,
    /// <c>@rpath/libcodebrix_wgpu.dylib</c> or <c>codebrix_wgpu.dll</c>.</summary>
    /// <param name="target">The target.</param>
    public static string ExpectedLinkName(CrateTarget target)
        => CrateTargets.IsMacOS(target)
            ? "@rpath/" + NativePackageLocator.NativeFileName(target)
            : NativePackageLocator.NativeFileName(target);

    /// <summary>
    /// Checks one artifact.
    /// </summary>
    /// <param name="artifactPath">The built library.</param>
    /// <param name="target">The target it was built for.</param>
    /// <param name="workspace">The staged workspace (not used by these rules).</param>
    /// <param name="ok">False when a rule failed; the notes say which and why.</param>
    /// <returns>The notes: what was found, then every failure.</returns>
    public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
    {
        var notes = new List<string>();
        var name = Path.GetFileName(artifactPath ?? string.Empty);
        LinkedBinaryInfo info;
        try
        {
            info = LinkedBinaryReader.Read(artifactPath, CrateTargets.IsMacOS(target) ? MachineOf(target) : null);
        }
        catch (Exception ex) when (ex is IOException || ex is FormatException || ex is UnauthorizedAccessException)
        {
            ok = false;
            notes.Add("WebGPU link: " + name + " could not be read: " + ex.Message);
            return notes;
        }

        ok = true;
        var expectedFormat = CrateTargets.IsWindows(target) ? LinkedBinaryFormat.Pe : CrateTargets.IsMacOS(target) ? LinkedBinaryFormat.MachO : LinkedBinaryFormat.Elf;
        notes.Add("WebGPU link: " + name + " (" + info.Format + ", " + info.Machine + ") links " + (info.LinkedLibraries.Count == 0 ? "nothing" : string.Join(", ", info.LinkedLibraries)) + ".");
        notes.AddRange(info.Warnings.Select(w => "WebGPU link: " + w));
        if (info.Format != expectedFormat)
        {
            ok = false;
            notes.Add("WebGPU link FAILED: " + name + " is " + info.Format + ", not the " + expectedFormat + " file a " + CrateTargets.Rid(target) + " library is.");
            return notes;
        }

        var expected = ExpectedLinkName(target);
        var comparison = CrateTargets.IsWindows(target) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var engineImports = info.ImportedSymbols.Where(s => s.StartsWith("wgpu", StringComparison.Ordinal)).ToList();
        var bundled = info.ExportedSymbols.Where(s => s.StartsWith("wgpu", StringComparison.Ordinal)).ToList();
        if (info.LinkedLibraries.Any(l => string.Equals(l, expected, comparison)))
        {
            notes.Add("WebGPU link: links " + expected + ", so the loader reuses the codebrix_wgpu .NET loaded first.");
        }
        else if (engineImports.Count == 0 && bundled.Count == 0 && !info.LinkedLibraries.Any(l => l.IndexOf("wgpu", StringComparison.OrdinalIgnoreCase) >= 0))
        {
            // The linker drops a library nothing refers to: a crate (or generated facade) that
            // calls no engine function has no reference to link. Nothing is bundled either (the
            // export rule below still applies), so the file is accepted with a warning.
            notes.Add(VerificationWarningPrefix + "WebGPU link: " + name + " was built with the CodeBrix WebGPU link, but the artifact does not reference the engine (it calls no codebrix_wgpu function, so the linker recorded no " + LinkKind(target) + " for " + expected + "); the link requested by LinkToCodeBrixWebGpu is not used.");
        }
        else
        {
            ok = false;
            notes.Add("WebGPU link FAILED: " + name + " does not link " + expected + " (" + LinkKind(target) + ")"
                + (engineImports.Count > 0 ? " although it uses engine functions (" + string.Join(", ", engineImports.Take(ListedExportLimit)) + (engineImports.Count > ListedExportLimit ? ", ..." : string.Empty) + ")" : string.Empty)
                + "; build it with the CodeBrix WebGPU link recipe (codebrix_wgpu_direct and the link search path).");
        }

        if (bundled.Count > 0)
        {
            ok = false;
            notes.Add("WebGPU link FAILED: " + name + " exports " + bundled.Count + " WebGPU symbol(s) (" + string.Join(", ", bundled.Take(ListedExportLimit)) + (bundled.Count > ListedExportLimit ? ", ..." : string.Empty)
                + "), so it bundles a WebGPU engine of its own; a module must never link wgpu statically or carry a second codebrix_wgpu.");
        }

        var others = info.LinkedLibraries
            .Where(l => l.IndexOf("wgpu", StringComparison.OrdinalIgnoreCase) >= 0 && !string.Equals(l, expected, comparison))
            .ToList();
        if (others.Count > 0)
        {
            ok = false;
            notes.Add("WebGPU link FAILED: " + name + " links " + string.Join(", ", others) + "; the only WebGPU library a module may link is " + expected + ".");
        }

        return notes;
    }

    private static string MachineOf(CrateTarget target) => target is CrateTarget.OsxArm64 ? "arm64" : "x86-64";

    private static string LinkKind(CrateTarget target)
        => CrateTargets.IsWindows(target) ? "PE import" : CrateTargets.IsMacOS(target) ? "LC_LOAD_DYLIB" : "ELF DT_NEEDED";
}
