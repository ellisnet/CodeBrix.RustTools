using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The answer to "can this machine build the request, and which of its targets?", produced
/// before anything else runs. Nothing is ever installed: the guidance says what the user
/// should install.
/// </summary>
public sealed class CrateReadinessReport
{
    private IReadOnlyDictionary<CrateTarget, IReadOnlyList<InstallGuidance>> _targetGuidance = new Dictionary<CrateTarget, IReadOnlyList<InstallGuidance>>();

    /// <summary>True when nothing blocks the build and at least one target is
    /// buildable.</summary>
    public bool IsReady { get; set; }

    /// <summary>The mode that was checked.</summary>
    public BuildMode Mode { get; set; }

    /// <summary>Every problem found, blocking ones first.</summary>
    public IReadOnlyList<BuildReadinessIssue> Issues { get; set; } = Array.Empty<BuildReadinessIssue>();

    /// <summary>The Rust install guidance for every problem (host mode), without
    /// repeats.</summary>
    public IReadOnlyList<InstallGuidance> Guidance { get; set; } = Array.Empty<InstallGuidance>();

    /// <summary>The Docker guidance (Docker mode: an unusable engine, or a missing docker CLI
    /// that a target's image layer needs).</summary>
    public IReadOnlyList<DockerGuidance> DockerGuidance { get; set; } = Array.Empty<DockerGuidance>();

    /// <summary>The requested targets this machine can build in <see cref="Mode"/>, in request
    /// order.</summary>
    public IReadOnlyList<CrateTarget> BuildableTargets { get; set; } = Array.Empty<CrateTarget>();

    /// <summary>The requested targets it cannot build, in request order.</summary>
    public IReadOnlyList<CrateTarget> UnbuildableTargets { get; set; } = Array.Empty<CrateTarget>();

    /// <summary>The detected Rust toolchain (host mode); <c>null</c> in Docker mode.</summary>
    public RustToolchainInfo Toolchain { get; set; }

    /// <summary>The engine check (Docker mode); <c>null</c> in host mode.</summary>
    public DockerEngineInfo Engine { get; set; }

    /// <summary>The install guidance that would make one target buildable (empty when it is
    /// buildable or when no Rust guidance applies).</summary>
    /// <param name="target">A requested target.</param>
    public IReadOnlyList<InstallGuidance> GetGuidance(CrateTarget target)
        => _targetGuidance.TryGetValue(target, out var guidance) ? guidance : Array.Empty<InstallGuidance>();

    /// <summary>"Ready" or "Not ready" with the mode, the buildable and unbuildable runtime
    /// identifiers, then one line per issue and the title of each guidance step.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append(IsReady ? "Ready" : "Not ready").Append(" to build in ").Append(Mode).Append(" mode.");
        builder.AppendLine().Append("  Buildable: ").Append(BuildableTargets.Count == 0 ? "(none)" : string.Join(", ", BuildableTargets.Select(CrateTargets.Rid)));
        builder.AppendLine().Append("  Not buildable: ").Append(UnbuildableTargets.Count == 0 ? "(none)" : string.Join(", ", UnbuildableTargets.Select(CrateTargets.Rid)));
        foreach (var issue in Issues)
        {
            builder.AppendLine().Append("  ").Append(issue);
        }

        foreach (var guidance in Guidance)
        {
            builder.AppendLine().Append("  Guidance: ").Append(guidance.Title);
        }

        foreach (var guidance in DockerGuidance)
        {
            builder.AppendLine().Append("  Docker guidance: ").Append(guidance.Title);
        }

        return builder.ToString();
    }

    internal void SetTargetGuidance(IReadOnlyDictionary<CrateTarget, IReadOnlyList<InstallGuidance>> guidance)
    {
        _targetGuidance = guidance ?? new Dictionary<CrateTarget, IReadOnlyList<InstallGuidance>>();
    }
}
