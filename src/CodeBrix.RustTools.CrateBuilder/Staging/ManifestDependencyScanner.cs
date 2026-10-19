using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.TomlParse.Model;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// Lists the dependency entries of a parsed Cargo.toml that carry a <c>path</c> or a
/// <c>git</c> source. The tables read are <c>[dependencies]</c>, <c>[dev-dependencies]</c>,
/// <c>[build-dependencies]</c> (and their legacy underscore spellings), every
/// <c>[target.'...'.*dependencies]</c> table, <c>[workspace.dependencies]</c>,
/// <c>[patch.&lt;source&gt;]</c> and <c>[replace]</c> - every place where cargo accepts a
/// path. Entries come out in document order.
/// </summary>
internal static class ManifestDependencyScanner
{
    private static readonly string[] DependencyTableNames =
    {
        "dependencies", "dev-dependencies", "dev_dependencies", "build-dependencies", "build_dependencies",
    };

    /// <summary>Scans a manifest's document table.</summary>
    public static IReadOnlyList<ManifestDependencyEntry> Scan(TomlTable document)
    {
        var result = new List<ManifestDependencyEntry>();
        if (document == null)
        {
            return result;
        }

        foreach (var name in DependencyTableNames)
        {
            AddTable(result, document, new[] { name });
        }

        if (document.TryGetValue("target", out var targetValue) && targetValue is TomlTable targets)
        {
            foreach (var target in targets)
            {
                if (target.Value is TomlTable targetTable)
                {
                    foreach (var name in DependencyTableNames)
                    {
                        AddTable(result, targetTable, new[] { "target", target.Key, name }, name);
                    }
                }
            }
        }

        if (document.TryGetValue("workspace", out var workspaceValue) && workspaceValue is TomlTable workspace)
        {
            AddTable(result, workspace, new[] { "workspace", "dependencies" }, "dependencies");
        }

        if (document.TryGetValue("patch", out var patchValue) && patchValue is TomlTable patches)
        {
            foreach (var source in patches)
            {
                if (source.Value is TomlTable sourceTable)
                {
                    AddEntries(result, sourceTable, new[] { "patch", source.Key });
                }
            }
        }

        if (document.TryGetValue("replace", out var replaceValue) && replaceValue is TomlTable replace)
        {
            AddEntries(result, replace, new[] { "replace" });
        }

        return result;
    }

    private static void AddTable(List<ManifestDependencyEntry> result, TomlTable parent, string[] keys, string childName = null)
    {
        var name = childName ?? keys[keys.Length - 1];
        if (parent.TryGetValue(name, out var value) && value is TomlTable table)
        {
            AddEntries(result, table, keys);
        }
    }

    private static void AddEntries(List<ManifestDependencyEntry> result, TomlTable table, string[] keys)
    {
        foreach (var dependency in table)
        {
            if (dependency.Value is not TomlTable entry)
            {
                continue;
            }

            var path = entry.TryGetValue("path", out var pathValue) ? pathValue as string : null;
            var git = entry.TryGetValue("git", out var gitValue) ? gitValue as string : null;
            if (path == null && git == null)
            {
                continue;
            }

            result.Add(new ManifestDependencyEntry
            {
                TableKeys = keys.Append(dependency.Key).ToArray(),
                Name = dependency.Key,
                Path = path,
                Git = git,
                GitReference = Text(entry, "rev") ?? Text(entry, "tag") ?? Text(entry, "branch"),
            });
        }
    }

    private static string Text(TomlTable table, string key)
        => table.TryGetValue(key, out var value) && value is string text && !string.IsNullOrWhiteSpace(text) ? text : null;

    /// <summary>"name @ url#reference" (no "#..." when the entry names no reference).</summary>
    public static string DescribeGit(ManifestDependencyEntry entry)
        => entry.Name + " @ " + entry.Git + (entry.GitReference == null ? string.Empty : "#" + entry.GitReference);

    /// <summary>Whether two key lists are equal (ordinal).</summary>
    public static bool SameKeys(IReadOnlyList<string> a, IReadOnlyList<string> b)
        => a.Count == b.Count && a.Zip(b).All(p => string.Equals(p.First, p.Second, StringComparison.Ordinal));
}
