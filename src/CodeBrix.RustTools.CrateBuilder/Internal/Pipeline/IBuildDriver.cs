using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.Building"/>: runs cargo for each target of the request
/// (host mode through CodeBrix.RustTools, Docker mode through CodeBrix.RustTools.Docker) and
/// copies the native files into the request's output folder in its layout. The request it
/// receives holds only the targets the readiness check found buildable; the pipeline adds the
/// others as <see cref="CrateTargetOutcome.NotBuildableInThisMode"/>. Implemented by WP3 (and
/// WP4 for the WebGPU link). A failed target is a result, not an exception.
/// </summary>
internal interface IBuildDriver
{
    /// <summary>Builds every target of <paramref name="request"/> and returns one result per
    /// target.</summary>
    Task<IReadOnlyList<CrateTargetResult>> BuildAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CancellationToken cancellationToken);
}
