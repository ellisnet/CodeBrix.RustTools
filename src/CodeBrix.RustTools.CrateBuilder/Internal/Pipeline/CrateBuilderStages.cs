using System;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Facade;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Wrapper;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// The implementation behind each stage. <see cref="CreateDefault"/> wires the production
/// stages; tests pass fakes through the internal <see cref="CrateBuilder"/> constructor.
/// </summary>
internal sealed class CrateBuilderStages
{
    public IReadinessChecker Readiness { get; set; }

    public ISourceResolver SourceResolver { get; set; }

    public IWorkspaceStager WorkspaceStager { get; set; }

    public IFacadeGenerator FacadeGenerator { get; set; }

    public IBuildDriver BuildDriver { get; set; }

    public IArtifactVerifier ArtifactVerifier { get; set; }

    public IWrapperWriter WrapperWriter { get; set; }

    /// <summary>The production stages: readiness, source resolution (local or Git), staging,
    /// the facade stages (API extraction, planning, rendering), the build driver of the
    /// request's mode with its contributions and verifiers (the drivers verify every artifact
    /// once), the verification recorder and the wrapper writer.</summary>
    public static CrateBuilderStages CreateDefault() => new CrateBuilderStages
    {
        Readiness = new ReadinessChecker(),
        SourceResolver = new SourceResolver(),
        WorkspaceStager = new WorkspaceStager(),
        FacadeGenerator = new FacadeGenerator(),
        BuildDriver = new ModeBuildDriver(),
        ArtifactVerifier = new VerificationRecorder(),
        WrapperWriter = new WrapperProjectWriter(),
    };

    /// <summary>Throws when a stage is missing.</summary>
    public void EnsureComplete()
    {
        if (Readiness == null || SourceResolver == null || WorkspaceStager == null || FacadeGenerator == null
            || BuildDriver == null || ArtifactVerifier == null || WrapperWriter == null)
        {
            throw new InvalidOperationException("Every CrateBuilder stage needs an implementation.");
        }
    }
}
