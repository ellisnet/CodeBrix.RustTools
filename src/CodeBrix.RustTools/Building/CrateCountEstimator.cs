using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Estimates <see cref="CargoProgress.Total"/> for a cargo command from <c>cargo metadata</c>
/// run with <c>--filter-platform</c> for the build's target: the packages the selected
/// packages reach over normal and build dependency edges
/// (<see cref="CargoMetadata.GetBuildClosure"/>).
/// </summary>
internal static class CrateCountEstimator
{
    private static readonly string[] WorkspaceFlags = { "--workspace", "--all" };

    private static readonly string[] DevTargetFlags = { "--all-targets", "--tests", "--test", "--benches", "--bench", "--examples", "--example" };

    /// <summary>
    /// The value for <c>--filter-platform</c>: the target triple, or <c>null</c> (no filter)
    /// when it is unknown or a custom target specification file (<c>*.json</c>).
    /// </summary>
    public static string FilterPlatformFor(string targetTriple)
    {
        if (string.IsNullOrWhiteSpace(targetTriple))
        {
            return null;
        }

        var triple = targetTriple.Trim();
        return triple.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? null : triple;
    }

    /// <summary>
    /// The estimated number of distinct packages the command compiles, or <c>null</c> when the
    /// metadata has no resolve graph or a selected package cannot be found in it.
    /// </summary>
    public static int? Estimate(CargoMetadata metadata, CargoCommand command, CargoBuildOptions options)
    {
        if (metadata == null || !metadata.HasResolve)
        {
            return null;
        }

        options ??= new CargoBuildOptions();
        var roots = SelectRoots(metadata, options);
        if (roots == null || roots.Count == 0)
        {
            return null;
        }

        var closure = metadata.GetBuildClosure(roots, IncludesRootDevDependencies(command, options));
        return closure.Count > 0 ? closure.Count : null;
    }

    /// <summary>
    /// True when cargo also builds the dev-dependencies of the selected packages: for
    /// <c>cargo test</c>, <see cref="CargoBuildOptions.AllTargets"/>,
    /// <see cref="CargoBuildOptions.Examples"/> and the matching extra arguments.
    /// </summary>
    public static bool IncludesRootDevDependencies(CargoCommand command, CargoBuildOptions options)
        => command == CargoCommand.Test
            || options.AllTargets
            || options.Examples.Count > 0
            || options.ExtraArguments.Any(a => DevTargetFlags.Contains(a, StringComparer.Ordinal));

    /// <summary>
    /// The package ids cargo acts on: <see cref="CargoBuildOptions.Packages"/> when set (each
    /// <c>-p</c> spec matched by name and optional version, workspace members first);
    /// otherwise every workspace member for <c>--workspace</c> / <c>--all</c>; otherwise the
    /// package of the manifest when it is not the workspace root; otherwise the default
    /// workspace members, or the root package, or every member. <c>null</c> when a
    /// <c>-p</c> spec matches nothing.
    /// </summary>
    public static IReadOnlyList<string> SelectRoots(CargoMetadata metadata, CargoBuildOptions options)
    {
        var specs = options.Packages.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).ToList();
        if (specs.Count > 0)
        {
            var result = new List<string>();
            foreach (var spec in specs)
            {
                var matches = MatchSpec(metadata, spec);
                if (matches.Count == 0)
                {
                    return null;
                }

                result.AddRange(matches.Where(id => !result.Contains(id, StringComparer.Ordinal)));
            }

            return result;
        }

        if (options.ExtraArguments.Any(a => WorkspaceFlags.Contains(a, StringComparer.Ordinal)))
        {
            return metadata.WorkspaceMembers;
        }

        var root = metadata.GetPackage(metadata.ResolveRoot);
        if (root != null && !SameFolder(ParentOf(root.ManifestPath), metadata.WorkspaceRoot))
        {
            return new[] { root.Id };
        }

        if (metadata.WorkspaceDefaultMembers.Count > 0)
        {
            return metadata.WorkspaceDefaultMembers;
        }

        return root != null ? new[] { root.Id } : metadata.WorkspaceMembers;
    }

    /// <summary>
    /// The package ids a <c>-p</c> spec names: <c>name</c>, <c>name@version</c>,
    /// <c>name:version</c> or a package id URL whose fragment is one of those (or a bare
    /// version after the folder name).
    /// </summary>
    internal static IReadOnlyList<string> MatchSpec(CargoMetadata metadata, string spec)
    {
        var text = spec;
        string name;
        string version = null;
        var hash = text.LastIndexOf('#');
        if (hash >= 0)
        {
            var fragment = text.Substring(hash + 1);
            var url = text.Substring(0, hash).TrimEnd('/');
            if (fragment.Length > 0 && char.IsDigit(fragment[0]))
            {
                name = url.Substring(url.LastIndexOfAny(new[] { '/', '\\' }) + 1);
                version = fragment;
            }
            else
            {
                (name, version) = SplitNameAndVersion(fragment);
            }
        }
        else
        {
            (name, version) = SplitNameAndVersion(text);
        }

        var candidates = metadata.Packages
            .Where(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            .Where(p => version == null || p.Version == version || p.Version.StartsWith(version + ".", StringComparison.Ordinal))
            .ToList();
        var members = candidates.Where(p => metadata.IsWorkspaceMember(p.Id)).ToList();
        return (members.Count > 0 ? members : candidates).Select(p => p.Id).ToList();
    }

    private static (string Name, string Version) SplitNameAndVersion(string text)
    {
        var separator = text.IndexOf('@');
        if (separator < 0)
        {
            separator = text.IndexOf(':');
        }

        return separator < 0 ? (text, null) : (text.Substring(0, separator), text.Substring(separator + 1));
    }

    private static string ParentOf(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        var cut = path.LastIndexOfAny(new[] { '/', '\\' });
        return cut <= 0 ? path : path.Substring(0, cut);
    }

    private static bool SameFolder(string a, string b)
        => string.Equals(Normalize(a), Normalize(b), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
}
