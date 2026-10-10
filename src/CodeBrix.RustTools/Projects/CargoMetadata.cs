using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// The output of <c>cargo metadata --format-version 1</c>: every package in the graph, the
/// resolved dependency edges, the workspace members, and the workspace and target folders.
/// </summary>
public sealed class CargoMetadata
{
    /// <summary>The source string of crates.io packages (git index protocol).</summary>
    public const string CratesIoSource = "registry+https://github.com/rust-lang/crates.io-index";

    /// <summary>The source string of crates.io packages (sparse index protocol).</summary>
    public const string CratesIoSparseSource = "sparse+https://index.crates.io/";

    private Dictionary<string, CargoMetadataPackage> _byId;

    /// <summary>Every package in the graph.</summary>
    public IReadOnlyList<CargoMetadataPackage> Packages { get; init; } = Array.Empty<CargoMetadataPackage>();

    /// <summary>The package ids of the workspace members.</summary>
    public IReadOnlyList<string> WorkspaceMembers { get; init; } = Array.Empty<string>();

    /// <summary>The package ids of the default workspace members.</summary>
    public IReadOnlyList<string> WorkspaceDefaultMembers { get; init; } = Array.Empty<string>();

    /// <summary>The resolved graph nodes; empty when run with <c>--no-deps</c>.</summary>
    public IReadOnlyList<CargoResolveNode> ResolveNodes { get; init; } = Array.Empty<CargoResolveNode>();

    /// <summary>The package id of the root package (the package whose manifest was given), or
    /// <c>null</c> for a virtual workspace or <c>--no-deps</c>.</summary>
    public string ResolveRoot { get; init; }

    /// <summary>The workspace root folder.</summary>
    public string WorkspaceRoot { get; init; } = string.Empty;

    /// <summary>The target (build output) folder.</summary>
    public string TargetDirectory { get; init; } = string.Empty;

    /// <summary>The metadata format version (1).</summary>
    public int FormatVersion { get; init; }

    /// <summary>True when the graph was resolved (cargo was not run with <c>--no-deps</c>).</summary>
    public bool HasResolve => ResolveNodes.Count > 0;

    /// <summary>
    /// Returns the package with the given id, or <c>null</c>.
    /// </summary>
    /// <param name="id">A package id.</param>
    public CargoMetadataPackage GetPackage(string id)
    {
        if (id == null)
        {
            return null;
        }

        _byId ??= Packages.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return _byId.TryGetValue(id, out var package) ? package : null;
    }

    /// <summary>
    /// Returns every package with the given name (several when different versions are in the
    /// graph).
    /// </summary>
    /// <param name="name">The package name.</param>
    public IReadOnlyList<CargoMetadataPackage> FindPackages(string name)
        => Packages.Where(p => string.Equals(p.Name, name, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// True when the package id is a workspace member.
    /// </summary>
    /// <param name="id">A package id.</param>
    public bool IsWorkspaceMember(string id) => id != null && WorkspaceMembers.Contains(id, StringComparer.Ordinal);

    /// <summary>
    /// Returns the workspace member packages.
    /// </summary>
    public IReadOnlyList<CargoMetadataPackage> GetWorkspacePackages()
        => WorkspaceMembers.Select(GetPackage).Where(p => p != null).ToList();

    /// <summary>
    /// Returns the resolve node of a package, or <c>null</c>.
    /// </summary>
    /// <param name="id">A package id.</param>
    public CargoResolveNode GetNode(string id)
        => ResolveNodes.FirstOrDefault(n => string.Equals(n.Id, id, StringComparison.Ordinal));

    /// <summary>
    /// Returns the package ids a build of the given packages compiles, as far as the resolve
    /// graph tells: the root packages plus every package reachable from them over normal and
    /// build dependency edges (dev-dependency edges are skipped, except the roots' own when
    /// <paramref name="includeRootDevDependencies"/> is true, as for <c>cargo test</c> or
    /// <c>--all-targets</c>). Each package id appears once, roots first. An edge without
    /// <c>dep_kinds</c> (an old cargo) counts as normal. Run <c>cargo metadata</c> with
    /// <see cref="CargoMetadataOptions.FilterPlatform"/> for the build's target triple so that
    /// other platforms' dependencies are not in the graph. Empty when the metadata has no
    /// resolve graph (<c>--no-deps</c>).
    /// </summary>
    /// <param name="rootPackageIds">The package ids to start from (for example
    /// <see cref="WorkspaceDefaultMembers"/>).</param>
    /// <param name="includeRootDevDependencies">Also follow the dev-dependency edges of the
    /// root packages.</param>
    /// <exception cref="ArgumentNullException"><paramref name="rootPackageIds"/> is
    /// null.</exception>
    public IReadOnlyList<string> GetBuildClosure(IEnumerable<string> rootPackageIds, bool includeRootDevDependencies = false)
    {
        ArgumentNullException.ThrowIfNull(rootPackageIds);
        var result = new List<string>();
        if (!HasResolve)
        {
            return result;
        }

        var nodes = new Dictionary<string, CargoResolveNode>(StringComparer.Ordinal);
        foreach (var node in ResolveNodes)
        {
            nodes.TryAdd(node.Id, node);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var roots = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        foreach (var id in rootPackageIds)
        {
            if (id != null && nodes.ContainsKey(id) && seen.Add(id))
            {
                roots.Add(id);
                result.Add(id);
                queue.Enqueue(id);
            }
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            var followDev = includeRootDevDependencies && roots.Contains(id);
            foreach (var dep in nodes[id].Deps)
            {
                if (string.IsNullOrEmpty(dep.PackageId) || !nodes.ContainsKey(dep.PackageId))
                {
                    continue;
                }

                var compiled = dep.HasKind(CargoDependencyKind.Normal) || dep.HasKind(CargoDependencyKind.Build) || (followDev && dep.HasKind(CargoDependencyKind.Dev));
                if (compiled && seen.Add(dep.PackageId))
                {
                    result.Add(dep.PackageId);
                    queue.Enqueue(dep.PackageId);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Joins the metadata of several runs of the same project (for example one
    /// <c>--filter-platform</c> run per target triple) into one whose packages are the union of
    /// theirs, for a license audit that covers every target. The workspace data and the resolve
    /// root come from the first; the resolve nodes are the union by package id.
    /// </summary>
    internal static CargoMetadata Union(IReadOnlyList<CargoMetadata> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 1)
        {
            return parts[0];
        }

        var first = parts[0];
        var packages = new List<CargoMetadataPackage>();
        var nodes = new List<CargoResolveNode>();
        var packageIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var part in parts)
        {
            packages.AddRange(part.Packages.Where(p => packageIds.Add(p.Id)));
            nodes.AddRange(part.ResolveNodes.Where(n => nodeIds.Add(n.Id)));
        }

        return new CargoMetadata
        {
            Packages = packages,
            WorkspaceMembers = first.WorkspaceMembers,
            WorkspaceDefaultMembers = first.WorkspaceDefaultMembers,
            ResolveNodes = nodes,
            ResolveRoot = first.ResolveRoot,
            WorkspaceRoot = first.WorkspaceRoot,
            TargetDirectory = first.TargetDirectory,
            FormatVersion = first.FormatVersion,
        };
    }

    /// <summary>
    /// Parses the JSON printed by <c>cargo metadata --format-version 1</c>.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <exception cref="RustToolsException">The text is not a cargo metadata document.</exception>
    public static CargoMetadata Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("packages", out var packagesElement) || packagesElement.ValueKind != JsonValueKind.Array)
            {
                throw new RustToolsException("The text is not cargo metadata output (no 'packages' array).");
            }

            var packages = packagesElement.EnumerateArray().Select(ParsePackage).ToList();
            var nodes = new List<CargoResolveNode>();
            string resolveRoot = null;
            if (root.TryGetProperty("resolve", out var resolve) && resolve.ValueKind == JsonValueKind.Object)
            {
                resolveRoot = GetString(resolve, "root");
                if (resolve.TryGetProperty("nodes", out var nodeArray) && nodeArray.ValueKind == JsonValueKind.Array)
                {
                    nodes.AddRange(nodeArray.EnumerateArray().Select(ParseNode));
                }
            }

            return new CargoMetadata
            {
                Packages = packages,
                WorkspaceMembers = GetStringArray(root, "workspace_members"),
                WorkspaceDefaultMembers = GetStringArray(root, "workspace_default_members"),
                ResolveNodes = nodes,
                ResolveRoot = resolveRoot,
                WorkspaceRoot = GetString(root, "workspace_root") ?? string.Empty,
                TargetDirectory = GetString(root, "target_directory") ?? string.Empty,
                FormatVersion = root.TryGetProperty("version", out var version) && version.TryGetInt32(out var number) ? number : 0,
            };
        }
        catch (JsonException ex)
        {
            throw new RustToolsException($"The cargo metadata output is not valid JSON: {ex.Message}", ex);
        }
    }

    private static CargoMetadataPackage ParsePackage(JsonElement element)
    {
        var targets = new List<CargoMetadataTarget>();
        if (element.TryGetProperty("targets", out var targetArray) && targetArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var target in targetArray.EnumerateArray())
            {
                targets.Add(new CargoMetadataTarget
                {
                    Name = GetString(target, "name") ?? string.Empty,
                    Kinds = GetStringArray(target, "kind").Select(CargoTargetKinds.Parse).ToList(),
                    CrateTypes = GetStringArray(target, "crate_types").Select(CargoTargetKinds.Parse).ToList(),
                    SrcPath = GetString(target, "src_path") ?? string.Empty,
                    Edition = GetString(target, "edition"),
                    RequiredFeatures = GetStringArray(target, "required-features"),
                });
            }
        }

        var dependencies = new List<CargoMetadataDependency>();
        if (element.TryGetProperty("dependencies", out var dependencyArray) && dependencyArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var dependency in dependencyArray.EnumerateArray())
            {
                dependencies.Add(new CargoMetadataDependency
                {
                    Name = GetString(dependency, "name") ?? string.Empty,
                    Requirement = GetString(dependency, "req"),
                    Kind = ParseKind(GetString(dependency, "kind")),
                    Optional = dependency.TryGetProperty("optional", out var optional) && optional.ValueKind == JsonValueKind.True,
                    UsesDefaultFeatures = !(dependency.TryGetProperty("uses_default_features", out var defaults) && defaults.ValueKind == JsonValueKind.False),
                    Features = GetStringArray(dependency, "features"),
                    Target = GetString(dependency, "target"),
                    Rename = GetString(dependency, "rename"),
                    Source = GetString(dependency, "source"),
                    Path = GetString(dependency, "path"),
                });
            }
        }

        var features = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        if (element.TryGetProperty("features", out var featureObject) && featureObject.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in featureObject.EnumerateObject())
            {
                features[property.Name] = GetStringArray(featureObject, property.Name);
            }
        }

        return new CargoMetadataPackage
        {
            Id = GetString(element, "id") ?? string.Empty,
            Name = GetString(element, "name") ?? string.Empty,
            Version = GetString(element, "version") ?? string.Empty,
            License = GetString(element, "license"),
            LicenseFile = GetString(element, "license_file"),
            ManifestPath = GetString(element, "manifest_path") ?? string.Empty,
            Source = GetString(element, "source"),
            Description = GetString(element, "description"),
            Repository = GetString(element, "repository"),
            Edition = GetString(element, "edition"),
            RustVersion = GetString(element, "rust_version"),
            Targets = targets,
            Dependencies = dependencies,
            Features = features,
        };
    }

    private static CargoResolveNode ParseNode(JsonElement element)
    {
        var deps = new List<CargoResolveDependency>();
        if (element.TryGetProperty("deps", out var depArray) && depArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var dep in depArray.EnumerateArray())
            {
                var kinds = new List<CargoResolveDependencyKind>();
                if (dep.TryGetProperty("dep_kinds", out var kindArray) && kindArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var kind in kindArray.EnumerateArray())
                    {
                        kinds.Add(new CargoResolveDependencyKind { Kind = ParseKind(GetString(kind, "kind")), Target = GetString(kind, "target") });
                    }
                }

                deps.Add(new CargoResolveDependency
                {
                    Name = GetString(dep, "name") ?? string.Empty,
                    PackageId = GetString(dep, "pkg") ?? string.Empty,
                    DepKinds = kinds,
                });
            }
        }
        else if (element.TryGetProperty("dependencies", out var idArray) && idArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in idArray.EnumerateArray())
            {
                if (id.ValueKind == JsonValueKind.String)
                {
                    deps.Add(new CargoResolveDependency { Name = string.Empty, PackageId = id.GetString() });
                }
            }
        }

        return new CargoResolveNode
        {
            Id = GetString(element, "id") ?? string.Empty,
            Deps = deps,
            Features = GetStringArray(element, "features"),
        };
    }

    private static CargoDependencyKind ParseKind(string text) => text switch
    {
        "dev" => CargoDependencyKind.Dev,
        "build" => CargoDependencyKind.Build,
        _ => CargoDependencyKind.Normal,
    };

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static IReadOnlyList<string> GetStringArray(JsonElement element, string name)
    {
        var result = new List<string>();
        if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    result.Add(item.GetString());
                }
            }
        }

        return result;
    }
}
