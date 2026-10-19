using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Builds a staged crate inside Docker (CodeBrix.RustTools.Docker's
/// <see cref="DockerRustWorkbench"/> from the context): ONE build container for all buildable
/// targets - the whole staging root copied into <c>/work</c> (<see cref="CopyRootFor"/>), cargo
/// run per target in the built manifest's folder below it with <c>-p &lt;crate&gt;</c>, the
/// shared libraries retrieved in the request's layout, the Docker package's default network
/// (crates download unless Offline) - in the image the request's <see cref="LinuxBaseline"/>
/// selects:
/// <list type="number">
/// <item><see cref="LinuxBaseline.RustImage"/>: the workbench's default image plus the
/// cross layers the Docker package adds for the targets;</item>
/// <item><see cref="LinuxBaseline.ManyLinux"/>: CrateBuilder's manylinux Containerfile
/// (<see cref="ManyLinuxImages"/>; linux-x64 only, see its remarks), and the built library is
/// checked against the image's glibc floor.</item>
/// </list>
/// Each target's cargo run gets its own contribution environment (the Docker package takes the
/// options per run); a contribution's rustflags go into the target's
/// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c>, which the Docker package APPENDS to its own
/// per-target flags (Windows <c>+crt-static</c>, the macOS link profile), so all targets share
/// one container. The container links Android for
/// <see cref="HostBuildDriver.AndroidApiLevelFor"/> (33 when WebGPU-linked, as in host mode).
/// macOS files from Docker are always development builds.
/// </summary>
internal sealed class DockerBuildDriver : IBuildDriver
{
    private readonly IReadOnlyList<IBuildContribution> _contributions;
    private readonly IReadOnlyList<IArtifactVerifier> _verifiers;

    /// <summary>A driver with no contributions and the <see cref="DefaultArtifactVerifier"/>.</summary>
    public DockerBuildDriver()
        : this(Array.Empty<IBuildContribution>(), new IArtifactVerifier[] { new DefaultArtifactVerifier() })
    {
    }

    /// <summary>A driver with the given contributions and verifiers (either may be
    /// empty).</summary>
    public DockerBuildDriver(IReadOnlyList<IBuildContribution> contributions, IReadOnlyList<IArtifactVerifier> verifiers)
    {
        _contributions = contributions ?? Array.Empty<IBuildContribution>();
        _verifiers = verifiers ?? Array.Empty<IArtifactVerifier>();
    }

    /// <summary>What <see cref="BuildContribution.WorkspaceToken"/> stands for in Docker mode: the
    /// container folder the staging root is copied to (<c>/work</c>).</summary>
    public const string WorkspaceTokenValue = BuildContainer.WorkDirectory;

    /// <summary>The host folder copied into the container's <c>/work</c>: the staging root
    /// (<see cref="StagedWorkspace.Directory"/>, which holds the copied workspace, the vendored
    /// path dependencies, a generated facade and the WebGPU link folder) when the built
    /// manifest lies inside it, else the built manifest's folder.</summary>
    public static string CopyRootFor(StagedWorkspace workspace, BuiltCrate crate)
    {
        var manifestDirectory = Path.GetDirectoryName(crate.ManifestPath);
        if (string.IsNullOrWhiteSpace(workspace.Directory))
        {
            return manifestDirectory;
        }

        var root = Path.GetFullPath(workspace.Directory);
        var relative = Path.GetRelativePath(root, manifestDirectory);
        return relative == "." || (!relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            ? root
            : manifestDirectory;
    }

    /// <summary>The ABSOLUTE container folder cargo runs in: <c>/work/&lt;path of the built
    /// manifest's folder below the copied <paramref name="copyRoot"/>&gt;</c>, with forward
    /// slashes; <c>null</c> = <c>/work</c> itself. (Absolute on purpose: the Docker package
    /// takes an absolute working directory as it is.)</summary>
    public static string ContainerWorkingDirectoryFor(string copyRoot, string manifestPath)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(copyRoot), Path.GetDirectoryName(Path.GetFullPath(manifestPath))).Replace('\\', '/');
        return relative == "." || relative.Length == 0 ? null : BuildContainer.WorkDirectory + "/" + relative.Trim('/');
    }

    /// <summary>
    /// Makes <paramref name="options"/> (a fresh object per target) the cargo options of one
    /// target's run: the target's contribution environment and, when the contribution has
    /// rustflags, the target's <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c> set to them (the
    /// Docker package appends them to its own flags for the target). Returns
    /// <paramref name="options"/>.
    /// </summary>
    public static CargoBuildOptions TargetOptions(CargoBuildOptions options, BuildContribution contribution, CrateTarget target)
    {
        var copy = options;
        if (contribution == null)
        {
            return copy;
        }

        foreach (var pair in contribution.Environment)
        {
            copy.Environment[pair.Key] = pair.Value;
        }

        if (contribution.RustFlags.Count > 0)
        {
            var variable = CargoOptionsFactory.RustFlagsVariable(CrateTargets.Triple(target));
            copy.Environment[variable] = ContributionMerger.AppendFlags(null, contribution.RustFlags);
        }

        return copy;
    }

    /// <summary>The build container's settings for a request: Android linked at
    /// <see cref="HostBuildDriver.AndroidApiLevelFor"/>, the same level host mode uses.</summary>
    public static BuildContainerOptions ContainerOptionsFor(CrateBuildRequest request)
        => new BuildContainerOptions { AndroidApiLevel = HostBuildDriver.AndroidApiLevelFor(request) };

    /// <summary>"Docker image R (id I, base B) on Docker engine V (arch)".</summary>
    public static string DescribeImage(RustBuildImage image, DockerEngineInfo engine)
    {
        if (image == null)
        {
            return engine == null ? null : $"Docker engine {engine.ServerVersion ?? "unknown"} ({engine.Architecture ?? "unknown"})";
        }

        var origin = image.ContainerfileImageReference != null
            ? $"from Containerfile image {image.ContainerfileImageReference} ({ManyLinuxImages.X64BaseImage}, Rust {ManyLinuxImages.RustVersion})"
            : "base " + image.BaseImageReference;
        return $"Docker image {image.Reference} (id {image.ImageId}, {origin}) on Docker engine {engine?.ServerVersion ?? "unknown"} ({engine?.Architecture ?? "unknown"})";
    }

    public async Task<IReadOnlyList<CrateTargetResult>> BuildAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var crate = BuiltCrate.From(workspace);
        var workbench = context.DockerWorkbench;
        var readiness = context.Readiness?.Mode == BuildMode.Docker && context.Readiness.Engine != null
            ? context.Readiness
            : ReadinessReportBuilder.ForDocker(request.Targets.ToList(), await workbench.CheckEngineAsync(cancellationToken).ConfigureAwait(false), request.LinuxBaseline);
        var engine = readiness.Engine;
        var matrix = TargetMatrix.Create(request, readiness);
        var built = new Dictionary<CrateTarget, CrateTargetResult>();
        var descriptions = new List<string>();

        var prepared = new Dictionary<CrateTarget, BuildContribution>();
        foreach (var target in matrix.Buildable)
        {
            prepared[target] = await ContributionMerger.PrepareAsync(_contributions, workspace, target, BuildMode.Docker, context, WorkspaceTokenValue, cancellationToken).ConfigureAwait(false);
        }

        var targets = matrix.Buildable.ToList();
        if (targets.Count > 0)
        {
            try
            {
                var (results, description) = await BuildAllAsync(workbench, engine, workspace, crate, request, context, targets, prepared, progress, cancellationToken).ConfigureAwait(false);
                foreach (var result in results)
                {
                    built[result.Target] = result;
                }

                if (description != null)
                {
                    descriptions.Add(description);
                }
            }
            catch (Exception ex) when (ex is DockerRustToolsException or RustToolsException or IOException or ArgumentException)
            {
                foreach (var target in targets)
                {
                    built[target] = new CrateTargetResult
                    {
                        Target = target,
                        Rid = CrateTargets.Rid(target),
                        Triple = CrateTargets.Triple(target),
                        Outcome = CrateTargetOutcome.Failed,
                        Provenance = ProvenanceOf(target),
                        Message = "The Docker build failed: " + ex.Message,
                    };
                }
            }
        }

        context.ToolchainDescription = descriptions.Count > 0 ? string.Join("; ", descriptions.Distinct()) : DescribeImage(null, engine);
        var ordered = new List<CrateTargetResult>();
        foreach (var target in request.Targets)
        {
            var ruledOut = matrix.NotBuildable.FirstOrDefault(r => r.Target == target);
            ordered.Add(ruledOut ?? built[target]);
        }

        return ordered;
    }

    private static ArtifactProvenance ProvenanceOf(CrateTarget target)
        => CrateTargets.IsMacOS(target) ? ArtifactProvenance.DevelopmentBuild : ArtifactProvenance.Distributable;

    private async Task<(IReadOnlyList<CrateTargetResult> Results, string Description)> BuildAllAsync(
        DockerRustWorkbench workbench,
        DockerEngineInfo engine,
        StagedWorkspace workspace,
        BuiltCrate crate,
        CrateBuildRequest request,
        CrateBuilderContext context,
        IReadOnlyList<CrateTarget> targets,
        IDictionary<CrateTarget, BuildContribution> prepared,
        IProgress<CrateBuildProgress> progress,
        CancellationToken cancellationToken)
    {
        var installName = CargoOptionsFactory.MacOSInstallName(crate);

        // The whole staging root goes into /work (so vendored path dependencies, the facade's
        // upstream crate and the WebGPU link folder come along, and the "{workspace}" token -
        // /work - names the same folder as on the host); cargo runs in the built manifest's
        // folder below it.
        var copyRoot = CopyRootFor(workspace, crate);
        CargoBuildOptions OptionsFor(CrateTarget target)
        {
            var options = CargoOptionsFactory.Create(request, crate, workspace.IsFacade);
            CargoOptionsFactory.AddUpstreamConfig(options, workspace);
            options.WorkingDirectory = ContainerWorkingDirectoryFor(copyRoot, crate.ManifestPath);
            if (CrateTargets.IsMacOS(target))
            {
                options.MacOSInstallName = installName;
            }

            return TargetOptions(options, prepared[target], target);
        }

        var manyLinux = request.LinuxBaseline == LinuxBaseline.ManyLinux;
        BuildImageSpec imageSpec;
        if (manyLinux)
        {
            var scratch = string.IsNullOrWhiteSpace(context.RunDirectory) ? context.ScratchRootDirectory : context.RunDirectory;
            imageSpec = ManyLinuxImages.CreateSpec(CrateTarget.LinuxX64, Path.Combine(scratch, "manylinux-x86_64"));
        }
        else
        {
            imageSpec = new BuildImageSpec();
        }

        var crossTargets = targets.Select(CrateTargets.ToCrossTarget).ToList();
        foreach (var crossTarget in crossTargets.Where(t => CrossTargetInfo.Get(t).RequiresLayer && !imageSpec.CrossTargets.Contains(t)))
        {
            imageSpec.CrossTargets.Add(crossTarget);
        }

        var retrievalOptions = new ArtifactRetrievalOptions
        {
            HostDirectory = Path.GetFullPath(request.OutputDirectory),
            Layout = request.Layout,
            IncludeExecutables = false,
            IncludeSharedLibraries = true,
            IncludeStaticLibraries = false,
            IncludeRlibs = false,
            Overwrite = true,
        };

        Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = $"Building {string.Join(", ", targets.Select(CrateTargets.Rid))} in Docker ({request.LinuxBaseline})." });
        var imageProgress = new SynchronousProgress<string>(line => Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = line }));
        var image = await workbench.EnsureImageAsync(imageSpec, imageProgress, cancellationToken).ConfigureAwait(false);
        var description = DescribeImage(image, engine);

        var verifiers = _verifiers.ToList();
        if (manyLinux)
        {
            verifiers.Add(new GlibcFloorVerifier(ManyLinuxImages.X64GlibcFloor));
        }

        var results = new List<CrateTargetResult>();
        var container = await workbench.StartContainerAsync(image, ContainerOptionsFor(request), cancellationToken).ConfigureAwait(false);
        try
        {
            Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = $"Copying {copyRoot} into the build container." });
            await workbench.CopySourceAsync(container, copyRoot, null, null, cancellationToken).ConfigureAwait(false);
            foreach (var target in targets)
            {
                var crossTarget = CrateTargets.ToCrossTarget(target);
                var result = new CrateTargetResult
                {
                    Target = target,
                    Rid = CrateTargets.Rid(target),
                    Triple = CrateTargets.Triple(target),
                    Outcome = CrateTargetOutcome.Failed,
                    Provenance = ProvenanceOf(target),
                };
                results.Add(result);
                Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = $"Building {result.Rid} ({result.Triple}) in Docker." });
                var cargoProgress = new SynchronousProgress<CargoProgress>(p => Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = p?.Message, CargoProgress = p }));
                CargoCommandResult cargo;
                try
                {
                    cargo = await workbench.RunCargoAsync(container, CargoCommand.Build, OptionsFor(target), crossTarget, cargoProgress, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is DockerRustToolsException or RustToolsException or ArgumentException)
                {
                    result.Message = "cargo could not run in the build container: " + ex.Message;
                    continue;
                }

                result.CargoResult = cargo;
                if (!cargo.Success)
                {
                    result.Message = CargoResultSummary.Describe(cargo);
                    continue;
                }

                var retrieval = await workbench.RetrieveArtifactsAsync(container, cargo.Artifacts, retrievalOptions, crossTarget, cancellationToken).ConfigureAwait(false);
                var ours = new List<string>();
                foreach (var artifact in (retrieval?.Artifacts ?? Array.Empty<RetrievedArtifact>()).Where(a => a.Kind == ArtifactKind.SharedLibrary))
                {
                    if (string.Equals(artifact.PackageName, crate.PackageName, StringComparison.Ordinal))
                    {
                        ours.Add(artifact.HostPath);
                    }
                    else if (File.Exists(artifact.HostPath))
                    {
                        File.Delete(artifact.HostPath);
                    }
                }

                if (ours.Count == 0)
                {
                    result.Message = $"cargo built {crate.PackageName} for {result.Triple} but no dynamic library was retrieved; the crate needs crate-type = [\"cdylib\"] in its [lib] section.";
                    continue;
                }

                result.ArtifactPaths = ours;
                result.Outcome = CrateTargetOutcome.Compiled;
                var notes = prepared[target].Notes.ToList();
                notes.Add("Built in " + description + ".");
                result.VerificationNotes = notes;
                ArtifactVerification.Apply(result, verifiers, workspace);
            }
        }
        finally
        {
            if (!container.KeepOnDispose)
            {
                try
                {
                    await container.StopAndRemoveAsync(true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    context.Warnings.Add($"The build container {container} could not be removed: {ex.Message}");
                }
            }
        }

        return (results, description);
    }

    private static void Report(CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CrateBuildProgress value)
    {
        if (progress != null)
        {
            progress.Report(value);
        }
        else
        {
            context.Report(value);
        }
    }
}
