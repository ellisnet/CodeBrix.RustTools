using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// Finds Cargo.toml files in a folder tree and works out which manifest is the root of the
/// project (preferring the workspace root that contains a member).
/// </summary>
public static class CargoProjectLocator
{
    /// <summary>The default number of folder levels searched below the starting folder.</summary>
    public const int DefaultMaxDepth = 6;

    private static readonly string[] SkippedFolderNames = { "target", "node_modules" };

    /// <summary>
    /// Returns the full path of every Cargo.toml under <paramref name="rootDirectory"/>, sorted.
    /// Folders named <c>target</c> or <c>node_modules</c> and hidden folders (names starting with
    /// '.', which includes <c>.git</c>) are not searched.
    /// </summary>
    /// <param name="rootDirectory">The folder to search.</param>
    /// <param name="maxDepth">How many folder levels below the root to search (0 = the root
    /// folder only).</param>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static IReadOnlyList<string> FindManifests(string rootDirectory, int maxDepth = DefaultMaxDepth)
    {
        var root = RequireDirectory(rootDirectory);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        var result = new List<string>();
        Search(root, 0, maxDepth, result);
        result.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>
    /// Returns the manifests that are project roots: by default only the outermost manifests
    /// (a manifest inside another manifest's folder - a workspace member or a vendored path
    /// crate - is not a separate project). With <paramref name="includeNested"/> the nested
    /// manifests are returned too, except members of a workspace whose root was found.
    /// </summary>
    /// <param name="rootDirectory">The folder to search.</param>
    /// <param name="maxDepth">How many folder levels below the root to search.</param>
    /// <param name="includeNested">Also return nested manifests that are not workspace members.</param>
    /// <exception cref="DirectoryNotFoundException">The folder does not exist.</exception>
    public static IReadOnlyList<string> FindProjectRoots(string rootDirectory, int maxDepth = DefaultMaxDepth, bool includeNested = false)
    {
        var manifests = FindManifests(rootDirectory, maxDepth);
        var directories = manifests.Select(Path.GetDirectoryName).ToList();
        var result = new List<string>();
        var memberDirectories = new HashSet<string>(PathComparer);
        if (includeNested)
        {
            foreach (var manifest in manifests)
            {
                var parsed = TryLoad(manifest);
                if (parsed?.Workspace != null)
                {
                    foreach (var member in ExpandWorkspaceMembers(Path.GetDirectoryName(manifest), parsed.Workspace))
                    {
                        if (!PathEquals(member, Path.GetDirectoryName(manifest)))
                        {
                            memberDirectories.Add(member);
                        }
                    }
                }
            }
        }

        for (var i = 0; i < manifests.Count; i++)
        {
            var directory = directories[i];
            var nested = directories.Any(other => !PathEquals(other, directory) && IsInside(directory, other));
            if (!nested || (includeNested && !memberDirectories.Contains(directory)))
            {
                result.Add(manifests[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds the manifest that owns <paramref name="path"/> (a folder, a file inside a project,
    /// or a Cargo.toml). The nearest Cargo.toml at or above the path is found first; when a
    /// workspace root further up lists it as a member (or is that manifest), the workspace
    /// root's manifest is returned instead.
    /// </summary>
    /// <param name="path">A folder or file path.</param>
    /// <returns>The full path of the root Cargo.toml, or <c>null</c> when there is none.</returns>
    public static string FindRoot(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Directory.Exists(fullPath) ? fullPath : Path.GetDirectoryName(fullPath);
        var nearest = FindNearestManifest(directory);
        if (nearest == null)
        {
            return null;
        }

        return FindWorkspaceRootManifest(Path.GetDirectoryName(nearest)) ?? nearest;
    }

    /// <summary>
    /// Returns the nearest Cargo.toml in <paramref name="directory"/> or one of its parents,
    /// or <c>null</c>.
    /// </summary>
    /// <param name="directory">The folder to start from.</param>
    public static string FindNearestManifest(string directory)
    {
        for (var current = directory == null ? null : new DirectoryInfo(Path.GetFullPath(directory)); current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, CargoManifest.FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the Cargo.toml of the workspace that the package in
    /// <paramref name="packageDirectory"/> belongs to (the outermost manifest at or above it
    /// with a <c>[workspace]</c> table that is that folder or lists it as a member), or
    /// <c>null</c> when the package is not in a workspace.
    /// </summary>
    /// <param name="packageDirectory">The package folder.</param>
    public static string FindWorkspaceRootManifest(string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            return null;
        }

        var target = Path.GetFullPath(packageDirectory);
        string found = null;
        for (var current = new DirectoryInfo(target); current != null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, CargoManifest.FileName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            var manifest = TryLoad(candidate);
            if (manifest?.Workspace == null)
            {
                continue;
            }

            if (PathEquals(current.FullName, target)
                || ExpandWorkspaceMembers(current.FullName, manifest.Workspace).Any(member => PathEquals(member, target)))
            {
                found = candidate;
            }
        }

        return found;
    }

    /// <summary>
    /// Expands a workspace's <c>members</c> entries (with <c>*</c> / <c>?</c> globs in any
    /// path segment) to the full paths of member folders that contain a Cargo.toml, minus the
    /// <c>exclude</c> entries. A root manifest that also has a <c>[package]</c> is not included
    /// here; <see cref="CargoProject.WorkspaceMemberDirectories"/> adds it.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace root folder.</param>
    /// <param name="workspace">The parsed <c>[workspace]</c> table.</param>
    public static IReadOnlyList<string> ExpandWorkspaceMembers(string workspaceDirectory, CargoWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = Path.GetFullPath(workspaceDirectory);
        var excluded = workspace.Exclude.Select(e => Path.GetFullPath(Path.Combine(root, e))).ToList();
        var result = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        foreach (var member in workspace.Members)
        {
            foreach (var directory in ExpandGlob(root, member))
            {
                if (!File.Exists(Path.Combine(directory, CargoManifest.FileName)))
                {
                    continue;
                }

                if (excluded.Any(e => PathEquals(e, directory) || IsInside(directory, e)))
                {
                    continue;
                }

                if (seen.Add(directory))
                {
                    result.Add(directory);
                }
            }
        }

        return result;
    }

    internal static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static bool PathEquals(string a, string b)
        => PathComparer.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b));

    internal static bool IsInside(string path, string directory)
    {
        var prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    internal static CargoManifest TryLoad(string manifestPath)
    {
        try
        {
            return CargoManifest.Load(manifestPath);
        }
        catch (RustToolsException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ExpandGlob(string root, string pattern)
    {
        var segments = pattern.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        IEnumerable<string> current = new[] { root };
        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment.IndexOfAny(new[] { '*', '?', '[' }) < 0)
            {
                current = current.Select(c => Path.GetFullPath(Path.Combine(c, segment))).Where(Directory.Exists).ToList();
                continue;
            }

            var regex = new Regex("^" + Regex.Escape(segment).Replace(@"\*", ".*").Replace(@"\?", ".") + "$", OperatingSystem.IsWindows() ? RegexOptions.IgnoreCase : RegexOptions.None);
            current = current.SelectMany(c => SafeDirectories(c).Where(d => regex.IsMatch(Path.GetFileName(d))).OrderBy(d => d, StringComparer.Ordinal)).ToList();
        }

        return current;
    }

    private static IEnumerable<string> SafeDirectories(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (IOException)
        {
            return Array.Empty<string>();
        }
        catch (UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static void Search(string directory, int depth, int maxDepth, List<string> result)
    {
        var manifest = Path.Combine(directory, CargoManifest.FileName);
        if (File.Exists(manifest))
        {
            result.Add(manifest);
        }

        if (depth >= maxDepth)
        {
            return;
        }

        foreach (var child in SafeDirectories(directory))
        {
            var name = Path.GetFileName(child);
            if (name.StartsWith('.') || SkippedFolderNames.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            Search(child, depth + 1, maxDepth, result);
        }
    }

    private static string RequireDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var full = Path.GetFullPath(directory);
        if (!Directory.Exists(full))
        {
            throw new DirectoryNotFoundException($"The folder '{full}' does not exist.");
        }

        return full;
    }
}
