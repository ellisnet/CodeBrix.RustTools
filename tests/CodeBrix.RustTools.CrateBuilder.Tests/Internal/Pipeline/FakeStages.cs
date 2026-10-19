using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Internal.Pipeline;

/// <summary>
/// Recording fakes for every pipeline seam. Each one appends its stage name to
/// <see cref="Calls"/>; <see cref="FailAt"/> makes one of them throw <see cref="Failure"/>.
/// </summary>
internal sealed class FakeStages : IReadinessChecker, ISourceResolver, IWorkspaceStager, IFacadeGenerator, IBuildDriver, IArtifactVerifier, IWrapperWriter
{
    public List<string> Calls { get; } = new List<string>();

    public string FailAt { get; set; }

    public Exception Failure { get; set; } = new InvalidOperationException("the fake stage failed");

    public IReadOnlyList<CrateTarget> Unbuildable { get; set; } = Array.Empty<CrateTarget>();

    public bool ReadinessBlocks { get; set; }

    public IReadOnlyList<string> CrateTypes { get; set; } = new[] { "cdylib" };

    public bool SourceCleanedUp { get; private set; }

    public string StagedRunDirectory { get; private set; }

    public List<CrateTarget> TargetsBuilt { get; } = new List<CrateTarget>();

    public Func<CancellationToken, Task> DuringBuild { get; set; }

    /// <summary>When true, a build of the crate itself (not a facade) fails verification the way
    /// the default verifier rejects a library that exports nothing.</summary>
    public bool CrateExportsNothing { get; set; }

    public CrateBuilderStages ToStages() => new CrateBuilderStages
    {
        Readiness = this,
        SourceResolver = this,
        WorkspaceStager = this,
        FacadeGenerator = this,
        BuildDriver = this,
        ArtifactVerifier = this,
        WrapperWriter = this,
    };

    public Task<CrateReadinessReport> CheckAsync(CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("Readiness");
        var issues = new List<BuildReadinessIssue>();
        if (ReadinessBlocks)
        {
            issues.Add(new BuildReadinessIssue { Code = "toolchain.tool-missing", Message = "Cargo is not installed.", IsBlocking = true });
        }

        issues.AddRange(Unbuildable.Select(t => new BuildReadinessIssue { Code = "target.target-missing", Message = "std missing for " + t, Target = t }));
        return Task.FromResult(new CrateReadinessReport
        {
            Mode = request.Mode,
            IsReady = !ReadinessBlocks,
            Issues = issues,
            BuildableTargets = ReadinessBlocks ? new List<CrateTarget>() : request.Targets.Where(t => !Unbuildable.Contains(t)).ToList(),
            UnbuildableTargets = ReadinessBlocks ? request.Targets.ToList() : request.Targets.Where(t => Unbuildable.Contains(t)).ToList(),
        });
    }

    public Task<ResolvedSource> ResolveAsync(CrateSource source, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("ResolvingSource");
        return Task.FromResult(new ResolvedSource
        {
            RepositoryDirectory = "/src/fake",
            ManifestPath = "/src/fake/Cargo.toml",
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind, ManifestPath = "/src/fake/Cargo.toml" },
            CleanUp = () => SourceCleanedUp = true,
        });
    }

    public Task<StagedWorkspace> StageAsync(ResolvedSource source, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("Staging");
        StagedRunDirectory = context.RunDirectory;
        var directory = Path.Combine(context.RunDirectory, "staged");
        Directory.CreateDirectory(directory);
        return Task.FromResult(new StagedWorkspace
        {
            Directory = directory,
            ManifestPath = Path.Combine(directory, "Cargo.toml"),
            UpstreamManifestPath = Path.Combine(directory, "Cargo.toml"),
            CrateName = "fake-crate",
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind, CrateName = "fake-crate", CrateVersion = "0.1.0", CrateTypes = CrateTypes, SourceHash = new string('c', 64) },
        });
    }

    public Task<FacadeGenerationResult> GenerateAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("Facade");
        var directory = Path.Combine(context.RunDirectory, "facade");
        return Task.FromResult(new FacadeGenerationResult { Generated = true, FacadeDirectory = directory, CrateName = "fake-crate-facade", LibName = "fake_crate_facade" });
    }

    public async Task<IReadOnlyList<CrateTargetResult>> BuildAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CancellationToken cancellationToken)
    {
        Enter("Building");
        if (DuringBuild != null)
        {
            await DuringBuild(cancellationToken);
        }

        var results = new List<CrateTargetResult>();
        foreach (var target in request.Targets)
        {
            TargetsBuilt.Add(target);
            var path = Path.Combine(request.OutputDirectory, "runtimes", CrateTargets.Rid(target), "native", CrateTargets.NativeFileName(target, workspace.CrateName));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "binary of " + target);
            progress.Report(new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = "built", Target = target });
            results.Add(CrateExportsNothing && !workspace.IsFacade
                ? new CrateTargetResult { Target = target, Outcome = CrateTargetOutcome.Failed, ArtifactPaths = new[] { path }, Message = "Artifact verification failed: " + Path.GetFileName(path) + ": " + DefaultArtifactVerifier.NoExportsNote + ", so .NET could not call into it." }
                : new CrateTargetResult { Target = target, Outcome = CrateTargetOutcome.Compiled, ArtifactPaths = new[] { path } });
        }

        return results;
    }

    public Task VerifyAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("Verifying");
        foreach (var target in compiledTargets)
        {
            target.VerificationNotes = new[] { "fake verification" };
        }

        return Task.CompletedTask;
    }

    public Task<WrapperResult> WriteAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, FacadeGenerationResult facade, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        Enter("Wrapping");
        return Task.FromResult(new WrapperResult { ProjectPath = "/out/Wrapper/Wrapper.csproj", ProjectDirectory = "/out/Wrapper", EntryClassName = "FakeEntry" });
    }

    private void Enter(string stage)
    {
        Calls.Add(stage);
        if (FailAt == stage)
        {
            throw Failure;
        }
    }
}
