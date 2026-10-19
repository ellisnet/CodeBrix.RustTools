using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.Verifying"/>: checks every compiled target's files
/// (architecture, exports, linked libraries, the WebGPU link) and records what it found in
/// <see cref="CrateTargetResult.VerificationNotes"/>; a target whose files are wrong becomes
/// <see cref="CrateTargetOutcome.Failed"/>, or the verifier throws
/// <see cref="ArtifactVerificationException"/>. Implemented by WP3 (and WP4). An addition to
/// the five seams of the WP1 brief, so that the stage has its own door.
/// </summary>
internal interface IArtifactVerifier
{
    /// <summary>Verifies the compiled targets (it may update the results in place).</summary>
    Task VerifyAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken);
}
