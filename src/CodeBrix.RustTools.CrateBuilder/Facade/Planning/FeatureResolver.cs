using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Works out which cargo features of the crate are ON for a build, the way cargo does for the
/// crate as a dependency of the generated facade: the requested features, plus
/// <c>default</c> unless default features are turned off (everything with all features), and
/// then every feature those enable, transitively. Optional dependencies that are not named
/// with <c>dep:</c> anywhere are features of the same name, as in cargo.
/// </summary>
internal static class FeatureResolver
{
    /// <summary>The resolved features of one build.</summary>
    internal sealed class Resolution
    {
        /// <summary>Every feature name the crate has (declared ones and the implicit features of
        /// optional dependencies).</summary>
        public HashSet<string> Known { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>The features that are ON.</summary>
        public HashSet<string> Enabled { get; } = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>Resolves the features of <paramref name="manifest"/> for the request's
    /// feature settings.</summary>
    public static Resolution Resolve(CargoManifest manifest, IEnumerable<string> requested, bool noDefaultFeatures, bool allFeatures)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var resolution = new Resolution();
        var table = manifest.Features.ToDictionary(f => f.Name, f => f.Enables ?? (IReadOnlyList<string>)Array.Empty<string>(), StringComparer.Ordinal);
        var explicitDeps = new HashSet<string>(table.Values.SelectMany(v => v).Where(e => e.StartsWith("dep:", StringComparison.Ordinal)).Select(e => e.Substring(4)), StringComparer.Ordinal);
        foreach (var name in table.Keys)
        {
            resolution.Known.Add(name);
        }

        foreach (var dependency in manifest.Dependencies.Where(d => d.Optional && d.Kind != CargoDependencyKind.Dev && !explicitDeps.Contains(d.Name)))
        {
            resolution.Known.Add(dependency.Name);
        }

        var queue = new Queue<string>();
        if (allFeatures)
        {
            foreach (var name in resolution.Known)
            {
                queue.Enqueue(name);
            }
        }
        else
        {
            foreach (var name in (requested ?? Enumerable.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)))
            {
                var trimmed = name.Trim();
                var slash = trimmed.IndexOf('/');
                queue.Enqueue(slash >= 0 && string.Equals(trimmed.Substring(0, slash), manifest.Package?.Name, StringComparison.Ordinal) ? trimmed.Substring(slash + 1) : trimmed);
            }

            if (!noDefaultFeatures && table.ContainsKey("default"))
            {
                queue.Enqueue("default");
            }
        }

        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (name.StartsWith("dep:", StringComparison.Ordinal))
            {
                continue;
            }

            var slash = name.IndexOf('/');
            if (slash >= 0)
            {
                // "dep/feature" turns the optional dependency (its implicit feature) on;
                // "dep?/feature" does not.
                var head = name.Substring(0, slash);
                if (!head.EndsWith('?') && resolution.Known.Contains(head))
                {
                    queue.Enqueue(head);
                }

                continue;
            }

            if (!resolution.Enabled.Add(name))
            {
                continue;
            }

            if (table.TryGetValue(name, out var enables))
            {
                foreach (var next in enables)
                {
                    queue.Enqueue(next);
                }
            }
        }

        return resolution;
    }
}
