using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.WebGpu;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// The pipeline's Building stage: picks the driver of the request's <see cref="BuildMode"/>
/// (<see cref="HostBuildDriver"/> or <see cref="DockerBuildDriver"/>) and gives it the build
/// contributions and the verifiers the request needs (<see cref="ContributionsFor"/>,
/// <see cref="VerifiersFor"/>). The drivers verify every artifact ONCE, right after it is
/// built; the Verifying stage then only records what they found.
/// </summary>
internal sealed class ModeBuildDriver : IBuildDriver
{
    /// <summary>The contributions of a request: the <see cref="WebGpuLinkStager"/> when it links
    /// CodeBrix WebGPU, else none.</summary>
    public static IReadOnlyList<IBuildContribution> ContributionsFor(CrateBuildRequest request)
        => request != null && request.LinkToCodeBrixWebGpu
            ? new IBuildContribution[] { new WebGpuLinkStager() }
            : Array.Empty<IBuildContribution>();

    /// <summary>The verifiers of a request: the <see cref="DefaultArtifactVerifier"/>, plus the
    /// <see cref="WebGpuLinkVerifier"/> when it links CodeBrix WebGPU. (The Docker driver adds the
    /// glibc floor check of <see cref="LinuxBaseline.ManyLinux"/> itself.)</summary>
    public static IReadOnlyList<IArtifactVerifier> VerifiersFor(CrateBuildRequest request)
        => request != null && request.LinkToCodeBrixWebGpu
            ? new IArtifactVerifier[] { new DefaultArtifactVerifier(), new WebGpuLinkVerifier() }
            : new IArtifactVerifier[] { new DefaultArtifactVerifier() };

    /// <summary>The driver for a request's mode.</summary>
    public static IBuildDriver DriverFor(CrateBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var contributions = ContributionsFor(request);
        var verifiers = VerifiersFor(request);
        return request.Mode == BuildMode.Docker
            ? new DockerBuildDriver(contributions, verifiers)
            : new HostBuildDriver(contributions, verifiers);
    }

    public Task<IReadOnlyList<CrateTargetResult>> BuildAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CancellationToken cancellationToken)
        => DriverFor(request).BuildAsync(workspace, request, context, progress, cancellationToken);
}
