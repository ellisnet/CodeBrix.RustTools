using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu;

/// <summary>
/// The build contribution of <see cref="CrateBuildRequest.LinkToCodeBrixWebGpu"/> (plan D3):
/// stages the CodeBrix.WebGpu.Native <c>include/</c> files and the target's
/// <c>codebrix_wgpu</c> library into <c>&lt;staged root&gt;/.codebrix-wgpu/</c>, writes
/// <c>HOW-TO-LINK.txt</c> there, and returns the environment variables and the rustflag that
/// let a crate following the recipe (<see cref="WebGpuLinkRecipe"/>) link to the engine.
/// </summary>
/// <remarks>
/// Every value uses the <c>{workspace}</c> token, which the build driver replaces with the
/// staged root of its mode (the host path, or the path inside the container). The files are
/// staged INSIDE the staged tree so a Docker build's copy-in carries them. Re-running is
/// idempotent: a file whose bytes are already in place is not rewritten (so cargo sees no
/// changed input).
/// </remarks>
internal sealed class WebGpuLinkStager : IBuildContribution
{
    private readonly NativePackageLocator _locator;
    private readonly object _gate = new object();
    private NativePackageLocation _location;
    private string _locatedFor;

    /// <summary>A stager that finds the package with a default <see cref="NativePackageLocator"/>.</summary>
    public WebGpuLinkStager()
        : this(new NativePackageLocator())
    {
    }

    /// <summary>A stager with its own locator (tests).</summary>
    /// <param name="locator">Finds the package.</param>
    public WebGpuLinkStager(NativePackageLocator locator)
    {
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
    }

    /// <summary>The link folder of a staged workspace root.</summary>
    /// <param name="workspaceDirectory">The staged root.</param>
    public static string LinkDirectory(string workspaceDirectory) => Path.Combine(workspaceDirectory, WebGpuLinkRecipe.LinkFolderName);

    /// <summary>
    /// Stages the files for one target and returns the contribution.
    /// </summary>
    /// <param name="workspace">The staged workspace (its <see cref="StagedWorkspace.Directory"/>
    /// receives <c>.codebrix-wgpu/</c>).</param>
    /// <param name="target">The target being built.</param>
    /// <param name="mode">The build mode (the values are the same in both; the driver
    /// resolves the token).</param>
    /// <param name="context">The run's context: its options name an explicit package folder;
    /// it receives the linked version and any warning. May be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels before any file is copied.</param>
    /// <exception cref="CrateBuilderException">The package's <c>include/</c> or the target's
    /// library was not found (stage <see cref="CrateBuildStage.Building"/>).</exception>
    public Task<BuildContribution> PrepareAsync(StagedWorkspace workspace, CrateTarget target, BuildMode mode, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        if (workspace == null || string.IsNullOrWhiteSpace(workspace.Directory))
        {
            throw new ArgumentException("A staged workspace with a root folder is required.", nameof(workspace));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var location = Locate(context?.Options?.WebGpuNativePackageDirectory);
        var libraryPath = location.NativeLibraryPath(target);
        var rid = CrateTargets.Rid(target);

        var linkDirectory = LinkDirectory(workspace.Directory);
        var includeDestination = Path.Combine(linkDirectory, NativePackageLocator.IncludeFolderName);
        var libraryDestination = Path.Combine(linkDirectory, rid);
        Directory.CreateDirectory(includeDestination);
        Directory.CreateDirectory(libraryDestination);

        foreach (var file in Directory.GetFiles(location.IncludeDirectory).OrderBy(f => f, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            CopyIfDifferent(file, Path.Combine(includeDestination, Path.GetFileName(file)));
        }

        var libraryFileName = NativePackageLocator.NativeFileName(target);
        CopyIfDifferent(libraryPath, Path.Combine(libraryDestination, libraryFileName));
        var licenseBesideLibrary = Path.Combine(Path.GetDirectoryName(libraryPath), NativePackageLocator.LicenseFileName);
        CopyIfDifferent(File.Exists(licenseBesideLibrary) ? licenseBesideLibrary : location.LicenseFilePath, Path.Combine(libraryDestination, NativePackageLocator.LicenseFileName));
        WriteIfDifferent(Path.Combine(linkDirectory, WebGpuLinkRecipe.HowToLinkFileName), WebGpuLinkRecipe.HowToLinkText(location.Version));

        var contribution = new BuildContribution();
        contribution.Environment[WebGpuLinkRecipe.IncludeDirVariable] = WebGpuLinkRecipe.IncludeDirValue;
        contribution.Environment[WebGpuLinkRecipe.LibDirVariable] = WebGpuLinkRecipe.LibDirValue(target);
        if (!CrateTargets.IsWindows(target))
        {
            contribution.RustFlags.Add(WebGpuLinkRecipe.LinkSearchRustFlag(target));
        }

        contribution.Notes.Add("CodeBrix.WebGpu.Native " + location.Version + ": staged " + WebGpuLinkRecipe.LinkFolderName + "/" + rid + "/" + libraryFileName + " (from " + libraryPath + ") and " + WebGpuLinkRecipe.LinkFolderName + "/include/ (from " + location.IncludeDirectory + ").");
        if (CrateTargets.IsWindows(target))
        {
            contribution.Notes.Add("Windows: the crate links codebrix_wgpu.dll through #[link(kind = \"raw-dylib\")]; no import library and no -L flag are needed.");
        }

        if (CrateTargets.IsAndroid(target))
        {
            contribution.Notes.Add("Android: the packaged engine needs Android 13 (API " + WebGpuLinkRecipe.AndroidApiFloor.ToString(CultureInfo.InvariantCulture) + ") or later; link the module at API level " + WebGpuLinkRecipe.AndroidApiFloor.ToString(CultureInfo.InvariantCulture) + " or higher.");
        }

        if (!location.IsReferencedVersion)
        {
            var warning = "The WebGPU link uses CodeBrix.WebGpu.Native " + location.Version + " from " + location.PackageDirectory + ", but CrateBuilder references " + location.ReferencedVersion + "; a wrapper must depend on the version it was linked against.";
            contribution.Notes.Add(warning);
            if (context != null && !context.Warnings.Contains(warning))
            {
                context.Warnings.Add(warning);
            }
        }

        if (context != null)
        {
            context.WebGpuNativeVersion = location.Version;
            context.Report(CrateBuildStage.Building, "Staged the CodeBrix WebGPU link files (" + rid + ")", target);
        }

        return Task.FromResult(contribution);
    }

    private NativePackageLocation Locate(string explicitDirectory)
    {
        lock (_gate)
        {
            var key = explicitDirectory ?? string.Empty;
            if (_location == null || !string.Equals(_locatedFor, key, StringComparison.Ordinal))
            {
                _location = _locator.Locate(explicitDirectory);
                _locatedFor = key;
            }

            return _location;
        }
    }

    private static void CopyIfDifferent(string source, string destination)
    {
        if (File.Exists(destination) && SameBytes(source, destination))
        {
            return;
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static void WriteIfDifferent(string path, string text)
    {
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        File.WriteAllBytes(path, bytes);
    }

    private static bool SameBytes(string a, string b)
    {
        var infoA = new FileInfo(a);
        var infoB = new FileInfo(b);
        if (infoA.Length != infoB.Length)
        {
            return false;
        }

        using var streamA = infoA.OpenRead();
        using var streamB = infoB.OpenRead();
        var bufferA = new byte[81920];
        var bufferB = new byte[81920];
        while (true)
        {
            var readA = streamA.ReadAtLeast(bufferA, bufferA.Length, throwOnEndOfStream: false);
            var readB = streamB.ReadAtLeast(bufferB, bufferB.Length, throwOnEndOfStream: false);
            if (readA != readB || !bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
            {
                return false;
            }

            if (readA == 0)
            {
                return true;
            }
        }
    }
}
