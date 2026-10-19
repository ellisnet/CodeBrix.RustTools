using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using CodeBrix.WebGpu.Native;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu;

/// <summary>
/// Finds the CodeBrix.WebGpu.Native assets at run time without loading the engine: the
/// <c>include/</c> folder (headers and <c>codebrix_wgpu_procs.rs</c>, which NuGet never copies
/// into an application) and the per-runtime-identifier <c>codebrix_wgpu</c> libraries.
/// </summary>
/// <remarks>
/// The package version is the informational version of the referenced managed assembly
/// (<c>CodeBrix.WebGpu.Native.dll</c>, the package's own version; the "+commit" suffix is
/// dropped). Search order for <c>include/</c>: the explicit folder
/// (<see cref="CrateBuilderOptions.WebGpuNativePackageDirectory"/>), then
/// <c>&lt;root&gt;/codebrix.webgpu.native.mitlicenseforever/&lt;version&gt;/</c> under each NuGet
/// global packages root (<c>NUGET_PACKAGES</c>, <c>$HOME/.nuget/packages</c>,
/// <c>%USERPROFILE%\.nuget\packages</c>). Libraries: the folder that supplied
/// <c>include/</c>, the other package folders of the same version, then the application base
/// directory's <c>runtimes/</c> (what NuGet copies into a framework-dependent build).
/// </remarks>
internal sealed class NativePackageLocator
{
    /// <summary>The NuGet package id.</summary>
    public const string PackageId = "CodeBrix.WebGpu.Native.MitLicenseForever";

    /// <summary>The package's folder name in the NuGet global packages folder (the lower-case
    /// id).</summary>
    public const string PackageFolderName = "codebrix.webgpu.native.mitlicenseforever";

    /// <summary>The engine library's base name (<c>-l</c> / <c>#[link(name)]</c>).</summary>
    public const string LibraryBaseName = "codebrix_wgpu";

    /// <summary>The package's header folder.</summary>
    public const string IncludeFolderName = "include";

    /// <summary>The licence file in <c>include/</c> and beside every library.</summary>
    public const string LicenseFileName = "LICENSE-WgpuNative.txt";

    /// <summary>The Rust file of the C ABI in <c>include/</c>.</summary>
    public const string RustProcsFileName = "codebrix_wgpu_procs.rs";

    /// <summary>The environment variable that moves the NuGet global packages folder.</summary>
    public const string NuGetPackagesVariable = "NUGET_PACKAGES";

    /// <summary>The files an <c>include/</c> folder must hold to be used.</summary>
    public static IReadOnlyList<string> RequiredIncludeFiles { get; } = Array.AsReadOnly(new[]
    {
        "webgpu.h", "wgpu.h", "codebrix_wgpu.h", "codebrix_wgpu_procs.h", RustProcsFileName, LicenseFileName,
    });

    private readonly Func<string, string> _getEnvironmentVariable;
    private readonly string _userProfileDirectory;
    private readonly string _applicationBaseDirectory;

    /// <summary>A locator for this process: the referenced package version, the real
    /// environment, the user profile folder and <see cref="AppContext.BaseDirectory"/>.</summary>
    public NativePackageLocator()
        : this(ReferencedPackageVersion, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), AppContext.BaseDirectory)
    {
    }

    /// <summary>A locator with explicit inputs (tests).</summary>
    /// <param name="version">The package version to look for.</param>
    /// <param name="getEnvironmentVariable">Reads an environment variable; <c>null</c> = none
    /// set.</param>
    /// <param name="userProfileDirectory">The user profile folder; <c>null</c> = none.</param>
    /// <param name="applicationBaseDirectory">The application base folder whose
    /// <c>runtimes/</c> is the last place searched for libraries; <c>null</c> = none.</param>
    public NativePackageLocator(string version, Func<string, string> getEnvironmentVariable, string userProfileDirectory, string applicationBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("A package version is required.", nameof(version));
        }

        Version = version.Trim();
        _getEnvironmentVariable = getEnvironmentVariable ?? (_ => null);
        _userProfileDirectory = userProfileDirectory;
        _applicationBaseDirectory = applicationBaseDirectory;
    }

    /// <summary>The CodeBrix.WebGpu.Native version CrateBuilder references (the managed
    /// assembly's informational version without the "+commit" suffix).</summary>
    public static string ReferencedPackageVersion { get; } = VersionOf(typeof(CodeBrixWebGpuNative).Assembly);

    /// <summary>The package version this locator looks for.</summary>
    public string Version { get; }

    /// <summary>
    /// The engine library's file name on a target: <c>libcodebrix_wgpu.so</c> (Linux,
    /// Android), <c>libcodebrix_wgpu.dylib</c> (macOS), <c>codebrix_wgpu.dll</c> (Windows).
    /// </summary>
    /// <param name="target">The target.</param>
    public static string NativeFileName(CrateTarget target) => CrateTargets.NativeFileName(target, LibraryBaseName);

    /// <summary>The NuGet global packages roots, in search order (existing or not, no
    /// repeats).</summary>
    public IReadOnlyList<string> NuGetPackageRoots()
    {
        var roots = new List<string>();
        Add(roots, _getEnvironmentVariable(NuGetPackagesVariable));
        AddUnder(roots, _getEnvironmentVariable("HOME"));
        AddUnder(roots, _getEnvironmentVariable("USERPROFILE"));
        AddUnder(roots, _userProfileDirectory);
        return roots;
    }

    /// <summary>The package folders looked at for <c>include/</c>, in search order.</summary>
    /// <param name="explicitDirectory">The explicit package folder, or <c>null</c>.</param>
    public IReadOnlyList<string> PackageDirectoryCandidates(string explicitDirectory)
    {
        var candidates = new List<string>();
        Add(candidates, explicitDirectory);
        foreach (var root in NuGetPackageRoots())
        {
            Add(candidates, Path.Combine(root, PackageFolderName, Version.ToLowerInvariant()));
        }

        return candidates;
    }

    /// <summary>
    /// Finds the package: the first candidate folder whose <c>include/</c> holds every
    /// <see cref="RequiredIncludeFiles"/> entry. Nothing is loaded or copied.
    /// </summary>
    /// <param name="explicitDirectory">The explicit package folder
    /// (<see cref="CrateBuilderOptions.WebGpuNativePackageDirectory"/>), or <c>null</c>.</param>
    /// <exception cref="CrateBuilderException">No candidate has a complete <c>include/</c>
    /// (stage <see cref="CrateBuildStage.Building"/>); the message names every folder searched
    /// and how to fix it.</exception>
    public NativePackageLocation Locate(string explicitDirectory = null)
    {
        var candidates = PackageDirectoryCandidates(explicitDirectory);
        var found = candidates.FirstOrDefault(HasCompleteInclude);
        if (found == null)
        {
            throw new CrateBuilderException(
                "The CodeBrix.WebGpu.Native " + Version + " include/ folder (" + string.Join(", ", RequiredIncludeFiles)
                + ") was not found. Searched: " + (candidates.Count == 0 ? "(no folder: no explicit folder, no NUGET_PACKAGES, no home folder)" : string.Join("; ", candidates.Select(c => Path.Combine(c, IncludeFolderName))))
                + ". NuGet never copies include/ into an application, so the package must be in the NuGet global packages folder of THIS machine: restore any project that references "
                + PackageId + " " + Version + " (dotnet restore), or set CrateBuilderOptions.WebGpuNativePackageDirectory to a folder that holds the package's include/ and runtimes/.",
                CrateBuildStage.Building);
        }

        var version = NuspecVersion(found) ?? Version;
        var binaryRoots = new List<string> { Path.Combine(found, "runtimes") };
        if (string.Equals(version, Version, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var other in candidates.Where(c => !SamePath(c, found) && Directory.Exists(c)))
            {
                Add(binaryRoots, Path.Combine(other, "runtimes"));
            }

            if (!string.IsNullOrWhiteSpace(_applicationBaseDirectory))
            {
                Add(binaryRoots, Path.Combine(_applicationBaseDirectory, "runtimes"));
            }
        }

        return new NativePackageLocation(version, Version, found, candidates, binaryRoots);
    }

    private static bool HasCompleteInclude(string packageDirectory)
    {
        var include = Path.Combine(packageDirectory, IncludeFolderName);
        return Directory.Exists(include) && RequiredIncludeFiles.All(f => File.Exists(Path.Combine(include, f)));
    }

    private static string NuspecVersion(string packageDirectory)
    {
        try
        {
            var nuspec = Directory.GetFiles(packageDirectory, "*.nuspec").OrderBy(p => p, StringComparer.Ordinal).FirstOrDefault();
            if (nuspec == null)
            {
                return null;
            }

            var version = XDocument.Load(nuspec).Descendants().FirstOrDefault(e => e.Name.LocalName == "version")?.Value;
            return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Xml.XmlException)
        {
            return null;
        }
    }

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational.Substring(0, plus) : informational;
        }

        return assembly.GetName().Version?.ToString();
    }

    private static void AddUnder(List<string> list, string home)
    {
        if (!string.IsNullOrWhiteSpace(home))
        {
            Add(list, Path.Combine(home.Trim(), ".nuget", "packages"));
        }
    }

    private static void Add(List<string> list, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var full = Path.GetFullPath(path.Trim());
        if (!list.Any(p => SamePath(p, full)))
        {
            list.Add(full);
        }
    }

    private static bool SamePath(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(a),
            Path.TrimEndingDirectorySeparator(b),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
