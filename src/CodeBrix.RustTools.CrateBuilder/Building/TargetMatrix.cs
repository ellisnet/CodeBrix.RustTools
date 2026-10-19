using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Which of a request's targets one build mode can build on this machine (D1), decided
/// BEFORE any cargo runs. It reuses the readiness check (<see cref="ReadinessReportBuilder"/>,
/// <see cref="HostTargetEvaluator"/>, <see cref="DockerTargetEvaluator"/>) and adds the limits of
/// the build drivers themselves: the ManyLinux baseline builds Linux targets only, and only the
/// architectures CrateBuilder has a manylinux image for that are the engine's own. Every
/// unbuildable target becomes a <see cref="CrateTargetOutcome.NotBuildableInThisMode"/> result
/// with the guidance and reason; the others are built.
/// </summary>
internal sealed class TargetMatrix
{
    private TargetMatrix(BuildMode mode, IReadOnlyList<CrateTarget> buildable, IReadOnlyList<CrateTargetResult> notBuildable)
    {
        Mode = mode;
        Buildable = buildable;
        NotBuildable = notBuildable;
    }

    /// <summary>The mode the matrix was made for.</summary>
    public BuildMode Mode { get; }

    /// <summary>The targets to build, in request order.</summary>
    public IReadOnlyList<CrateTarget> Buildable { get; }

    /// <summary>One <see cref="CrateTargetOutcome.NotBuildableInThisMode"/> result per target
    /// that is not built, in request order.</summary>
    public IReadOnlyList<CrateTargetResult> NotBuildable { get; }

    /// <summary>Whether a target is built.</summary>
    public bool IsBuildable(CrateTarget target) => Buildable.Contains(target);

    /// <summary>
    /// The matrix of a request from a readiness report (the pipeline's, or one made by
    /// <see cref="ForHost"/> / <see cref="ForDocker"/>).
    /// </summary>
    /// <param name="request">The request (its Mode, Targets and LinuxBaseline are
    /// used).</param>
    /// <param name="readiness">The readiness report of the same mode; <c>null</c> = every
    /// target is ready as far as readiness goes (only the driver limits apply).</param>
    public static TargetMatrix Create(CrateBuildRequest request, CrateReadinessReport readiness)
    {
        ArgumentNullException.ThrowIfNull(request);
        var buildable = new List<CrateTarget>();
        var notBuildable = new List<CrateTargetResult>();
        foreach (var target in request.Targets ?? new List<CrateTarget>())
        {
            if (readiness != null && (readiness.UnbuildableTargets.Contains(target) || !readiness.IsReady))
            {
                var reasons = readiness.Issues.Where(i => i.Target == target || (i.Target == null && i.IsBlocking)).Select(i => i.Message).ToList();
                notBuildable.Add(NotBuildableResult(request.Mode, target, reasons.Count > 0 ? string.Join(" ", reasons) : $"Not buildable in {request.Mode} mode on this machine.", readiness.GetGuidance(target)));
                continue;
            }

            var limit = DriverLimit(request, target, readiness?.Engine);
            if (limit != null)
            {
                notBuildable.Add(NotBuildableResult(request.Mode, target, limit, Array.Empty<InstallGuidance>()));
                continue;
            }

            buildable.Add(target);
        }

        return new TargetMatrix(request.Mode, buildable, notBuildable);
    }

    /// <summary>The host-mode matrix from a detected toolchain.</summary>
    public static TargetMatrix ForHost(CrateBuildRequest request, RustToolchainInfo toolchain, HostEvaluationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Create(request, ReadinessReportBuilder.ForHost(request.Targets.ToList(), toolchain, settings));
    }

    /// <summary>The Docker-mode matrix from an engine check.</summary>
    public static TargetMatrix ForDocker(CrateBuildRequest request, DockerEngineInfo engine)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Create(request, ReadinessReportBuilder.ForDocker(request.Targets.ToList(), engine, request.LinuxBaseline));
    }

    /// <summary>
    /// The reason the build driver of the request's mode cannot build a target although
    /// readiness allows it, or <c>null</c>. Host mode has none. Docker mode with
    /// <see cref="LinuxBaseline.ManyLinux"/>: non-Linux targets, Linux architectures without a
    /// manylinux image, and an image architecture that is not the engine's own (the image
    /// would run under emulation, which the Docker package cannot request).
    /// </summary>
    public static string DriverLimit(CrateBuildRequest request, CrateTarget target, DockerEngineInfo engine)
        => request.Mode != BuildMode.Docker ? null : ManyLinuxLimit(request.LinuxBaseline, target, engine);

    /// <summary>Why <see cref="LinuxBaseline.ManyLinux"/> cannot build <paramref name="target"/>
    /// on <paramref name="engine"/>, or <c>null</c> when it can (or the baseline is the rust
    /// image). The readiness check uses it too, so it reports the same buildable set the Docker
    /// driver builds.</summary>
    public static string ManyLinuxLimit(LinuxBaseline baseline, CrateTarget target, DockerEngineInfo engine)
    {
        if (baseline != LinuxBaseline.ManyLinux)
        {
            return null;
        }

        if (!CrateTargets.IsLinux(target))
        {
            return ManyLinuxImages.NonLinuxReason;
        }

        if (!ManyLinuxImages.HasImage(target))
        {
            return ManyLinuxImages.ForeignArchitectureReason;
        }

        if (engine != null && !CrossTargetInfo.Get(CrateTargets.ToCrossTarget(target)).IsNativeOn(engine))
        {
            return $"The ManyLinux image of {CrateTargets.Rid(target)} is an x86-64 image and this Docker engine is {engine.Architecture ?? "of unknown architecture"}; running it would need a foreign-platform image, which CodeBrix.RustTools.Docker cannot request. Use LinuxBaseline.RustImage.";
        }

        return null;
    }

    private static CrateTargetResult NotBuildableResult(BuildMode mode, CrateTarget target, string message, IReadOnlyList<InstallGuidance> guidance) => new CrateTargetResult
    {
        Provenance = CrateBuildPipeline.DefaultProvenance(target, mode, HostOperatingSystem.Detect()),
        Target = target,
        Rid = CrateTargets.Rid(target),
        Triple = CrateTargets.Triple(target),
        Outcome = CrateTargetOutcome.NotBuildableInThisMode,
        Message = message,
        Guidance = guidance ?? Array.Empty<InstallGuidance>(),
    };
}
