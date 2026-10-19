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
    /// manifests are returned too, except members of a workspace whose root was found (listed
    /// and implicit members, <see cref="FindWorkspaceMembers"/>).
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
                    foreach (var member in FindWorkspaceMembers(Path.GetDirectoryName(manifest), parsed))
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
    /// or a Cargo.toml). The nearest Cargo.toml at or above the path is found first; when it
    /// belongs to a workspace (<see cref="FindWorkspaceRootManifest(string)"/>, cargo's rule),
    /// the workspace root's manifest is returned instead.
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
    /// <paramref name="packageDirectory"/> belongs to, by cargo's own rule, or <c>null</c> when
    /// the package is not in a workspace (or cargo would refuse it, see the strict overload):
    /// <list type="number">
    /// <item>a manifest with a <c>[workspace]</c> table is its own root;</item>
    /// <item><c>[package] workspace = "..."</c> names the root explicitly;</item>
    /// <item>otherwise the NEAREST folder above the package whose Cargo.toml has a
    /// <c>[workspace]</c> that does not exclude the package
    /// (<see cref="IsExcludedFromWorkspace"/>) is the root, and the package must be one of its
    /// members (<see cref="IsWorkspaceMember"/>: listed, or an implicit member).</item>
    /// </list>
    /// Unreadable manifests are skipped.
    /// </summary>
    /// <param name="packageDirectory">The package folder.</param>
    public static string FindWorkspaceRootManifest(string packageDirectory)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
        {
            return null;
        }

        return FindWorkspaceRootManifest(packageDirectory, null, false);
    }

    /// <summary>
    /// <see cref="FindWorkspaceRootManifest(string)"/> with a search boundary and an optional
    /// strict mode that reports what cargo itself would refuse.
    /// </summary>
    /// <param name="packageDirectory">The package folder (it must hold a Cargo.toml).</param>
    /// <param name="boundaryDirectory">When not <c>null</c>, folders above this one are not
    /// searched (for example a clone's root: a Cargo.toml above the clone is not part of the
    /// repository). The <c>package.workspace</c> pointer is followed wherever it leads.</param>
    /// <param name="throwOnError">False: problems give <c>null</c> (an unreadable manifest
    /// above the package is skipped). True: they throw <see cref="InvalidDataException"/>.</param>
    /// <returns>The full path of the workspace root's Cargo.toml (the package's own when it is
    /// its own root), or <c>null</c> for a standalone package.</returns>
    /// <exception cref="InvalidDataException">Only with <paramref name="throwOnError"/>: a
    /// manifest cannot be read, the <c>package.workspace</c> pointer leads to no
    /// <c>[workspace]</c>, or the package lies inside a workspace that neither lists nor
    /// excludes it (cargo refuses to build it there).</exception>
    public static string FindWorkspaceRootManifest(string packageDirectory, string boundaryDirectory, bool throwOnError)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packageDirectory);
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(packageDirectory));
        var ownPath = Path.Combine(target, CargoManifest.FileName);
        var own = Load(ownPath, throwOnError);
        if (own == null)
        {
            if (throwOnError || File.Exists(ownPath))
            {
                return null;
            }
        }
        else
        {
            if (own.Workspace != null)
            {
                return ownPath;
            }

            var pointer = own.Package?.Workspace;
            if (!string.IsNullOrWhiteSpace(pointer))
            {
                var rootManifest = Path.GetFullPath(Path.Combine(target, pointer, CargoManifest.FileName));
                var root = File.Exists(rootManifest) ? Load(rootManifest, throwOnError) : null;
                if (root?.Workspace != null)
                {
                    return rootManifest;
                }

                if (throwOnError)
                {
                    throw new InvalidDataException($"{ownPath} names the workspace root '{pointer}' (package.workspace), but {rootManifest} does not exist or has no [workspace] table.");
                }

                return null;
            }
        }

        var boundary = boundaryDirectory == null ? null : Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundaryDirectory));
        for (var directory = Path.GetDirectoryName(target); directory != null; directory = Path.GetDirectoryName(directory))
        {
            if (boundary != null && !PathEquals(directory, boundary) && !IsInside(directory, boundary))
            {
                break;
            }

            var candidate = Path.Combine(directory, CargoManifest.FileName);
            if (!File.Exists(candidate))
            {
                continue;
            }

            var manifest = Load(candidate, throwOnError);
            if (manifest?.Workspace == null || IsExcludedFromWorkspace(directory, manifest.Workspace, target))
            {
                continue;
            }

            if (IsWorkspaceMember(directory, manifest, target))
            {
                return candidate;
            }

            if (throwOnError)
            {
                throw new InvalidDataException(
                    $"{ownPath} lies inside the workspace {candidate} but is not one of its members; cargo refuses to build it there. "
                    + "Add the crate to the workspace's members, or to its exclude list.");
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Whether cargo treats <paramref name="packageDirectory"/> as excluded from the workspace
    /// rooted at <paramref name="workspaceDirectory"/>: the folder is at or below an
    /// <c>exclude</c> entry and NOT at or below a literal <c>members</c> entry (an explicit member
    /// is never excluded).
    /// </summary>
    /// <param name="workspaceDirectory">The workspace root folder.</param>
    /// <param name="workspace">Its parsed <c>[workspace]</c> table.</param>
    /// <param name="packageDirectory">The package folder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="workspace"/> is <c>null</c>.</exception>
    public static bool IsExcludedFromWorkspace(string workspaceDirectory, CargoWorkspace workspace, string packageDirectory)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = Path.GetFullPath(workspaceDirectory);
        var package = Path.GetFullPath(packageDirectory);
        bool Under(string entry)
        {
            var folder = Path.GetFullPath(Path.Combine(root, entry));
            return PathEquals(package, folder) || IsInside(package, folder);
        }

        var excluded = workspace.Exclude.Where(e => !string.IsNullOrWhiteSpace(e)).Any(Under);
        var explicitMember = workspace.Members.Where(m => !string.IsNullOrWhiteSpace(m) && m.IndexOfAny(GlobCharacters) < 0).Any(Under);
        return excluded && !explicitMember;
    }

    /// <summary>
    /// Whether <paramref name="packageDirectory"/> is a member of the workspace rooted at
    /// <paramref name="workspaceDirectory"/> (see <see cref="FindWorkspaceMembers"/>).
    /// </summary>
    /// <param name="workspaceDirectory">The workspace root folder.</param>
    /// <param name="workspaceManifest">The root's parsed Cargo.toml (it must have a
    /// <c>[workspace]</c>).</param>
    /// <param name="packageDirectory">The package folder.</param>
    /// <exception cref="ArgumentNullException"><paramref name="workspaceManifest"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The manifest has no <c>[workspace]</c> table.</exception>
    public static bool IsWorkspaceMember(string workspaceDirectory, CargoManifest workspaceManifest, string packageDirectory)
    {
        var package = Path.GetFullPath(packageDirectory);
        return FindWorkspaceMembers(workspaceDirectory, workspaceManifest).Any(m => PathEquals(m, package));
    }

    /// <summary>
    /// Every member folder of a workspace by cargo's rule: the root folder itself when the root
    /// also has a <c>[package]</c>, each <c>members</c> entry (globs expanded, see
    /// <see cref="ExpandWorkspaceMembers"/>), and the IMPLICIT members - path dependencies
    /// (also through a workspace-inherited <c>[workspace.dependencies]</c> path entry) of a member
    /// that lie inside the workspace folder and are not excluded, followed transitively. Only
    /// folders that hold a Cargo.toml are returned, each once, in discovery order.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace root folder.</param>
    /// <param name="workspaceManifest">The root's parsed Cargo.toml (it must have a
    /// <c>[workspace]</c>).</param>
    /// <exception cref="ArgumentNullException"><paramref name="workspaceManifest"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The manifest has no <c>[workspace]</c> table.</exception>
    public static IReadOnlyList<string> FindWorkspaceMembers(string workspaceDirectory, CargoManifest workspaceManifest)
    {
        ArgumentNullException.ThrowIfNull(workspaceManifest);
        var workspace = workspaceManifest.Workspace ?? throw new ArgumentException("The manifest has no [workspace] table.", nameof(workspaceManifest));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceDirectory));
        var members = new List<string>();
        var seen = new HashSet<string>(PathComparer);
        void Add(string directory)
        {
            var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (File.Exists(Path.Combine(full, CargoManifest.FileName)) && seen.Add(full))
            {
                members.Add(full);
            }
        }

        if (workspaceManifest.Package != null)
        {
            Add(root);
        }

        foreach (var member in ExpandWorkspaceMembers(root, workspace))
        {
            Add(member);
        }

        for (var i = 0; i < members.Count; i++)
        {
            var manifest = PathEquals(members[i], root) ? workspaceManifest : TryLoad(Path.Combine(members[i], CargoManifest.FileName));
            if (manifest == null)
            {
                continue;
            }

            foreach (var dependency in manifest.Dependencies)
            {
                string dependencyDirectory = null;
                if (dependency.IsPath)
                {
                    dependencyDirectory = Path.GetFullPath(Path.Combine(members[i], dependency.Path));
                }
                else if (dependency.IsWorkspaceInherited)
                {
                    var inherited = workspace.Dependencies.FirstOrDefault(d => string.Equals(d.Name, dependency.Name, StringComparison.Ordinal));
                    if (inherited != null && inherited.IsPath)
                    {
                        dependencyDirectory = Path.GetFullPath(Path.Combine(root, inherited.Path));
                    }
                }

                if (dependencyDirectory != null
                    && (PathEquals(dependencyDirectory, root) || IsInside(dependencyDirectory, root))
                    && !IsExcludedFromWorkspace(root, workspace, dependencyDirectory))
                {
                    Add(dependencyDirectory);
                }
            }
        }

        return members;
    }

    /// <summary>
    /// Expands a workspace's <c>members</c> entries (with <c>*</c> / <c>?</c> globs in any
    /// path segment) to the full paths of member folders that contain a Cargo.toml, minus the
    /// ones <see cref="IsExcludedFromWorkspace"/> rules out (an <c>exclude</c> entry never
    /// removes a literal <c>members</c> entry). A root manifest that also has a
    /// <c>[package]</c> is not included here, and neither are implicit members;
    /// <see cref="FindWorkspaceMembers"/> and <see cref="CargoProject.WorkspaceMemberDirectories"/>
    /// add them.
    /// </summary>
    /// <param name="workspaceDirectory">The workspace root folder.</param>
    /// <param name="workspace">The parsed <c>[workspace]</c> table.</param>
    public static IReadOnlyList<string> ExpandWorkspaceMembers(string workspaceDirectory, CargoWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var root = Path.GetFullPath(workspaceDirectory);
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

                if (IsExcludedFromWorkspace(root, workspace, directory))
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

    private static readonly char[] GlobCharacters = { '*', '?', '[' };

    private static CargoManifest Load(string manifestPath, bool throwOnError)
    {
        if (!throwOnError)
        {
            return File.Exists(manifestPath) ? TryLoad(manifestPath) : null;
        }

        try
        {
            return CargoManifest.Load(manifestPath);
        }
        catch (RustToolsException ex)
        {
            throw new InvalidDataException($"{manifestPath} cannot be read: {ex.Message}", ex);
        }
        catch (FileNotFoundException ex)
        {
            throw new InvalidDataException($"{manifestPath} does not exist.", ex);
        }
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
