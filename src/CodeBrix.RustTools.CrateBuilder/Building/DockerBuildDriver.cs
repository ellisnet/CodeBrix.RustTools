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
/// <see cref="DockerRustWorkbench"/> from the context): the whole staging root copied into
/// <c>/work</c> (<see cref="CopyRootFor"/>), cargo run per target in the built manifest's folder
/// below it with <c>-p &lt;crate&gt;</c>, the shared libraries retrieved in the request's layout,
/// the Docker package's default network (crates download unless Offline) - in the image the
/// request's <see cref="LinuxBaseline"/> selects:
/// <list type="number">
/// <item><see cref="LinuxBaseline.RustImage"/>: ONE build container for all buildable targets,
/// from the workbench's default image plus the cross layers the Docker package adds for the
/// targets;</item>
/// <item><see cref="LinuxBaseline.ManyLinux"/>: CrateBuilder's manylinux Containerfile of each
/// Linux target (<see cref="ManyLinuxImages"/>), ONE image and ONE container per platform. A
/// target whose image is not the engine's own architecture (linux-arm64 / linux-riscv64 on an
/// x86-64 engine) runs under qemu-user emulation: the platform is probed once per build
/// (<see cref="DockerRustWorkbench.ProbePlatformAsync"/>), a failed probe makes the target
/// <see cref="CrateTargetOutcome.NotBuildableInThisMode"/> with
/// <see cref="ManyLinuxImages.EmulationUnavailableReason"/>, and a successful one builds it
/// (slowly). Each built library is checked against its own image's glibc floor
/// (<see cref="ManyLinuxImages.GlibcFloor"/>).</item>
/// </list>
/// Each target's cargo run gets its own contribution environment (the Docker package takes the
/// options per run); a contribution's rustflags go into the target's
/// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c>, which the Docker package APPENDS to its own
/// per-target flags (Windows <c>+crt-static</c>, the macOS link profile), so all targets share
/// one container. The container links Android for
/// <see cref="HostBuildDriver.AndroidApiLevelFor"/> (33 when WebGPU-linked, as in host mode).
/// macOS files from Docker are always development builds.
/// With <see cref="CrateBuilderOptions.CargoTargetDirectory"/> its <c>docker/</c> sub-folder
/// (<c>docker/manylinux-&lt;arch&gt;/</c> for a ManyLinux image) is bind-mounted into the
/// container and is cargo's target folder there; after the container's last cargo run its
/// files are handed back to the folder's owner (<see cref="SharedCargoTarget"/>).
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
        => ContainerOptionsFor(request, null);

    /// <summary>The build container's settings for a request, with the shared cargo target
    /// folder <paramref name="sharedTargetDirectory"/> (the <c>docker/</c> sub-folder of
    /// <see cref="CrateBuilderOptions.CargoTargetDirectory"/>; <c>null</c> = none)
    /// bind-mounted at <see cref="SharedCargoTarget.ContainerPath"/>.</summary>
    public static BuildContainerOptions ContainerOptionsFor(CrateBuildRequest request, string sharedTargetDirectory)
        => SharedCargoTarget.Mount(new BuildContainerOptions { AndroidApiLevel = HostBuildDriver.AndroidApiLevelFor(request) }, sharedTargetDirectory);

    /// <summary>"Docker image R (id I, base B[, platform P under emulation]) on Docker engine V
    /// (arch)"; a Containerfile image names its <c>BASE_IMAGE</c> build argument (the pinned
    /// manylinux digest) and the Rust version.</summary>
    public static string DescribeImage(RustBuildImage image, DockerEngineInfo engine)
    {
        if (image == null)
        {
            return engine == null ? null : $"Docker engine {engine.ServerVersion ?? "unknown"} ({engine.Architecture ?? "unknown"})";
        }

        string origin;
        if (image.ContainerfileImageReference != null)
        {
            var arguments = image.Spec?.ContainerfileBuildArgs;
            var baseImage = arguments != null && arguments.TryGetValue("BASE_IMAGE", out var value) ? value : "unknown base";
            var rust = arguments != null && arguments.TryGetValue("RUST_VERSION", out var version) ? version : ManyLinuxImages.RustVersion;
            origin = $"from Containerfile image {image.ContainerfileImageReference} ({baseImage}, Rust {rust})";
        }
        else
        {
            origin = "base " + image.BaseImageReference;
        }

        if (image.Platform != null)
        {
            origin += engine != null && engine.IsNativePlatform(image.Platform)
                ? $", platform {image.Platform}"
                : $", platform {image.Platform} under emulation";
        }

        return $"Docker image {image.Reference} (id {image.ImageId}, {origin}) on Docker engine {engine?.ServerVersion ?? "unknown"} ({engine?.Architecture ?? "unknown"})";
    }

    /// <summary>
    /// One build container's worth of a request: the image it runs, the targets built in it
    /// and the shared cargo target folder mounted into it (<c>null</c> = none).
    /// </summary>
    internal sealed class BuildGroup
    {
        public BuildImageSpec ImageSpec { get; init; }

        public IReadOnlyList<CrateTarget> Targets { get; init; }

        public string SharedTargetDirectory { get; init; }
    }

    /// <summary>
    /// The build groups of a request's buildable targets: one group for
    /// <see cref="LinuxBaseline.RustImage"/> (the default image plus the targets' cross
    /// layers), one per target for <see cref="LinuxBaseline.ManyLinux"/> (its manylinux
    /// Containerfile, written into <c>&lt;scratch&gt;/manylinux-&lt;arch&gt;</c>; a foreign
    /// architecture's spec carries its platform, also on an engine where linux-x64 is the
    /// foreign one).
    /// </summary>
    public static IReadOnlyList<BuildGroup> GroupsFor(CrateBuildRequest request, CrateBuilderContext context, DockerEngineInfo engine, IReadOnlyList<CrateTarget> targets)
    {
        var groups = new List<BuildGroup>();
        if (request.LinuxBaseline == LinuxBaseline.ManyLinux)
        {
            var scratch = string.IsNullOrWhiteSpace(context.RunDirectory) ? context.ScratchRootDirectory : context.RunDirectory;
            foreach (var target in targets)
            {
                var spec = ManyLinuxImages.CreateSpec(target, Path.Combine(scratch, "manylinux-" + ManyLinuxImages.ArchitectureName(target)));
                var platform = ManyLinuxImages.PlatformOf(target);
                if (spec.Platform == null && engine != null && !engine.IsNativePlatform(platform))
                {
                    spec.Platform = platform;
                }

                groups.Add(new BuildGroup { ImageSpec = spec, Targets = new[] { target }, SharedTargetDirectory = SharedCargoTarget.ManyLinuxFolderFor(context.Options, target) });
            }

            return groups;
        }

        var imageSpec = new BuildImageSpec();
        foreach (var crossTarget in targets.Select(CrateTargets.ToCrossTarget).Where(t => CrossTargetInfo.Get(t).RequiresLayer && !imageSpec.CrossTargets.Contains(t)))
        {
            imageSpec.CrossTargets.Add(crossTarget);
        }

        groups.Add(new BuildGroup { ImageSpec = imageSpec, Targets = targets.ToList(), SharedTargetDirectory = SharedCargoTarget.For(context.Options, SharedCargoTarget.DockerFolderName) });
        return groups;
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

        var targets = matrix.Buildable.ToList();
        if (targets.Count > 0)
        {
            try
            {
                targets = await RuleOutUnrunnablePlatformsAsync(workbench, engine, request, context, targets, built, progress, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is DockerRustToolsException or RustToolsException or ArgumentException)
            {
                foreach (var target in targets)
                {
                    built[target] = FailedResult(target, "The Docker build failed: " + ex.Message);
                }

                targets.Clear();
            }
        }

        var prepared = new Dictionary<CrateTarget, BuildContribution>();
        foreach (var target in targets)
        {
            prepared[target] = await ContributionMerger.PrepareAsync(_contributions, workspace, target, BuildMode.Docker, context, WorkspaceTokenValue, cancellationToken).ConfigureAwait(false);
        }

        if (targets.Count > 0)
        {
            foreach (var group in GroupsFor(request, context, engine, targets))
            {
                try
                {
                    var (results, description) = await BuildGroupAsync(workbench, engine, workspace, crate, request, context, group, prepared, progress, cancellationToken).ConfigureAwait(false);
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
                    foreach (var target in group.Targets)
                    {
                        built[target] = FailedResult(target, "The Docker build failed: " + ex.Message);
                    }
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

    /// <summary>
    /// For <see cref="LinuxBaseline.ManyLinux"/>: probes the platform of every target whose
    /// manylinux image is not the engine's own architecture (once per platform; the workbench
    /// caches the answer) and records a <see cref="CrateTargetOutcome.NotBuildableInThisMode"/>
    /// result in <paramref name="built"/> for each one whose platform cannot run. Returns the
    /// targets still to build.
    /// </summary>
    private static async Task<List<CrateTarget>> RuleOutUnrunnablePlatformsAsync(
        DockerRustWorkbench workbench,
        DockerEngineInfo engine,
        CrateBuildRequest request,
        CrateBuilderContext context,
        List<CrateTarget> targets,
        IDictionary<CrateTarget, CrateTargetResult> built,
        IProgress<CrateBuildProgress> progress,
        CancellationToken cancellationToken)
    {
        if (request.LinuxBaseline != LinuxBaseline.ManyLinux)
        {
            return targets;
        }

        var remaining = new List<CrateTarget>();
        foreach (var target in targets)
        {
            var platform = ManyLinuxImages.PlatformOf(target);
            if (engine == null || engine.IsNativePlatform(platform))
            {
                remaining.Add(target);
                continue;
            }

            Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = $"Checking that this Docker engine ({engine.Architecture}) can run {platform} containers (qemu-user emulation) for {CrateTargets.Rid(target)}." });
            var probe = await workbench.ProbePlatformAsync(platform, cancellationToken).ConfigureAwait(false);
            if (probe.CanRun)
            {
                Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = probe.Message + " Building under emulation is many times slower than natively." });
                remaining.Add(target);
                continue;
            }

            built[target] = TargetMatrix.NotBuildableResult(BuildMode.Docker, target, ManyLinuxImages.EmulationUnavailableReason(target, engine, probe), Array.Empty<CodeBrix.RustTools.Toolchain.InstallGuidance>());
        }

        return remaining;
    }

    private static CrateTargetResult FailedResult(CrateTarget target, string message) => new CrateTargetResult
    {
        Target = target,
        Rid = CrateTargets.Rid(target),
        Triple = CrateTargets.Triple(target),
        Outcome = CrateTargetOutcome.Failed,
        Provenance = ProvenanceOf(target),
        Message = message,
    };

    private static ArtifactProvenance ProvenanceOf(CrateTarget target)
        => CrateTargets.IsMacOS(target) ? ArtifactProvenance.DevelopmentBuild : ArtifactProvenance.Distributable;

    private async Task<(IReadOnlyList<CrateTargetResult> Results, string Description)> BuildGroupAsync(
        DockerRustWorkbench workbench,
        DockerEngineInfo engine,
        StagedWorkspace workspace,
        BuiltCrate crate,
        CrateBuildRequest request,
        CrateBuilderContext context,
        BuildGroup group,
        IDictionary<CrateTarget, BuildContribution> prepared,
        IProgress<CrateBuildProgress> progress,
        CancellationToken cancellationToken)
    {
        var installName = CargoOptionsFactory.MacOSInstallName(crate);
        var targets = group.Targets;

        // The whole staging root goes into /work (so vendored path dependencies, the facade's
        // upstream crate and the WebGPU link folder come along, and the "{workspace}" token -
        // /work - names the same folder as on the host); cargo runs in the built manifest's
        // folder below it.
        var copyRoot = CopyRootFor(workspace, crate);
        var sharedTarget = group.SharedTargetDirectory;
        CargoBuildOptions OptionsFor(CrateTarget target)
        {
            var options = CargoOptionsFactory.Create(request, crate, workspace.IsFacade);
            CargoOptionsFactory.AddUpstreamConfig(options, workspace);
            options.WorkingDirectory = ContainerWorkingDirectoryFor(copyRoot, crate.ManifestPath);
            if (CrateTargets.IsMacOS(target))
            {
                options.MacOSInstallName = installName;
            }

            TargetOptions(options, prepared[target], target);
            SharedCargoTarget.ApplyContainer(options, sharedTarget);
            return options;
        }

        var manyLinux = request.LinuxBaseline == LinuxBaseline.ManyLinux;
        var imageSpec = group.ImageSpec;
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

        var where = imageSpec.Platform != null && engine != null && !engine.IsNativePlatform(imageSpec.Platform)
            ? $"Docker ({request.LinuxBaseline}, {imageSpec.Platform} under emulation)"
            : $"Docker ({request.LinuxBaseline})";
        Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = $"Building {string.Join(", ", targets.Select(CrateTargets.Rid))} in {where}." });
        var imageProgress = new SynchronousProgress<string>(line => Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = line }));
        var image = await workbench.EnsureImageAsync(imageSpec, imageProgress, cancellationToken).ConfigureAwait(false);
        var description = DescribeImage(image, engine);

        var verifiers = _verifiers.ToList();
        if (manyLinux)
        {
            verifiers.Add(new GlibcFloorVerifier(ManyLinuxImages.GlibcFloor));
        }

        var results = new List<CrateTargetResult>();
        var container = await workbench.StartContainerAsync(image, ContainerOptionsFor(request, sharedTarget), cancellationToken).ConfigureAwait(false);
        try
        {
            Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Message = $"Copying {copyRoot} into the build container." });
            await workbench.CopySourceAsync(container, copyRoot, null, null, cancellationToken).ConfigureAwait(false);
            foreach (var target in targets)
            {
                var crossTarget = CrateTargets.ToCrossTarget(target);
                var result = FailedResult(target, null);
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
            var ownership = await SharedCargoTarget.RestoreOwnershipAsync(workbench, container, sharedTarget).ConfigureAwait(false);
            if (ownership != null)
            {
                context.Warnings.Add(ownership);
            }

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
