using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu;

/// <summary>
/// Where the CodeBrix.WebGpu.Native assets a WebGPU link needs were found: the package
/// folder with <c>include/</c>, and per target the <c>codebrix_wgpu</c> library to link
/// against. Made by <see cref="NativePackageLocator.Locate"/>.
/// </summary>
internal sealed class NativePackageLocation
{
    private readonly IReadOnlyList<string> _binaryRoots;

    /// <summary>Creates a location.</summary>
    /// <param name="version">The version of the package folder that supplied <c>include/</c>.</param>
    /// <param name="referencedVersion">The CodeBrix.WebGpu.Native version CrateBuilder
    /// references.</param>
    /// <param name="packageDirectory">The package folder (holds <c>include/</c>).</param>
    /// <param name="searchedDirectories">Every package folder that was looked at, in
    /// order.</param>
    /// <param name="binaryRoots">The folders holding <c>&lt;rid&gt;/native/</c> sub-folders
    /// (a package's <c>runtimes/</c>, the application's <c>runtimes/</c>), in search
    /// order.</param>
    public NativePackageLocation(string version, string referencedVersion, string packageDirectory, IReadOnlyList<string> searchedDirectories, IReadOnlyList<string> binaryRoots)
    {
        Version = version;
        ReferencedVersion = referencedVersion;
        PackageDirectory = packageDirectory;
        IncludeDirectory = Path.Combine(packageDirectory, NativePackageLocator.IncludeFolderName);
        SearchedDirectories = searchedDirectories ?? Array.Empty<string>();
        _binaryRoots = binaryRoots ?? Array.Empty<string>();
    }

    /// <summary>The package version of <see cref="PackageDirectory"/> (from its .nuspec, else
    /// the referenced version).</summary>
    public string Version { get; }

    /// <summary>The CodeBrix.WebGpu.Native version CrateBuilder references (the one an
    /// application using a wrapper loads).</summary>
    public string ReferencedVersion { get; }

    /// <summary>Whether <see cref="Version"/> equals <see cref="ReferencedVersion"/>.</summary>
    public bool IsReferencedVersion => string.Equals(Version, ReferencedVersion, StringComparison.OrdinalIgnoreCase);

    /// <summary>The package folder that holds <see cref="IncludeDirectory"/>.</summary>
    public string PackageDirectory { get; }

    /// <summary>The package's <c>include/</c> folder (webgpu.h, wgpu.h, codebrix_wgpu.h,
    /// codebrix_wgpu_procs.h, codebrix_wgpu_procs.rs, LICENSE-WgpuNative.txt).</summary>
    public string IncludeDirectory { get; }

    /// <summary>The licence file of the engine's headers and libraries in
    /// <see cref="IncludeDirectory"/>.</summary>
    public string LicenseFilePath => Path.Combine(IncludeDirectory, NativePackageLocator.LicenseFileName);

    /// <summary>Every package folder that was looked at for <c>include/</c>, in order.</summary>
    public IReadOnlyList<string> SearchedDirectories { get; }

    /// <summary>The folders searched for <c>&lt;rid&gt;/native/&lt;file&gt;</c>, in order.</summary>
    public IReadOnlyList<string> BinaryRoots => _binaryRoots;

    /// <summary>The candidate paths of a target's library, in search order (existing or
    /// not).</summary>
    /// <param name="target">The target.</param>
    public IReadOnlyList<string> NativeLibraryCandidates(CrateTarget target)
        => _binaryRoots
            .Select(root => Path.Combine(root, CrateTargets.Rid(target), "native", NativePackageLocator.NativeFileName(target)))
            .ToList();

    /// <summary>Finds a target's <c>codebrix_wgpu</c> library.</summary>
    /// <param name="target">The target.</param>
    /// <param name="path">The first existing candidate, or <c>null</c>.</param>
    public bool TryGetNativeLibraryPath(CrateTarget target, out string path)
    {
        path = NativeLibraryCandidates(target).FirstOrDefault(File.Exists);
        return path != null;
    }

    /// <summary>
    /// The path of a target's <c>codebrix_wgpu</c> library: <c>libcodebrix_wgpu.so</c>
    /// (Linux, Android), <c>libcodebrix_wgpu.dylib</c> (macOS) or <c>codebrix_wgpu.dll</c>
    /// (Windows) under <c>runtimes/&lt;rid&gt;/native/</c>.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <exception cref="CrateBuilderException">No candidate exists (stage
    /// <see cref="CrateBuildStage.Building"/>); the message lists every path tried.</exception>
    public string NativeLibraryPath(CrateTarget target)
    {
        if (TryGetNativeLibraryPath(target, out var path))
        {
            return path;
        }

        throw new CrateBuilderException(
            $"The CodeBrix.WebGpu.Native library for {CrateTargets.Rid(target)} ({NativePackageLocator.NativeFileName(target)}) was not found. Tried: "
            + string.Join("; ", NativeLibraryCandidates(target))
            + ". Restore CodeBrix.WebGpu.Native.MitLicenseForever " + Version + " on this machine (the package holds the library of every runtime identifier).",
            CrateBuildStage.Building);
    }
}
