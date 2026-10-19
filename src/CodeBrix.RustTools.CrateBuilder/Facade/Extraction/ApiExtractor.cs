using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Running;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Extraction;

/// <summary>
/// Runs codebrix-api-extractor against a staged crate and returns its public API as a
/// <see cref="CrateApiModel"/>. The tool's source is materialised once per tool version into
/// <c>&lt;scratch&gt;/api-extractor/&lt;hash&gt;/</c> and built once there:
/// <list type="number">
/// <item><description>Host mode, or Docker mode on a machine whose own Rust toolchain is
/// ready: built with CodeBrix.RustTools (release) and run on the host.</description></item>
/// <item><description>Docker mode without a usable host toolchain, Linux host of the engine's
/// architecture: built in a container, the Linux executable copied to the host and run
/// there (when it does not run on the host - for example an older glibc than the rust
/// image's - it is run inside the container instead).</description></item>
/// <item><description>Docker mode otherwise (Windows / macOS hosts, or another
/// architecture): the tool's source and the staged workspace copied side by side into one
/// container (<c>/work/codebrix-api-extractor</c> and <c>/work/crate</c>), the tool built and
/// run there; the JSON comes back on standard output. The container's target
/// volume is kept (named after the tool version) so later runs reuse the build.</description></item>
/// </list>
/// With <see cref="CrateBuilderOptions.CargoTargetDirectory"/> the tool's cargo runs use
/// <c>api-extractor/&lt;hash&gt;/</c> (host) or <c>api-extractor-docker/&lt;hash&gt;/</c>
/// (container, bind-mounted, see <see cref="SharedCargoTarget"/>) below it as cargo's target
/// folder; the hash is the tool version's, as in the scratch folder.
/// Diagnostics of severity warning or error become context warnings.
/// </summary>
internal sealed class ApiExtractor
{
    /// <summary>The file name of the JSON document in the run folder.</summary>
    public const string DocumentFileName = "crate-api.json";

    /// <summary>The <c>/work</c> sub-folder the tool's source is copied to on the in-container
    /// route.</summary>
    public const string ToolContainerFolder = "codebrix-api-extractor";

    /// <summary>The <c>/work</c> sub-folder the staged crate is copied to on the in-container
    /// route (beside <see cref="ToolContainerFolder"/>).</summary>
    public const string CrateContainerFolder = "crate";

    /// <summary>The tool's exit code for "no crate root / unusable manifest".</summary>
    public const int NoCrateRootExitCode = 2;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> BuildLocks = new ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.Ordinal);

    private readonly ApiExtractorOptions _options;

    /// <summary>Creates an extractor.</summary>
    /// <param name="options">Its settings; <c>null</c> = the defaults.</param>
    public ApiExtractor(ApiExtractorOptions options = null)
    {
        _options = options ?? new ApiExtractorOptions();
    }

    /// <summary>The settings.</summary>
    public ApiExtractorOptions Options => _options;

    /// <summary>
    /// Extracts the public API of the staged crate (its upstream manifest).
    /// </summary>
    /// <exception cref="FacadeGenerationException">Stage <see cref="CrateBuildStage.ExtractingApi"/>:
    /// the tool could not be built or run, it found no crate root, or its output could not be
    /// read.</exception>
    public async Task<CrateApiModel> ExtractAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, CancellationToken ct)
    {
        if (workspace == null)
        {
            throw new ArgumentNullException(nameof(workspace));
        }

        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        var manifest = workspace.UpstreamManifestPath ?? workspace.ManifestPath;
        if (string.IsNullOrWhiteSpace(manifest) || !File.Exists(manifest))
        {
            throw new FacadeGenerationException("The staged crate's Cargo.toml was not found: " + manifest, CrateBuildStage.ExtractingApi);
        }

        var outputPath = Path.Combine(context.RunDirectory ?? context.ScratchRootDirectory, "api", DocumentFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        try
        {
            context.Report(CrateBuildStage.ExtractingApi, "Preparing the API extractor tool");
            var toolDirectory = ApiExtractorToolSource.Materialise(context.ScratchRootDirectory);
            var route = await ChooseRouteAsync(request, context, ct).ConfigureAwait(false);
            if (route == ExtractionRoute.InContainer)
            {
                await RunInContainerAsync(toolDirectory, workspace, manifest, request, context, outputPath, ct).ConfigureAwait(false);
            }
            else if (route == ExtractionRoute.Host)
            {
                var executable = await BuildOnHostAsync(toolDirectory, request, context, ct).ConfigureAwait(false);
                await RunOnHostAsync(executable, workspace, manifest, request, context, outputPath, ct).ConfigureAwait(false);
            }
            else
            {
                var executable = await BuildInContainerForHostAsync(toolDirectory, request, context, ct).ConfigureAwait(false);
                try
                {
                    await RunOnHostAsync(executable, workspace, manifest, request, context, outputPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException && !(ex is ExtractorExitException exit && exit.ExitCode == NoCrateRootExitCode))
                {
                    // The container-built program may not start on this host (for example an
                    // older glibc than the rust image's): run it inside the container instead.
                    context.Report(CrateBuildStage.ExtractingApi, "The container-built API extractor did not run on this host (" + ex.Message + "); running it inside the container");
                    await RunInContainerAsync(toolDirectory, workspace, manifest, request, context, outputPath, ct).ConfigureAwait(false);
                }
            }

            var model = CrateApiModel.Load(outputPath);
            foreach (var diagnostic in model.Diagnostics.Where(d => d.Severity != RustDiagnosticSeverity.Info))
            {
                context.Warnings.Add("API extraction: " + diagnostic);
            }

            context.Report(CrateBuildStage.ExtractingApi, $"Extracted {model.Functions.Count} functions, {model.Structs.Count} structs and {model.Enums.Count} enums from {model.Crate.Name}");
            return model;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FacadeGenerationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is RustToolsException || ex is IOException || ex is InvalidDataException || ex is UnauthorizedAccessException || ex is InvalidOperationException)
        {
            throw new FacadeGenerationException("API extraction failed: " + ex.Message, CrateBuildStage.ExtractingApi, ex);
        }
    }

    internal enum ExtractionRoute
    {
        Host,
        ContainerBuildHostRun,
        InContainer,
    }

    /// <summary>The route for this request on this machine.</summary>
    internal async Task<ExtractionRoute> ChooseRouteAsync(CrateBuildRequest request, CrateBuilderContext context, CancellationToken ct)
    {
        if (request.Mode == BuildMode.Host)
        {
            return ExtractionRoute.Host;
        }

        if (_options.PreferHostToolchain)
        {
            var workbench = context.RustWorkbench;
            var toolchain = await workbench.DetectToolchainAsync(null, ct).ConfigureAwait(false);
            if (workbench.EvaluateReadiness(toolchain).IsReady)
            {
                return ExtractionRoute.Host;
            }
        }

        switch (_options.DockerRoute)
        {
            case ApiExtractorDockerRoute.RunInContainer:
                return ExtractionRoute.InContainer;
            case ApiExtractorDockerRoute.RetrieveAndRunOnHost:
                return ExtractionRoute.ContainerBuildHostRun;
            default:
                var engine = await context.DockerWorkbench.CheckEngineAsync(ct).ConfigureAwait(false);
                var hostRid = HostLinuxRuntimeIdentifier();
                return hostRid != null && string.Equals(hostRid, engine.NativeLinuxRuntimeIdentifier, StringComparison.Ordinal)
                    ? ExtractionRoute.ContainerBuildHostRun
                    : ExtractionRoute.InContainer;
        }
    }

    /// <summary>linux-x64 / linux-arm64 / linux-riscv64 on a Linux host, else <c>null</c>.</summary>
    internal static string HostLinuxRuntimeIdentifier()
    {
        if (!OperatingSystem.IsLinux())
        {
            return null;
        }

        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "linux-x64",
            Architecture.Arm64 => "linux-arm64",
            Architecture.RiscV64 => "linux-riscv64",
            _ => null,
        };
    }

    /// <summary>The tool's command-line arguments (after the executable).</summary>
    internal static IReadOnlyList<string> BuildArguments(string manifestPath, string outPath, StagedWorkspace workspace, CrateBuildRequest request)
    {
        var arguments = new List<string> { "--manifest", manifestPath, "--out", outPath };
        if (!workspace.IsFacade && !string.IsNullOrWhiteSpace(workspace.CrateName))
        {
            arguments.Add("--crate");
            arguments.Add(workspace.CrateName);
        }

        var features = (request.Features ?? new List<string>()).Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
        if (features.Count > 0)
        {
            arguments.Add("--features");
            arguments.Add(string.Join(",", features));
        }

        return arguments;
    }

    private CargoBuildOptions ToolBuildOptions(CrateBuildRequest request) => new CargoBuildOptions
    {
        Profile = CargoProfile.Release,
        Locked = true,
        Offline = request.Offline,
        Timeout = _options.BuildTimeout,
    };

    /// <summary>The tool version's sub-folder of a shared cargo target folder
    /// (<paramref name="folderName"/> below <see cref="CrateBuilderOptions.CargoTargetDirectory"/>,
    /// then the tool's hash), or <c>null</c> when no shared folder is set.</summary>
    internal static string SharedToolTargetDirectory(CrateBuilderOptions options, string folderName)
    {
        var folder = SharedCargoTarget.For(options, folderName);
        return folder == null ? null : Path.Combine(folder, ApiExtractorToolSource.ContentHash.Substring(0, ApiExtractorToolSource.FolderHashLength));
    }

    private static string ExecutableName => OperatingSystem.IsWindows() ? ApiExtractorToolSource.ToolName + ".exe" : ApiExtractorToolSource.ToolName;

    private async Task<string> BuildOnHostAsync(string toolDirectory, CrateBuildRequest request, CrateBuilderContext context, CancellationToken ct)
    {
        var shared = SharedToolTargetDirectory(context.Options, SharedCargoTarget.ExtractorHostFolderName);
        var expected = Path.Combine(shared ?? Path.Combine(toolDirectory, "target"), "release", ExecutableName);
        var marker = Path.Combine(toolDirectory, "host-build.ok");
        var gate = BuildLocks.GetOrAdd(toolDirectory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(marker) && File.Exists(expected))
            {
                return expected;
            }

            context.Report(CrateBuildStage.ExtractingApi, "Building the API extractor tool with the installed Rust toolchain (first use only)");
            var workbench = context.RustWorkbench;
            var project = workbench.OpenProject(Path.Combine(toolDirectory, "Cargo.toml"));
            var options = ToolBuildOptions(request);
            SharedCargoTarget.ApplyHost(options, shared);
            var result = await workbench.BuildAsync(project, options, null, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new FacadeGenerationException("The API extractor tool did not build: " + Describe(result), CrateBuildStage.ExtractingApi);
            }

            var executable = result.Artifacts.FirstOrDefault(a => a.Executable != null && a.TargetName == ApiExtractorToolSource.ToolName)?.Executable ?? expected;
            if (!File.Exists(executable))
            {
                throw new FacadeGenerationException("The API extractor tool built, but its executable was not found: " + executable, CrateBuildStage.ExtractingApi);
            }

            File.WriteAllText(marker, executable + "\n");
            return executable;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<string> BuildInContainerForHostAsync(string toolDirectory, CrateBuildRequest request, CrateBuilderContext context, CancellationToken ct)
    {
        var outputDirectory = Path.Combine(toolDirectory, "docker-" + (HostLinuxRuntimeIdentifier() ?? "linux"));
        var expected = Path.Combine(outputDirectory, ApiExtractorToolSource.ToolName);
        var marker = Path.Combine(outputDirectory, "docker-build.ok");
        var gate = BuildLocks.GetOrAdd(outputDirectory, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (File.Exists(marker) && File.Exists(expected))
            {
                return expected;
            }

            context.Report(CrateBuildStage.ExtractingApi, "Building the API extractor tool in a Docker container (first use only)");
            var shared = SharedToolTargetDirectory(context.Options, SharedCargoTarget.ExtractorDockerFolderName);
            if (shared != null)
            {
                var built = await BuildInContainerWithSharedTargetAsync(toolDirectory, outputDirectory, shared, request, context, ct).ConfigureAwait(false);
                File.WriteAllText(marker, built + "\n");
                return built;
            }

            var buildRequest = new ContainerBuildRequest
            {
                ProjectDirectory = toolDirectory,
                Options = ToolBuildOptions(request),
                Targets = new List<CrossTarget> { CrossTarget.LinuxNative },
                ExtraExcludePatterns = new List<string> { "docker-*" },
                Retrieval = new ArtifactRetrievalOptions
                {
                    HostDirectory = outputDirectory,
                    Layout = ArtifactLayout.Flat,
                    IncludeExecutables = true,
                    IncludeSharedLibraries = false,
                },
            };
            var result = await context.DockerWorkbench.BuildAsync(buildRequest, null, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                throw new FacadeGenerationException(
                    "The API extractor tool did not build in the container: " + string.Join("; ", result.Results.Values.Select(Describe)),
                    CrateBuildStage.ExtractingApi);
            }

            var executable = result.Retrieval?.Artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Executable && a.TargetName == ApiExtractorToolSource.ToolName)?.HostPath;
            if (executable == null || !File.Exists(executable))
            {
                throw new FacadeGenerationException("The API extractor tool built in the container, but its executable was not retrieved.", CrateBuildStage.ExtractingApi);
            }

            File.WriteAllText(marker, executable + "\n");
            return executable;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>The container build of the tool for the host when a shared cargo target folder
    /// is set: one container with <paramref name="sharedTarget"/> bind-mounted as cargo's target
    /// folder, the executable retrieved into <paramref name="outputDirectory"/>, then the files
    /// handed back to the folder's owner and the container removed.</summary>
    private async Task<string> BuildInContainerWithSharedTargetAsync(string toolDirectory, string outputDirectory, string sharedTarget, CrateBuildRequest request, CrateBuilderContext context, CancellationToken ct)
    {
        var workbench = context.DockerWorkbench;
        var image = await workbench.EnsureImageAsync(null, null, ct).ConfigureAwait(false);
        var container = await workbench.StartContainerAsync(image, SharedCargoTarget.Mount(new BuildContainerOptions { KeepOnDispose = false }, sharedTarget), ct).ConfigureAwait(false);
        try
        {
            await workbench.CopySourceAsync(container, toolDirectory, new[] { "docker-*" }, null, ct).ConfigureAwait(false);
            var options = ToolBuildOptions(request);
            SharedCargoTarget.ApplyContainer(options, sharedTarget);
            var build = await workbench.RunCargoAsync(container, CargoCommand.Build, options, CrossTarget.LinuxNative, null, ct).ConfigureAwait(false);
            if (!build.Success)
            {
                throw new FacadeGenerationException("The API extractor tool did not build in the container: " + Describe(build), CrateBuildStage.ExtractingApi);
            }

            var retrieval = await workbench.RetrieveArtifactsAsync(
                container,
                build.Artifacts,
                new ArtifactRetrievalOptions
                {
                    HostDirectory = outputDirectory,
                    Layout = ArtifactLayout.Flat,
                    IncludeExecutables = true,
                    IncludeSharedLibraries = false,
                    Overwrite = true,
                },
                CrossTarget.LinuxNative,
                ct).ConfigureAwait(false);
            var executable = retrieval?.Artifacts.FirstOrDefault(a => a.Kind == ArtifactKind.Executable && a.TargetName == ApiExtractorToolSource.ToolName)?.HostPath;
            if (executable == null || !File.Exists(executable))
            {
                throw new FacadeGenerationException("The API extractor tool built in the container, but its executable was not retrieved.", CrateBuildStage.ExtractingApi);
            }

            return executable;
        }
        finally
        {
            var ownership = await SharedCargoTarget.RestoreOwnershipAsync(workbench, container, sharedTarget).ConfigureAwait(false);
            if (ownership != null)
            {
                context.Warnings.Add(ownership);
            }

            await container.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task RunOnHostAsync(string executable, StagedWorkspace workspace, string manifest, CrateBuildRequest request, CrateBuilderContext context, string outputPath, CancellationToken ct)
    {
        context.Report(CrateBuildStage.ExtractingApi, "Reading the public API of " + manifest);
        var runner = new BuiltBinaryRunner(context.Options.HostOptions);
        var run = new RunOptions { Timeout = _options.RunTimeout, WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(manifest)) };
        foreach (var argument in BuildArguments(Path.GetFullPath(manifest), outputPath, workspace, request))
        {
            run.Arguments.Add(argument);
        }

        var result = await runner.RunAsync(executable, run, ct).ConfigureAwait(false);
        if (result.TimedOut)
        {
            throw new FacadeGenerationException($"The API extractor did not finish within {_options.RunTimeout}.", CrateBuildStage.ExtractingApi);
        }

        if (result.ExitCode != 0)
        {
            throw new ExtractorExitException(result.ExitCode, FailureMessage(result.ExitCode, result.StandardError));
        }
    }

    private async Task RunInContainerAsync(string toolDirectory, StagedWorkspace workspace, string manifest, CrateBuildRequest request, CrateBuilderContext context, string outputPath, CancellationToken ct)
    {
        var stagingRoot = Path.GetFullPath(workspace.Directory ?? Path.GetDirectoryName(manifest));
        var relativeManifest = Path.GetRelativePath(stagingRoot, Path.GetFullPath(manifest)).Replace('\\', '/');
        if (relativeManifest.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relativeManifest))
        {
            throw new FacadeGenerationException("The staged Cargo.toml is not inside the staging folder: " + manifest, CrateBuildStage.ExtractingApi);
        }

        var workbench = context.DockerWorkbench;
        context.Report(CrateBuildStage.ExtractingApi, "Preparing a Docker container for the API extractor tool");
        var image = await workbench.EnsureImageAsync(null, null, ct).ConfigureAwait(false);
        var hash = ApiExtractorToolSource.ContentHash.Substring(0, ApiExtractorToolSource.FolderHashLength);
        var shared = SharedToolTargetDirectory(context.Options, SharedCargoTarget.ExtractorDockerFolderName);
        var containerOptions = SharedCargoTarget.Mount(
            new BuildContainerOptions
            {
                KeepOnDispose = false,
                TargetVolumeName = workbench.Options.ContainerNamePrefix + "-api-extractor-" + hash,
            },
            shared);
        var container = await workbench.StartContainerAsync(image, containerOptions, ct).ConfigureAwait(false);
        var removed = false;
        try
        {
            // The tool and the staged crate sit side by side below /work (CopySourceIntoAsync
            // empties only its own sub-folder), so neither copy disturbs the other.
            await workbench.CopySourceIntoAsync(container, toolDirectory, ToolContainerFolder, new[] { "docker-*", "target" }, null, ct).ConfigureAwait(false);
            context.Report(CrateBuildStage.ExtractingApi, "Building the API extractor tool in the container (reused while the target volume exists)");
            var toolOptions = ToolBuildOptions(request);
            toolOptions.WorkingDirectory = ToolContainerFolder;
            SharedCargoTarget.ApplyContainer(toolOptions, shared);
            var build = await workbench.RunCargoAsync(container, CargoCommand.Build, toolOptions, CrossTarget.LinuxNative, null, ct).ConfigureAwait(false);
            if (!build.Success)
            {
                throw new FacadeGenerationException("The API extractor tool did not build in the container: " + Describe(build), CrateBuildStage.ExtractingApi);
            }

            var executable = build.Artifacts.FirstOrDefault(a => a.Executable != null && a.TargetName == ApiExtractorToolSource.ToolName)?.Executable
                ?? (shared != null ? SharedCargoTarget.ContainerPath : BuildContainer.TargetPath) + "/release/" + ApiExtractorToolSource.ToolName;

            context.Report(CrateBuildStage.ExtractingApi, "Copying the staged crate into the container");
            await workbench.CopySourceIntoAsync(container, stagingRoot, CrateContainerFolder, null, null, ct).ConfigureAwait(false);
            var containerManifest = BuildContainer.WorkDirectory + "/" + CrateContainerFolder + "/" + relativeManifest;
            var command = new List<string> { executable };
            command.AddRange(BuildArguments(containerManifest, "-", workspace, request));
            string json;
            try
            {
                json = await workbench.ExecAsync(container, command, ct).ConfigureAwait(false);
            }
            catch (ContainerBuildException ex) when (ex.ExitCode.HasValue && ex.ExitCode.Value != 0)
            {
                throw new FacadeGenerationException(FailureMessage(ex.ExitCode.Value, ex.StandardError), CrateBuildStage.ExtractingApi, ex);
            }

            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            await RestoreSharedOwnershipAsync(workbench, container, shared, context).ConfigureAwait(false);
            await container.StopAndRemoveAsync(removeTargetVolume: false, ct).ConfigureAwait(false);
            removed = true;
        }
        finally
        {
            if (!removed)
            {
                await RestoreSharedOwnershipAsync(workbench, container, shared, context).ConfigureAwait(false);
                await container.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task RestoreSharedOwnershipAsync(DockerRustWorkbench workbench, BuildContainer container, string shared, CrateBuilderContext context)
    {
        var ownership = await SharedCargoTarget.RestoreOwnershipAsync(workbench, container, shared).ConfigureAwait(false);
        if (ownership != null)
        {
            context.Warnings.Add(ownership);
        }
    }

    /// <summary>A non-zero exit of the tool run on the host (stage ExtractingApi).</summary>
    private sealed class ExtractorExitException : FacadeGenerationException
    {
        public ExtractorExitException(long exitCode, string message)
            : base(message, CrateBuildStage.ExtractingApi)
        {
            ExitCode = exitCode;
        }

        public long ExitCode { get; }
    }

    private static string FailureMessage(long exitCode, string standardError)
    {
        var detail = string.IsNullOrWhiteSpace(standardError) ? string.Empty : ": " + standardError.Trim();
        return exitCode == NoCrateRootExitCode
            ? "The API extractor found no crate to read" + detail
            : $"The API extractor failed with exit code {exitCode}" + detail;
    }

    private static string Describe(CargoCommandResult result)
    {
        if (result == null)
        {
            return "no result";
        }

        if (result.TimedOut)
        {
            return "cargo timed out";
        }

        var errors = result.Errors.Select(e => e.Message).Take(3).ToList();
        if (errors.Count > 0)
        {
            return string.Join("; ", errors);
        }

        var stderr = (result.StandardError ?? string.Empty).Trim();
        return stderr.Length == 0 ? "cargo exited with code " + result.ExitCode : stderr;
    }
}
