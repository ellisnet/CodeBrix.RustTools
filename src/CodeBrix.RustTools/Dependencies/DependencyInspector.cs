using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Dependencies;

/// <summary>
/// Answers dependency-health questions about a resolved graph from <c>cargo metadata</c>:
/// the flat package list, a <c>cargo tree</c>-style listing, crates present in several
/// versions, and (through crates.io) outdated or yanked crates.
/// </summary>
public sealed class DependencyInspector
{
    /// <summary>
    /// Creates an inspector over collected metadata.
    /// </summary>
    /// <param name="metadata">The output of <see cref="CargoProject.GetMetadataAsync"/> (run
    /// without <see cref="CargoMetadataOptions.NoDependencies"/> for a graph).</param>
    public DependencyInspector(CargoMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        Metadata = metadata;
    }

    /// <summary>The metadata being inspected.</summary>
    public CargoMetadata Metadata { get; }

    /// <summary>Every package in the graph, sorted by name then version.</summary>
    public IReadOnlyList<CargoMetadataPackage> Packages
        => Metadata.Packages.OrderBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => SortableVersion(p.Version)).ToList();

    /// <summary>
    /// Builds a text listing of the graph in the style of <c>cargo tree</c>: one root per
    /// workspace member (or the resolve root), children indented with box-drawing characters,
    /// local packages followed by their folder, and a package already expanded elsewhere marked
    /// <c>(*)</c> instead of being repeated (which also makes the listing safe for cycles).
    /// </summary>
    /// <param name="maxDepth">The deepest level listed (0 = the roots only).</param>
    /// <param name="includeDevDependencies">Also follow dev-dependency edges (of the roots
    /// only, as cargo does).</param>
    public IReadOnlyList<string> GetDependencyTree(int maxDepth = int.MaxValue, bool includeDevDependencies = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxDepth);
        var lines = new List<string>();
        var roots = Metadata.WorkspaceMembers.Count > 0
            ? Metadata.WorkspaceMembers.ToList()
            : Metadata.ResolveRoot == null ? new List<string>() : new List<string> { Metadata.ResolveRoot };
        var expanded = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in roots)
        {
            if (lines.Count > 0)
            {
                lines.Add(string.Empty);
            }

            lines.Add(Describe(root));
            expanded.Add(root);
            AddChildren(lines, root, string.Empty, 1, maxDepth, includeDevDependencies, expanded, new HashSet<string>(StringComparer.Ordinal) { root });
        }

        return lines;
    }

    /// <summary>
    /// Returns the crates present in more than one version, sorted by name.
    /// </summary>
    public IReadOnlyList<DuplicateCrate> FindDuplicateVersions()
        => Metadata.Packages
            .GroupBy(p => p.Name, StringComparer.Ordinal)
            .Select(g => new DuplicateCrate
            {
                Name = g.Key,
                Versions = g.Select(p => p.Version).Distinct(StringComparer.Ordinal).OrderBy(SortableVersion).ToList(),
            })
            .Where(d => d.Versions.Count > 1)
            .OrderBy(d => d.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Looks up every crates.io package of the graph (path, git and other-registry packages are
    /// skipped) and returns those with a newer release or whose installed version was yanked.
    /// One request is made per crate name; crates.io's request spacing in
    /// <see cref="RustWorkbenchOptions.CratesIoRequestInterval"/> applies.
    /// </summary>
    /// <param name="client">The crates.io client.</param>
    /// <param name="cancellationToken">Cancels the lookups.</param>
    /// <exception cref="RustToolsException">A crates.io request failed.</exception>
    public async Task<IReadOnlyList<OutdatedCrate>> CheckOutdatedAsync(CratesIoClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        var result = new List<OutdatedCrate>();
        foreach (var group in Metadata.Packages.Where(p => p.IsCratesIo).GroupBy(p => p.Name, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var info = await client.GetCrateAsync(group.Key, cancellationToken).ConfigureAwait(false);
            if (info == null)
            {
                continue;
            }

            var latest = info.LatestVersion;
            foreach (var installed in group.Select(p => p.Version).Distinct(StringComparer.Ordinal).OrderBy(SortableVersion))
            {
                var yanked = info.IsYanked(installed) ?? false;
                var outdated = latest != null && IsNewer(latest, installed);
                if (yanked || outdated)
                {
                    result.Add(new OutdatedCrate
                    {
                        Name = group.Key,
                        InstalledVersion = installed,
                        LatestVersion = latest,
                        IsYanked = yanked,
                        IsOutdated = outdated,
                    });
                }
            }
        }

        return result;
    }

    private void AddChildren(List<string> lines, string id, string prefix, int depth, int maxDepth, bool includeDev, HashSet<string> expanded, HashSet<string> path)
    {
        if (depth > maxDepth)
        {
            return;
        }

        var node = Metadata.GetNode(id);
        if (node == null)
        {
            return;
        }

        var children = node.Deps
            .Where(d => d.HasKind(CargoDependencyKind.Normal) || d.HasKind(CargoDependencyKind.Build) || (includeDev && depth == 1 && d.HasKind(CargoDependencyKind.Dev)))
            .Select(d => d.PackageId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => Metadata.GetPackage(c)?.Name ?? c, StringComparer.Ordinal)
            .ToList();
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            var last = i == children.Count - 1;
            var repeated = path.Contains(child) || !expanded.Add(child);
            var hasChildren = Metadata.GetNode(child)?.Deps.Count > 0;
            lines.Add(prefix + (last ? "└── " : "├── ") + Describe(child) + (repeated && hasChildren ? " (*)" : string.Empty));
            if (!repeated)
            {
                path.Add(child);
                AddChildren(lines, child, prefix + (last ? "    " : "│   "), depth + 1, maxDepth, includeDev, expanded, path);
                path.Remove(child);
            }
        }
    }

    private string Describe(string id)
    {
        var package = Metadata.GetPackage(id);
        if (package == null)
        {
            return id;
        }

        var text = $"{package.Name} v{package.Version}";
        return package.IsLocal && package.ManifestDirectory != null ? $"{text} ({package.ManifestDirectory})" : text;
    }

    private static bool IsNewer(string candidate, string installed)
        => ToolVersion.TryParse(candidate, out var a) && ToolVersion.TryParse(installed, out var b)
            ? a > b
            : !string.Equals(candidate, installed, StringComparison.Ordinal);

    private static ToolVersion SortableVersion(string version)
        => ToolVersion.TryParse(version, out var parsed) ? parsed : new ToolVersion(0, 0, 0);
}
