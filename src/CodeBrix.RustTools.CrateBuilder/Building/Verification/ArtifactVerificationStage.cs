using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using BuildingVerifier = CodeBrix.RustTools.CrateBuilder.Building.IArtifactVerifier;
using StageVerifier = CodeBrix.RustTools.CrateBuilder.Internal.Pipeline.IArtifactVerifier;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// The pipeline's Verifying stage built from per-artifact verifiers: runs them over every
/// compiled target's files (see <see cref="ArtifactVerification"/>). For a pipeline whose
/// build drivers already run the same verifiers, construct the drivers with an empty
/// verifier list or this stage with one, so files are not verified twice.
/// </summary>
internal sealed class ArtifactVerificationStage : StageVerifier
{
    private readonly IReadOnlyList<BuildingVerifier> _verifiers;

    /// <summary>A stage that runs <see cref="DefaultArtifactVerifier"/>.</summary>
    public ArtifactVerificationStage()
        : this(new BuildingVerifier[] { new DefaultArtifactVerifier() })
    {
    }

    /// <summary>A stage that runs <paramref name="verifiers"/> (may be empty).</summary>
    public ArtifactVerificationStage(IReadOnlyList<BuildingVerifier> verifiers)
    {
        _verifiers = verifiers ?? Array.Empty<BuildingVerifier>();
    }

    public Task VerifyAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        foreach (var target in compiledTargets ?? Array.Empty<CrateTargetResult>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArtifactVerification.Apply(target, _verifiers, workspace);
        }

        return Task.CompletedTask;
    }
}
