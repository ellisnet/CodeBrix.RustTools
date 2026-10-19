using System;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// Decides, from an engine check, whether a Docker-mode build can produce a target. The
/// Docker package builds every target through its image layers, with two limits it states
/// itself: Android needs an x86-64 engine (the NDK's tools are x86-64 Linux programs), and any
/// derived image - a target that is not the engine's own Linux architecture, or a manylinux
/// Containerfile - is built through the docker CLI. A manylinux image of another architecture
/// than the engine's gets a non-blocking warning (<c>docker.manylinux-emulated</c>): it runs
/// under qemu-user emulation, which the build probes for before it builds.
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

        if (baseline == LinuxBaseline.ManyLinux && !engine.IsNativePlatform(Building.ManyLinux.ManyLinuxImages.PlatformOf(target)))
        {
            var platform = Building.ManyLinux.ManyLinuxImages.PlatformOf(target);
            var machine = DockerPlatforms.MachineOf(platform);
            evaluation.Issues.Add(new BuildReadinessIssue
            {
                Code = "docker.manylinux-emulated",
                Message = $"The ManyLinux image of {CrateTargets.Rid(target)} is a {platform} image and this engine is {engine.Architecture ?? "of unknown architecture"}: it is built and run under qemu-user emulation, "
                          + $"which needs a qemu-user binfmt handler for {machine} on the engine's host (/proc/sys/fs/binfmt_misc/qemu-{machine}; the build probes for it first) and is many times slower than a native build.",
                Target = target,
            });
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
