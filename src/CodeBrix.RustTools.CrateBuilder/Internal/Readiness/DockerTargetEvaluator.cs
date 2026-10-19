using System;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// Decides, from an engine check, whether a Docker-mode build can produce a target. The
/// Docker package builds every target through its image layers, with two limits it states
/// itself: Android needs an x86-64 engine (the NDK's tools are x86-64 Linux programs), and any
/// derived image - a target that is not the engine's own Linux architecture, or a manylinux
/// Containerfile - is built through the docker CLI.
/// </summary>
internal static class DockerTargetEvaluator
{
    public static TargetEvaluation Evaluate(CrateTarget target, DockerEngineInfo engine, LinuxBaseline baseline)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var evaluation = new TargetEvaluation { Target = target, IsBuildable = true };
        var info = CrossTargetInfo.Get(CrateTargets.ToCrossTarget(target));
        if (info.IsAndroid && !IsX64(engine.Architecture))
        {
            evaluation.IsBuildable = false;
            evaluation.Issues.Add(new BuildReadinessIssue
            {
                Code = "docker.android-needs-x64-engine",
                Message = $"Android targets need an x86-64 Docker engine (the Android NDK's compilers run on x86-64 Linux only); this engine is {engine.Architecture ?? "of unknown architecture"}.",
                Target = target,
            });
        }

        var manyLinux = Building.TargetMatrix.ManyLinuxLimit(baseline, target, engine);
        if (manyLinux != null)
        {
            evaluation.IsBuildable = false;
            evaluation.Issues.Add(new BuildReadinessIssue
            {
                Code = "docker.manylinux-limit",
                Message = manyLinux,
                Target = target,
            });
            return evaluation;
        }

        if (NeedsDerivedImage(target, engine, baseline) && !engine.DockerCliFound)
        {
            evaluation.IsBuildable = false;
            evaluation.Issues.Add(new BuildReadinessIssue
            {
                Code = "docker.cli-missing",
                Message = $"{CrateTargets.Rid(target)} needs a derived build image, which is built through the docker command-line tool (with its buildx plugin); it was not found on PATH.",
                Target = target,
            });
        }

        return evaluation;
    }

    /// <summary>Whether the target's build image is more than the plain base image.</summary>
    public static bool NeedsDerivedImage(CrateTarget target, DockerEngineInfo engine, LinuxBaseline baseline)
    {
        var info = CrossTargetInfo.Get(CrateTargets.ToCrossTarget(target));
        if (info.IsLinux)
        {
            return baseline == LinuxBaseline.ManyLinux || !info.IsNativeOn(engine);
        }

        return true;
    }

    private static bool IsX64(string architecture)
        => string.Equals(architecture, "x86_64", StringComparison.OrdinalIgnoreCase)
           || string.Equals(architecture, "amd64", StringComparison.OrdinalIgnoreCase);
}
