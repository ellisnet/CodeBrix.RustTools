using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Asks every <see cref="IBuildContribution"/> for one target and merges the answers into one
/// <see cref="BuildContribution"/>, with <see cref="BuildContribution.WorkspaceToken"/>
/// replaced by the workspace root as cargo sees it.
/// </summary>
internal static class ContributionMerger
{
    public static async Task<BuildContribution> PrepareAsync(
        IReadOnlyList<IBuildContribution> contributions,
        StagedWorkspace workspace,
        CrateTarget target,
        BuildMode mode,
        CrateBuilderContext context,
        string workspaceRoot,
        CancellationToken cancellationToken)
    {
        var merged = new BuildContribution();
        foreach (var contribution in contributions ?? Array.Empty<IBuildContribution>())
        {
            if (contribution == null)
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var part = await contribution.PrepareAsync(workspace, target, mode, context, cancellationToken).ConfigureAwait(false);
            if (part != null)
            {
                Merge(merged, part, workspaceRoot);
            }
        }

        return merged;
    }

    /// <summary>Adds <paramref name="part"/> to <paramref name="into"/>, replacing the token
    /// (a later value for the same variable wins; rustflags and notes accumulate).</summary>
    public static void Merge(BuildContribution into, BuildContribution part, string workspaceRoot)
    {
        foreach (var pair in part.Environment.Where(p => !string.IsNullOrWhiteSpace(p.Key)))
        {
            into.Environment[pair.Key] = Replace(pair.Value, workspaceRoot);
        }

        foreach (var flag in part.RustFlags.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            into.RustFlags.Add(Replace(flag.Trim(), workspaceRoot));
        }

        foreach (var note in part.Notes.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            into.Notes.Add(Replace(note, workspaceRoot));
        }
    }

    /// <summary>Replaces <see cref="BuildContribution.WorkspaceToken"/>; <c>null</c> stays
    /// <c>null</c>.</summary>
    public static string Replace(string value, string workspaceRoot)
        => value?.Replace(BuildContribution.WorkspaceToken, workspaceRoot ?? string.Empty, StringComparison.Ordinal);

    /// <summary>Appends space-separated flags to a rustflags value (either may be
    /// empty).</summary>
    public static string AppendFlags(string existing, IEnumerable<string> flags)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            parts.Add(existing.Trim());
        }

        parts.AddRange(flags.Where(f => !string.IsNullOrWhiteSpace(f)));
        return string.Join(" ", parts);
    }
}
