using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// Assembles a <see cref="CrateReadinessReport"/> from detection results (no I/O beyond what
/// the evaluators' settings do), so the mapping can be tested with constructed inputs.
/// </summary>
internal static class ReadinessReportBuilder
{
    public static CrateReadinessReport ForHost(IReadOnlyList<CrateTarget> targets, RustToolchainInfo toolchain, HostEvaluationSettings settings)
    {
        var issues = new List<BuildReadinessIssue>();
        var guidance = new List<InstallGuidance>();
        var perTarget = new Dictionary<CrateTarget, IReadOnlyList<InstallGuidance>>();
        var buildable = new List<CrateTarget>();
        var unbuildable = new List<CrateTarget>();

        var baseReadiness = ToolchainReadiness.Evaluate(toolchain);
        issues.AddRange(baseReadiness.Issues.Select(i => HostTargetEvaluator.FromToolchainIssue(i, "toolchain", null)));
        Add(guidance, InstallGuidanceProvider.ForReadiness(baseReadiness));

        if (!baseReadiness.IsReady)
        {
            unbuildable.AddRange(targets);
        }
        else
        {
            foreach (var target in targets)
            {
                var evaluation = HostTargetEvaluator.Evaluate(target, toolchain, settings);
                (evaluation.IsBuildable ? buildable : unbuildable).Add(target);
                issues.AddRange(evaluation.Issues);
                Add(guidance, evaluation.Guidance);
                perTarget[target] = evaluation.Guidance.ToList();
            }
        }

        var report = Finish(BuildMode.Host, issues, buildable, unbuildable);
        report.Guidance = guidance;
        report.Toolchain = toolchain;
        report.SetTargetGuidance(perTarget);
        return report;
    }

    public static CrateReadinessReport ForDocker(IReadOnlyList<CrateTarget> targets, DockerEngineInfo engine, LinuxBaseline baseline)
    {
        var issues = new List<BuildReadinessIssue>();
        var buildable = new List<CrateTarget>();
        var unbuildable = new List<CrateTarget>();
        IReadOnlyList<DockerGuidance> dockerGuidance = new List<DockerGuidance>();

        if (engine == null || !engine.IsAvailable)
        {
            issues.Add(new BuildReadinessIssue
            {
                Code = "docker.engine-unavailable",
                Message = "The Docker engine cannot be used" + (string.IsNullOrWhiteSpace(engine?.ErrorMessage) ? "." : ": " + engine.ErrorMessage)
                    + (engine == null || engine.Issues.Count == 0 ? string.Empty : " (" + string.Join(", ", engine.Issues) + ")"),
                IsBlocking = true,
            });
            unbuildable.AddRange(targets);
            dockerGuidance = engine == null ? dockerGuidance : DockerGuidanceProvider.ForEngine(engine);
        }
        else
        {
            foreach (var target in targets)
            {
                var evaluation = DockerTargetEvaluator.Evaluate(target, engine, baseline);
                (evaluation.IsBuildable ? buildable : unbuildable).Add(target);
                issues.AddRange(evaluation.Issues);
            }

            if (issues.Any(i => i.Code == "docker.cli-missing"))
            {
                dockerGuidance = DockerGuidanceProvider.ForIssues(new[] { DockerEngineIssue.DockerCliNotFound }, engine.HostOperatingSystem);
            }
        }

        var report = Finish(BuildMode.Docker, issues, buildable, unbuildable);
        report.Engine = engine;
        report.DockerGuidance = dockerGuidance;
        return report;
    }

    private static CrateReadinessReport Finish(BuildMode mode, List<BuildReadinessIssue> issues, List<CrateTarget> buildable, List<CrateTarget> unbuildable)
    {
        if (buildable.Count == 0 && !issues.Any(i => i.IsBlocking))
        {
            issues.Add(new BuildReadinessIssue
            {
                Code = "targets.none-buildable",
                Message = $"None of the requested targets can be built in {mode} mode on this machine.",
                IsBlocking = true,
            });
        }

        return new CrateReadinessReport
        {
            Mode = mode,
            Issues = issues.OrderByDescending(i => i.IsBlocking).ToList(),
            BuildableTargets = buildable,
            UnbuildableTargets = unbuildable,
            IsReady = buildable.Count > 0 && !issues.Any(i => i.IsBlocking),
        };
    }

    private static void Add(List<InstallGuidance> into, IEnumerable<InstallGuidance> items)
    {
        foreach (var item in items)
        {
            if (!into.Any(g => g.Topic == item.Topic && g.Title == item.Title && g.Commands.SequenceEqual(item.Commands)))
            {
                into.Add(item);
            }
        }
    }
}
