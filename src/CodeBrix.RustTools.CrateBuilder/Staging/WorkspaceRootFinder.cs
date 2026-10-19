using System;
using System.IO;
using CodeBrix.RustTools.Projects;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// Finds the workspace a crate belongs to by cargo's own rule, which lives in
/// CodeBrix.RustTools (<see cref="CargoProjectLocator.FindWorkspaceRootManifest(string, string, bool)"/>,
/// strict mode): a manifest with a <c>[workspace]</c> table is its own root;
/// <c>[package] workspace = "..."</c> names the root explicitly; otherwise the nearest folder
/// above the crate whose Cargo.toml has a <c>[workspace]</c> that does not exclude the crate is
/// the root, and the crate must then be a member of it (listed in <c>members</c>, globs
/// included, or a path dependency of a member that lies inside the workspace folder). No such
/// folder = a standalone crate.
/// </summary>
/// <remarks>Problems are reported as <see cref="InvalidDataException"/>; the source resolver
/// and the stager wrap them in their own stage's exception.</remarks>
internal static class WorkspaceRootFinder
{
    /// <summary>
    /// Returns the full path of the workspace root's Cargo.toml for the crate whose manifest is
    /// <paramref name="crateManifestPath"/>: that manifest itself when the crate is standalone
    /// or is its own workspace root.
    /// </summary>
    /// <param name="crateManifestPath">The crate's Cargo.toml.</param>
    /// <param name="boundaryDirectory">When not <c>null</c>, folders above this one are not
    /// searched (a clone's root: a Cargo.toml above the clone is not part of the
    /// repository).</param>
    /// <exception cref="InvalidDataException">A manifest cannot be read, a
    /// <c>package.workspace</c> pointer leads nowhere, or the crate lies in a workspace that
    /// neither lists nor excludes it (cargo refuses to build it).</exception>
    public static string FindRootManifest(string crateManifestPath, string boundaryDirectory = null)
    {
        var crateManifest = Path.GetFullPath(crateManifestPath);
        return CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(crateManifest), boundaryDirectory, throwOnError: true)
            ?? crateManifest;
    }

    /// <summary>Reads a Cargo.toml with the RustTools manifest model.</summary>
    /// <exception cref="InvalidDataException">The file is not valid TOML.</exception>
    public static CargoManifest LoadManifest(string manifestPath)
    {
        try
        {
            return CargoManifest.Load(manifestPath);
        }
        catch (RustToolsException ex)
        {
            throw new InvalidDataException($"{manifestPath} cannot be read: {ex.Message}", ex);
        }
    }

    /// <summary>Reads a Cargo.toml as a TOML document table.</summary>
    /// <exception cref="InvalidDataException">The file is not valid TOML.</exception>
    public static TomlTable ReadTable(string manifestPath)
    {
        try
        {
            return TomlSerializer.Deserialize<TomlTable>(File.ReadAllText(manifestPath), new TomlSerializerOptions { SourceName = manifestPath }) ?? new TomlTable();
        }
        catch (TomlException ex)
        {
            throw new InvalidDataException($"{manifestPath} is not valid TOML: {ex.Message}", ex);
        }
    }
}
