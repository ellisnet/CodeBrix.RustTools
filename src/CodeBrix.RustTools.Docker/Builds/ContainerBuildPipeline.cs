using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Archives;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// The stages of <see cref="DockerRustWorkbench.BuildAsync"/>: engine check, image, container,
/// source copy, license gate, cargo per target, retrieval and cleanup, with a progress report
/// at every stage change and the time each stage took.
/// </summary>
internal sealed class ContainerBuildPipeline
{
    private readonly DockerRustWorkbench _workbench;
    private readonly ContainerBuildRequest _request;
    private readonly IProgress<ContainerBuildProgress> _progress;
    private readonly Dictionary<BuildStage, TimeSpan> _durations = new Dictionary<BuildStage, TimeSpan>();
    private readonly Stopwatch _stageClock = new Stopwatch();
    private BuildStage _stage = BuildStage.CheckingEngine;

    public ContainerBuildPipeline(DockerRustWorkbench workbench, ContainerBuildRequest request, IProgress<ContainerBuildProgress> progress)
    {
        _workbench = workbench;
        _request = request;
        _progress = progress;
    }

    /// <summary>
    /// The image spec the request needs: its own spec (or the base image) plus the layers of
    /// the requested targets, minus <see cref="CrossTarget.LinuxNative"/> and the engine's own
    /// Linux target, which build natively (<paramref name="engine"/> <c>null</c> keeps every
    /// Linux target, for tests).
    /// </summary>
    internal static BuildImageSpec ImageSpecFor(ContainerBuildRequest request, DockerEngineInfo engine = null)
    {
        var spec = request.ImageSpec?.Clone() ?? new BuildImageSpec();
        foreach (var target in request.Targets)
        {
            if (CrossTargetInfo.Get(target).RequiresLayer && !spec.CrossTargets.Contains(target))
            {
                spec.CrossTargets.Add(target);
            }
        }

        return engine == null ? spec : spec.WithoutNativeLinuxTargets(engine);
    }

    /// <summary>
    /// The targets cargo builds for a request, in order: every requested target, with
    /// <see cref="CrossTarget.LinuxNative"/> resolved to the engine's own Linux target and
    /// <see cref="CrossTarget.MacOSUniversal"/> replaced by its two slices; each once.
    /// </summary>
    /// <exception cref="CrossTargetNotSupportedException"><see cref="CrossTarget.LinuxNative"/>
    /// was requested and the engine's architecture has no Linux target.</exception>
    internal static IReadOnlyList<CrossTarget> BuildTargetsFor(IEnumerable<CrossTarget> requested, DockerEngineInfo engine)
    {
        var result = new List<CrossTarget>();
        foreach (var target in requested ?? Array.Empty<CrossTarget>())
        {
            var resolved = CrossTargetInfo.Resolve(target, engine);
            var expanded = CrossTargetInfo.Get(resolved).IsMacOSUniversal ? CrossTargetInfo.MacOSUniversalSlices : new[] { resolved };
            foreach (var each in expanded)
            {
                if (!result.Contains(each))
                {
                    result.Add(each);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// The <c>cargo metadata</c> options for one build target: the build's dependency
    /// selection plus <c>--filter-platform</c> with the target's triple. One call per target
    /// serves both the license audit and that target's crate-count estimate.
    /// </summary>
    internal static CargoMetadataOptions MetadataOptionsFor(CargoBuildOptions options, CrossTarget target)
        => RustWorkbench.ToMetadataOptions(options, CrateCountEstimator.FilterPlatformFor(CrossTargetInfo.Get(target).Triple));

    /// <summary>
    /// The container folder a project is built in: <c>/work</c> for the copied root, or
    /// <c>/work/&lt;relative path&gt;</c> for a workspace member below it.
    /// </summary>
    internal static string ContainerDirectoryFor(string copiedRoot, string projectDirectory)
    {
        var relative = Path.GetRelativePath(copiedRoot, projectDirectory).Replace('\\', '/');
        return relative == "." || relative.Length == 0
            ? BuildContainer.WorkDirectory
            : BuildContainer.WorkDirectory + "/" + relative.Trim('/');
    }

    /// <summary>
    /// The host folder copied into <c>/work</c>: <see cref="ContainerBuildRequest.CopyRootDirectory"/>
    /// when set (it must contain <paramref name="workspaceRoot"/>), else the workspace root.
    /// </summary>
    /// <exception cref="ArgumentException">The copy root does not contain the workspace
    /// root.</exception>
    internal static string CopyRootFor(ContainerBuildRequest request, string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(request.CopyRootDirectory))
        {
            return workspaceRoot;
        }

        var copyRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(request.CopyRootDirectory));
        var relative = Path.GetRelativePath(copyRoot, workspaceRoot);
        if (relative != "." && (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative)))
        {
            throw new ArgumentException($"CopyRootDirectory '{copyRoot}' does not contain the project's workspace root '{workspaceRoot}'.", "request");
        }

        return copyRoot;
    }

    /// <summary>
    /// The options of one target's cargo run: the build's options plus the
    /// <see cref="ContainerBuildRequest.TargetEnvironment"/> entries of every requested target
    /// that resolves to <paramref name="target"/> (the same object when there are none).
    /// </summary>
    internal static CargoBuildOptions OptionsForTarget(ContainerBuildRequest request, CargoBuildOptions options, CrossTarget target, DockerEngineInfo engine)
    {
        var extra = new List<KeyValuePair<string, string>>();
        foreach (var pair in request.TargetEnvironment ?? new Dictionary<CrossTarget, IDictionary<string, string>>())
        {
            if (pair.Value == null || !BuildTargetsFor(new[] { pair.Key }, engine).Contains(target))
            {
                continue;
            }

            extra.AddRange(pair.Value);
        }

        if (extra.Count == 0)
        {
            return options;
        }

        var copy = options.Clone();
        foreach (var pair in extra)
        {
            copy.Environment[pair.Key] = pair.Value;
        }

        return copy;
    }

    public async Task<ContainerBuildResult> RunAsync(CancellationToken cancellationToken)
    {
        var total = Stopwatch.StartNew();
        var options = _request.Options?.Clone() ?? new CargoBuildOptions();
        BuildImageSpec imageSpec = null;
        IReadOnlyList<CrossTarget> buildTargets = Array.Empty<CrossTarget>();
        var timeout = _request.Timeout;
        using var timeoutSource = timeout.HasValue && timeout.Value > TimeSpan.Zero && timeout.Value != Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource(timeout.Value)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var token = linked.Token;

        ExtractedSource ownedSource = null;
        BuildContainer container = null;
        var destroy = true;
        DockerEngineInfo engine = null;
        RustBuildImage image = null;
        ContainerBuildSource source = null;
        LicenseAuditReport audit = null;
        ArtifactRetrievalResult retrieval = null;
        BuildStage? failedStage = null;
        var results = new Dictionary<CrossTarget, CargoCommandResult>();

        try
        {
            Enter(BuildStage.CheckingEngine, "Checking the Docker engine");
            engine = await _workbench.CheckEngineAsync(token).ConfigureAwait(false);
            if (!engine.IsAvailable)
            {
                throw _workbench.Unavailable(engine, null);
            }

            imageSpec = ImageSpecFor(_request, engine);
            buildTargets = BuildTargetsFor(_request.Targets, engine);
            CrossTargetInfo.EnsureEngineCanBuild(engine, imageSpec.CrossTargets);

            Enter(BuildStage.EnsuringImage, "Preparing the build image from " + imageSpec.BaseImageReference);
            image = await _workbench.EnsureImageAsync(imageSpec, new LineProgress(this, BuildStage.EnsuringImage), token).ConfigureAwait(false);

            Enter(BuildStage.StartingContainer, "Starting a build container from " + image.Reference);
            var containerOptions = (_request.ContainerOptions ?? new BuildContainerOptions()).Clone();
            containerOptions.AndroidApiLevel = _request.AndroidApiLevel;
            container = await _workbench.StartContainerAsync(image, containerOptions, token).ConfigureAwait(false);
            destroy = _request.DestroyContainerAfterBuild ?? !container.KeepOnDispose;

            Enter(BuildStage.CopyingSource, "Copying the source into the container");
            string projectDirectory;
            string archivePath = null;
            var kind = ContainerBuildSourceKind.ProjectDirectory;
            if (!string.IsNullOrWhiteSpace(_request.ArchivePath))
            {
                kind = ContainerBuildSourceKind.Archive;
                archivePath = Path.GetFullPath(_request.ArchivePath);
                ownedSource = await new SourceArchiveExtractor().ExtractAsync(archivePath, null, _request.ArchiveOptions, token).ConfigureAwait(false);
                if (ownedSource.ManifestPath == null)
                {
                    throw new SourceArchiveException($"The archive '{archivePath}' does not contain a Cargo.toml, so there is nothing to build.", archivePath, null);
                }

                projectDirectory = ownedSource.ProjectDirectory;
            }
            else if (_request.ExtractedSource != null)
            {
                kind = ContainerBuildSourceKind.ExtractedSource;
                archivePath = _request.ExtractedSource.ArchivePath;
                projectDirectory = _request.ExtractedSource.ProjectDirectory;
            }
            else
            {
                projectDirectory = Path.GetFullPath(_request.ProjectDirectory);
            }

            var project = CargoProject.Open(projectDirectory);
            if (options.MacOSInstallName == null && _request.Targets.Any(t => CrossTargetInfo.Get(t).IsMacOS))
            {
                options.MacOSInstallName = MacOSLinkProfile.DefaultInstallName(project);
            }

            var copiedRoot = CopyRootFor(_request, project.WorkspaceRootManifestPath != null
                ? Path.GetDirectoryName(project.WorkspaceRootManifestPath)
                : project.Directory);
            var containerDirectory = ContainerDirectoryFor(copiedRoot, project.Directory);
            var copy = await _workbench.CopySourceAsync(container, copiedRoot, _request.ExtraExcludePatterns, new CopyProgressReporter(this), token).ConfigureAwait(false);
            source = new ContainerBuildSource
            {
                Kind = kind,
                ArchivePath = archivePath,
                HostDirectory = copiedRoot,
                HostManifestPath = project.ManifestPath,
                ContainerDirectory = containerDirectory,
                PackageName = project.Name,
                Copy = copy,
            };
            Report(BuildStage.CopyingSource, $"Copied {copy.FileCount} file(s), {copy.TotalBytes} byte(s)");

            // The total only feeds progress: without a reporter (and without a policy, whose
            // metadata is fetched anyway) no metadata call is made for it.
            var wantEstimate = !options.ExpectedCrateCount.HasValue;
            var estimate = wantEstimate && (_progress != null || _workbench.HasProgressListeners || options.LicensePolicy != null);
            var metadataByTarget = new Dictionary<CrossTarget, CargoMetadata>();
            if (options.LicensePolicy != null)
            {
                Enter(BuildStage.AuditingLicenses, "Auditing dependency licenses inside the container");
                foreach (var target in buildTargets)
                {
                    metadataByTarget[target] = await _workbench.GetMetadataCoreAsync(container, containerDirectory + "/Cargo.toml", MetadataOptionsFor(options, target), token).ConfigureAwait(false);
                }

                audit = LicenseAuditor.Audit(CargoMetadata.Union(buildTargets.Select(t => metadataByTarget[t]).ToList()), options.LicensePolicy);
                if (audit.Passed)
                {
                    Report(BuildStage.AuditingLicenses, "License audit passed");
                }
                else if (options.LicenseGateMode == LicenseGateMode.ReportOnly)
                {
                    Report(BuildStage.AuditingLicenses, RustWorkbench.ReportOnlyAuditMessage(audit));
                }
                else
                {
                    if (options.ThrowOnLicenseViolation)
                    {
                        throw new LicensePolicyViolationException(
                            $"The container build of '{project.ManifestPath}' was not run because the license audit failed: {audit.RejectedCount} rejected and {audit.UnresolvedCount} unresolved crate(s).",
                            audit);
                    }

                    failedStage = BuildStage.AuditingLicenses;
                    foreach (var target in buildTargets)
                    {
                        results[target] = CargoCommandResult.ForFailedLicenseGate(CargoCommand.Build, audit, containerDirectory);
                    }

                    Report(BuildStage.AuditingLicenses, $"License audit failed: {audit.RejectedCount} rejected, {audit.UnresolvedCount} unresolved");
                }
            }

            if (failedStage == null)
            {
                Enter(BuildStage.Building, "Building " + string.Join(", ", buildTargets));
                foreach (var target in buildTargets)
                {
                    Report(BuildStage.Building, $"Building for {CrossTargetInfo.Get(target)}");
                    var targetOptions = OptionsForTarget(_request, options, target, engine);
                    if (estimate)
                    {
                        if (!metadataByTarget.TryGetValue(target, out var metadata))
                        {
                            metadata = await _workbench.TryGetMetadataCoreAsync(container, containerDirectory + "/Cargo.toml", MetadataOptionsFor(options, target), token).ConfigureAwait(false);
                        }

                        targetOptions = targetOptions.Clone();
                        targetOptions.ExpectedCrateCount = CrateCountEstimator.Estimate(metadata, CargoCommand.Build, options);
                    }

                    var result = await _workbench.RunCargoCoreAsync(
                        container, CargoCommand.Build, targetOptions, target, containerDirectory,
                        cargoProgress => Report(BuildStage.Building, null, cargoProgress), token).ConfigureAwait(false);
                    result.LicenseAudit = audit;
                    results[target] = result;
                    if (!result.Success)
                    {
                        failedStage = BuildStage.Building;
                        if (!_request.ContinueOnTargetFailure)
                        {
                            break;
                        }
                    }
                }
            }

            if (_request.Retrieval != null && results.Values.Any(r => r.Success))
            {
                Enter(BuildStage.RetrievingArtifacts, "Copying the built files to " + _request.Retrieval.HostDirectory);
                var transport = new DockerCopyTransport(container.Client, container.Id);
                var parts = new List<ArtifactRetrievalResult>();
                foreach (var requested in _request.Targets)
                {
                    var target = CrossTargetInfo.Resolve(requested, engine);
                    if (CrossTargetInfo.Get(target).IsMacOSUniversal)
                    {
                        if (results.TryGetValue(CrossTarget.MacOSArm64, out var arm64) && arm64.Success
                            && results.TryGetValue(CrossTarget.MacOSX64, out var x64) && x64.Success)
                        {
                            Report(BuildStage.RetrievingArtifacts, "Joining the macOS slices into universal files");
                            parts.Add(await ArtifactRetriever.RetrieveUniversalAsync(transport, arm64.Artifacts, x64.Artifacts, _request.Retrieval, token).ConfigureAwait(false));
                        }
                    }
                    else if (results.TryGetValue(target, out var result) && result.Success)
                    {
                        parts.Add(await ArtifactRetriever.RetrieveAsync(transport, result.Artifacts, _request.Retrieval, target, engine, token).ConfigureAwait(false));
                    }
                }

                retrieval = ArtifactRetrievalResult.Combine(Path.GetFullPath(_request.Retrieval.HostDirectory), parts);
                Report(BuildStage.RetrievingArtifacts, $"Copied {retrieval.Artifacts.Count} file(s), {retrieval.TotalBytes} byte(s)");
            }
        }
        catch (Exception ex)
        {
            var failedIn = _stage;
            await CleanUpAfterFailureAsync(container, destroy, ownedSource).ConfigureAwait(false);
            Finish(failedIn);
            Report(BuildStage.Failed, $"Failed while {Describe(failedIn)}: {ex.Message}");
            if (ex is OperationCanceledException && timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new ContainerBuildException($"The container build did not finish within {timeout}.", new TimeoutException(ex.Message, ex));
            }

            throw;
        }

        Enter(BuildStage.CleaningUp, destroy ? "Removing the build container" : "Keeping the build container");
        try
        {
            if (destroy)
            {
                await container.StopAndRemoveAsync(_request.RemoveTargetVolumeAfterBuild, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (CodeBrix.Docker.DockerException ex)
        {
            throw new ContainerBuildException($"The build finished but the build container {container} could not be removed: {ex.Message}", ex);
        }
        finally
        {
            ownedSource?.Dispose();
        }

        Finish(_stage);
        var success = failedStage == null;
        Report(success ? BuildStage.Completed : BuildStage.Failed,
            success ? $"Built {string.Join(", ", _request.Targets)} in {total.Elapsed.TotalSeconds:0.0} s" : $"Build failed while {Describe(failedStage.Value)}",
            null,
            success ? 100 : null);

        return new ContainerBuildResult
        {
            Engine = engine,
            Image = image,
            ContainerId = container.Id,
            ContainerName = container.Name,
            ContainerRemoved = container.IsRemoved,
            Source = source,
            Results = results,
            LicenseAudit = audit,
            Retrieval = retrieval,
            StageDurations = new Dictionary<BuildStage, TimeSpan>(_durations),
            Duration = total.Elapsed,
            FailedStage = failedStage,
            Success = success,
        };
    }

    private static async Task CleanUpAfterFailureAsync(BuildContainer container, bool destroy, ExtractedSource ownedSource)
    {
        try
        {
            if (container != null && destroy)
            {
                await container.StopAndRemoveAsync(true, CancellationToken.None).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is CodeBrix.Docker.DockerException || ex is ObjectDisposedException)
        {
            // Best effort: the original failure matters more; the labels let CleanupAsync find it.
        }
        finally
        {
            ownedSource?.Dispose();
        }
    }

    private static string Describe(BuildStage stage) => stage switch
    {
        BuildStage.CheckingEngine => "checking the Docker engine",
        BuildStage.EnsuringImage => "preparing the build image",
        BuildStage.StartingContainer => "starting the build container",
        BuildStage.CopyingSource => "copying the source",
        BuildStage.AuditingLicenses => "auditing licenses",
        BuildStage.Building => "building",
        BuildStage.RetrievingArtifacts => "retrieving artifacts",
        BuildStage.CleaningUp => "cleaning up",
        _ => stage.ToString(),
    };

    private void Enter(BuildStage stage, string message)
    {
        if (_stageClock.IsRunning)
        {
            Finish(_stage);
        }

        _stage = stage;
        _stageClock.Restart();
        Report(stage, message);
    }

    private void Finish(BuildStage stage)
    {
        if (_stageClock.IsRunning)
        {
            _durations[stage] = _durations.TryGetValue(stage, out var earlier) ? earlier + _stageClock.Elapsed : _stageClock.Elapsed;
            _stageClock.Reset();
        }
    }

    private void Report(BuildStage stage, string message, CargoProgress cargoProgress = null, double? percent = null)
    {
        if (percent == null && cargoProgress?.Total is int totalCrates && totalCrates > 0)
        {
            percent = Math.Min(100.0, 100.0 * cargoProgress.Completed / totalCrates);
        }

        _workbench.Publish(new ContainerBuildProgress
        {
            Stage = stage,
            Message = message ?? cargoProgress?.Message ?? string.Empty,
            CargoProgress = cargoProgress,
            Percent = percent,
        }, _progress);
    }

    private sealed class LineProgress : IProgress<string>
    {
        private readonly ContainerBuildPipeline _owner;
        private readonly BuildStage _stage;

        public LineProgress(ContainerBuildPipeline owner, BuildStage stage)
        {
            _owner = owner;
            _stage = stage;
        }

        public void Report(string value) => _owner.Report(_stage, value);
    }

    private sealed class CopyProgressReporter : IProgress<SourceCopyProgress>
    {
        private readonly ContainerBuildPipeline _owner;

        public CopyProgressReporter(ContainerBuildPipeline owner)
        {
            _owner = owner;
        }

        public void Report(SourceCopyProgress value) => _owner.Report(BuildStage.CopyingSource, value.ToString());
    }
}
