using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// The names a build needs about the crate it builds, read from the staged manifest: the
/// package name (cargo's <c>-p</c>) and the library name (the base of the native file
/// name).
/// </summary>
internal sealed class BuiltCrate
{
    /// <summary>The package name (<c>[package] name</c>).</summary>
    public string PackageName { get; private set; }

    /// <summary>The library target's name: <c>[lib] name</c>, else the package name with every
    /// '-' turned into '_'.</summary>
    public string LibraryName { get; private set; }

    /// <summary>The Cargo.toml that is built (absolute).</summary>
    public string ManifestPath { get; private set; }

    /// <summary>Reads the names from <see cref="StagedWorkspace.ManifestPath"/>.</summary>
    /// <exception cref="CrateBuilderException">The manifest is missing or has no
    /// package.</exception>
    public static BuiltCrate From(StagedWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(workspace.ManifestPath) || !File.Exists(workspace.ManifestPath))
        {
            throw new CrateBuilderException($"The staged manifest '{workspace.ManifestPath}' does not exist.", CrateBuildStage.Building);
        }

        var manifestPath = Path.GetFullPath(workspace.ManifestPath);
        CargoManifest manifest;
        try
        {
            manifest = CargoManifest.Load(manifestPath);
        }
        catch (RustToolsException ex)
        {
            throw new CrateBuilderException($"The staged manifest '{manifestPath}' cannot be read: {ex.Message}", CrateBuildStage.Building, ex);
        }

        var packageName = manifest.Package?.Name;
        if (string.IsNullOrWhiteSpace(packageName))
        {
            throw new CrateBuilderException($"The staged manifest '{manifestPath}' declares no [package]; a crate (not a virtual workspace) must be built.", CrateBuildStage.Building);
        }

        var libraryName = string.IsNullOrWhiteSpace(manifest.Lib?.Name) ? packageName.Replace('-', '_') : manifest.Lib.Name;
        return new BuiltCrate { PackageName = packageName, LibraryName = libraryName, ManifestPath = manifestPath };
    }
}
