using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.CrateBuilder.Sources;

/// <summary>
/// What the build needs to know from a crate's Cargo.toml: the package name and version
/// (workspace inheritance resolved against the workspace root's <c>[workspace.package]</c>),
/// the <c>[lib] crate-type</c> list and the library target's name.
/// </summary>
internal sealed class CrateManifestFacts
{
    /// <summary>The version cargo assumes when a package declares none.</summary>
    public const string DefaultVersion = "0.0.0";

    /// <summary>The package name.</summary>
    public string PackageName { get; private set; }

    /// <summary>The package version; <see cref="DefaultVersion"/> when the manifest declares
    /// none.</summary>
    public string Version { get; private set; }

    /// <summary>The declared crate types (empty = cargo's default, an rlib).</summary>
    public IReadOnlyList<string> CrateTypes { get; private set; } = Array.Empty<string>();

    /// <summary>The library target's name: <c>[lib] name</c> when declared, else the package
    /// name with '-' replaced by '_' (cargo's rule). The base of the native file name.</summary>
    public string LibraryName { get; private set; }

    /// <summary>The workspace root's Cargo.toml (the crate's own when standalone).</summary>
    public string WorkspaceRootManifestPath { get; private set; }

    /// <summary>
    /// Reads the facts of the crate at <paramref name="manifestPath"/>.
    /// </summary>
    /// <param name="manifestPath">The crate's Cargo.toml.</param>
    /// <param name="workspaceRootManifestPath">The workspace root's Cargo.toml, or <c>null</c>
    /// to find it with <see cref="WorkspaceRootFinder"/> (searching no higher than
    /// <paramref name="boundaryDirectory"/>).</param>
    /// <param name="boundaryDirectory">See <see cref="WorkspaceRootFinder.FindRootManifest"/>.</param>
    /// <exception cref="InvalidDataException">A manifest cannot be read, the manifest is a
    /// virtual workspace manifest (no <c>[package]</c>), or an inherited version is missing
    /// from the workspace.</exception>
    public static CrateManifestFacts Read(string manifestPath, string workspaceRootManifestPath = null, string boundaryDirectory = null)
    {
        var fullPath = Path.GetFullPath(manifestPath);
        var rootPath = workspaceRootManifestPath == null ? WorkspaceRootFinder.FindRootManifest(fullPath, boundaryDirectory) : Path.GetFullPath(workspaceRootManifestPath);
        var manifest = WorkspaceRootFinder.LoadManifest(fullPath);
        if (manifest.Package == null || string.IsNullOrWhiteSpace(manifest.Package.Name))
        {
            throw new InvalidDataException(
                $"{fullPath} is a virtual workspace manifest (it has no [package]); name the Cargo.toml of the member crate to build instead.");
        }

        var rootManifest = StagingFileSystem.PathEquals(rootPath, fullPath) ? manifest : WorkspaceRootFinder.LoadManifest(rootPath);
        var package = manifest.Package.WithInheritance(rootManifest.Workspace?.Package);
        if (package.IsInherited("version") && string.IsNullOrWhiteSpace(package.Version))
        {
            throw new InvalidDataException($"{fullPath} inherits its version from the workspace (version.workspace = true), but {rootPath} has no [workspace.package] version.");
        }

        var lib = manifest.Lib;
        return new CrateManifestFacts
        {
            PackageName = package.Name,
            Version = string.IsNullOrWhiteSpace(package.Version) ? DefaultVersion : package.Version,
            CrateTypes = lib?.CrateTypes?.ToList() ?? new List<string>(),
            LibraryName = !string.IsNullOrWhiteSpace(lib?.Name) ? lib.Name : package.Name.Replace('-', '_'),
            WorkspaceRootManifestPath = rootPath,
        };
    }
}
