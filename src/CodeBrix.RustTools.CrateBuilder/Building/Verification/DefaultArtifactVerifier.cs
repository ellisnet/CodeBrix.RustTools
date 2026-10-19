using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// The verifier every build runs unless told otherwise: the file exists, it is the target's
/// format and architecture, and - when the crate is a cdylib (or a generated facade) - it
/// exports at least one symbol. Its notes name the file's own library name, the libraries it
/// loads (NEEDED / imports / LC_LOAD_DYLIB), the export count and, for ELF, the highest glibc
/// symbol version it needs, so later verifiers and people can inspect them.
/// </summary>
internal sealed class DefaultArtifactVerifier : IArtifactVerifier
{
    /// <summary>The words of the note (and the failure message) for a library that exports
    /// nothing; the pipeline's <see cref="FacadeMode.Auto"/> fallback looks for them.</summary>
    public const string NoExportsNote = "exports no symbols";

    /// <summary>The words of the note that names the Android API level an Android library was
    /// linked for (its <c>.note.android.ident</c>).</summary>
    public const string AndroidApiLevelNote = "linked for Android API level";

    /// <summary>How many export names the notes list before "...".</summary>
    public const int ListedExportCount = 10;

    public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
    {
        var notes = new List<string>();
        var name = Path.GetFileName(artifactPath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
        {
            ok = false;
            notes.Add($"{name}: the file does not exist.");
            return notes;
        }

        NativeArtifactInfo info;
        try
        {
            info = NativeArtifactReader.Read(artifactPath);
        }
        catch (FormatException ex)
        {
            ok = false;
            notes.Add($"{name}: not a readable native file: {ex.Message}");
            return notes;
        }

        ok = true;
        notes.Add($"{name}: {info}.");
        var expectedFormat = NativeArtifactReader.ExpectedFormat(target);
        var expectedArchitecture = NativeArtifactReader.ExpectedArchitecture(target);
        if (info.Format != expectedFormat)
        {
            ok = false;
            notes.Add($"{name}: is {info.Format}, but {CrateTargets.Rid(target)} needs {expectedFormat}.");
        }

        if (!string.Equals(info.Architecture, expectedArchitecture, StringComparison.Ordinal))
        {
            ok = false;
            notes.Add($"{name}: is built for {info.Architecture}, but {CrateTargets.Rid(target)} needs {expectedArchitecture}.");
        }

        notes.Add($"{name}: needs [{string.Join(", ", info.NeededLibraries)}].");
        var shown = info.ExportedSymbols.Take(ListedExportCount).ToList();
        notes.Add($"{name}: exports {info.ExportedSymbols.Count} symbol(s)" + (shown.Count == 0 ? "." : ": " + string.Join(", ", shown) + (info.ExportedSymbols.Count > shown.Count ? ", ..." : string.Empty) + "."));
        var glibc = HighestGlibcVersion(info);
        if (glibc != null)
        {
            notes.Add($"{name}: highest glibc symbol version needed: {glibc}.");
        }

        if (info.AndroidApiLevel.HasValue)
        {
            notes.Add($"{name}: {AndroidApiLevelNote} {info.AndroidApiLevel.Value} (it loads on that Android version and later).");
        }

        if (RequiresExports(workspace) && info.ExportedSymbols.Count == 0)
        {
            ok = false;
            notes.Add($"{name}: {NoExportsNote}, so .NET could not call into it (a cdylib needs #[unsafe(no_mangle)] pub extern \"C\" functions).");
        }

        notes.AddRange(info.Notes.Select(n => name + ": " + n));
        return notes;
    }

    /// <summary>Whether the built crate must export symbols: it is a generated facade, it
    /// declares a cdylib, or its crate types are unknown.</summary>
    public static bool RequiresExports(StagedWorkspace workspace)
    {
        if (workspace == null || workspace.IsFacade)
        {
            return true;
        }

        var types = workspace.Identity?.CrateTypes;
        return types == null || types.Count == 0 || types.Contains("cdylib", StringComparer.Ordinal);
    }

    /// <summary>The highest <c>GLIBC_x.y[.z]</c> version an ELF file needs, or <c>null</c>
    /// when it needs none.</summary>
    public static string HighestGlibcVersion(NativeArtifactInfo info)
    {
        Version best = null;
        string bestText = null;
        foreach (var required in info?.RequiredSymbolVersions ?? Array.Empty<string>())
        {
            if (required.StartsWith("GLIBC_", StringComparison.Ordinal) && Version.TryParse(required.Substring(6), out var version))
            {
                if (best == null || version > best)
                {
                    best = version;
                    bestText = required.Substring(6);
                }
            }
        }

        return bestText;
    }
}
