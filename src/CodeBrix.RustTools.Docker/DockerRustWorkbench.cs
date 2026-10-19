using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Internal;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// The entry point for building Rust code inside Linux Docker containers. A workbench checks
/// the Docker engine (and returns guidance when it cannot be used), prepares build images,
/// starts labelled build containers with persistent cargo cache volumes, copies project source
/// in, runs cargo inside them (with the sibling package's diagnostics, progress, test and
/// license-gate model), copies the built files out, and removes what it created.
/// <see cref="BuildAsync"/> does all of it in one call.
/// </summary>
/// <remarks>
/// The workbench owns one Docker client; create one workbench and keep it. It is safe to use
/// from several threads. Dispose the build containers before the workbench.
/// </remarks>
public sealed class DockerRustWorkbench : IDisposable
{
    private readonly object _clientLock = new object();
    private readonly ResourceLabels _labels;
    private DockerClient _client;
    private volatile bool _engineVerified;
    private volatile DockerEngineInfo _engineInfo;
    private bool _disposed;

    /// <summary>
    /// Creates a workbench. Nothing contacts the engine until the first call that needs it.
    /// </summary>
    /// <param name="options">The settings; <c>null</c> uses the defaults. The options are copied,
    /// so later changes to the object have no effect.</param>
    /// <exception cref="ArgumentException">A required option is blank.</exception>
    public DockerRustWorkbench(DockerRustWorkbenchOptions options = null)
    {
        Options = (options ?? new DockerRustWorkbenchOptions()).Clone();
        Options.Validate();
        InstanceId = Guid.NewGuid().ToString("N");
        _labels = new ResourceLabels(Options.LabelPrefix, InstanceId);
    }

    /// <summary>
    /// Raised for every progress report of <see cref="BuildAsync"/> (in addition to its
    /// <see cref="IProgress{T}"/>), and for the cargo progress of <see cref="RunCargoAsync"/> and
    /// <see cref="TestAsync"/> (as <see cref="BuildStage.Building"/> reports). Raised on the
    /// thread that produced the report; exceptions thrown by handlers are ignored.
    /// </summary>
    public event EventHandler<ContainerBuildProgress> Progress;

    /// <summary>Whether anyone receives <see cref="Progress"/> (the crate-count estimate is only
    /// worth a <c>cargo metadata</c> call when someone does).</summary>
    internal bool HasProgressListeners => Progress != null;

    /// <summary>The workbench's settings (a private copy of what was passed in).</summary>
    public DockerRustWorkbenchOptions Options { get; }

    /// <summary>
    /// A random identifier for this workbench instance, written into the
    /// <c>&lt;prefix&gt;.workbench</c> label of everything it creates so that
    /// <see cref="CleanupOptions.OnlyThisWorkbench"/> can find exactly those resources.
    /// </summary>
    public string InstanceId { get; }

    internal ResourceLabels Labels => _labels;

    internal DockerClient Client => GetClient();

    /// <summary>
    /// Checks whether the Docker engine can run Linux build containers. Never throws for a
    /// Docker-side problem: the result's <see cref="DockerEngineInfo.IsAvailable"/> and
    /// <see cref="DockerEngineInfo.Issues"/> say what is wrong, and
    /// <see cref="GetDockerGuidance"/> turns them into instructions.
    /// </summary>
    /// <param name="cancellationToken">Cancels the check.</param>
    public async Task<DockerEngineInfo> CheckEngineAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        DockerClient client;
        try
        {
            client = GetClient();
        }
        catch (DockerUnavailableException ex) when (ex.Engine != null)
        {
            return ex.Engine;
        }

        var info = await DockerAvailability.CheckAsync(client, Options.CreateClientOptions().DockerCliPath, cancellationToken).ConfigureAwait(false);
        if (info.IsAvailable)
        {
            _engineInfo = info;
            _engineVerified = true;
        }

        return info;
    }

    /// <summary>
    /// Returns the operating-system-specific steps that resolve the issues an engine check
    /// found (install Docker, start it, grant access, switch to Linux containers). Empty when
    /// the engine has no issues. The workbench never runs these commands.
    /// </summary>
    /// <param name="info">A result of <see cref="CheckEngineAsync"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <c>null</c>.</exception>
    public IReadOnlyList<DockerGuidance> GetDockerGuidance(DockerEngineInfo info) => DockerGuidanceProvider.ForEngine(info);

    /// <summary>
    /// Makes sure the build image a spec describes exists on the engine. Pulls the base image
    /// when it is missing; when the spec adds no layers the base image itself is the result
    /// (<see cref="RustBuildImage.IsBaseImage"/>); otherwise the derived image tagged with the
    /// hash of its generated Dockerfile is built once and reused afterwards. Building a derived
    /// image needs the <c>docker</c> command-line tool (BuildKit), and the generated layers are
    /// Debian (<c>apt</c>) based. With <see cref="BuildImageSpec.Containerfile"/> the base image is
    /// built from that Containerfile instead of being pulled (once per content hash; see the
    /// property) - the route for a non-Debian base such as manylinux - and is used as it is when
    /// the spec adds no layers.
    /// </summary>
    /// <param name="spec">The image to prepare; <c>null</c> means the workbench's base image
    /// with no layers.</param>
    /// <param name="progress">Receives image-pull and image-build output lines; may be
    /// <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the pull or build. The
    /// <see cref="DockerRustTimeouts.ImagePreparation"/> limit applies as well.</param>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="ContainerBuildException">The pull or the image build failed (the
    /// message includes the engine's or the build's explanation).</exception>
    /// <exception cref="CrossTargetNotSupportedException">The spec asks for a macOS target, or
    /// for an Android target on an engine that is not x86-64 (the Android NDK's compilers run on
    /// x86-64 Linux only).</exception>
    /// <exception cref="ArgumentException">A value in the spec is not valid.</exception>
    /// <exception cref="FileNotFoundException">The spec's Containerfile does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">The spec's Containerfile context folder does
    /// not exist.</exception>
    public async Task<RustBuildImage> EnsureImageAsync(BuildImageSpec spec = null, IProgress<string> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var resolved = (spec ?? new BuildImageSpec()).WithDefaults(Options);
        resolved.ValidateContainerfile();
        DockerfileGenerator.Generate(resolved);
        var engine = await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        resolved = resolved.WithoutNativeLinuxTargets(engine);
        CrossTargetInfo.EnsureEngineCanBuild(engine, resolved.CrossTargets);
        var builder = new RustBuildImageBuilder(GetClient(), _labels);
        return await RunDockerAsync(
            $"Preparing the build image from {(resolved.UsesContainerfile ? resolved.Containerfile : resolved.BaseImageReference)}",
            token => builder.EnsureAsync(resolved, progress, token),
            Options.DefaultTimeouts.ImagePreparation,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Starts a build container from a prepared image (or, with
    /// <see cref="BuildContainerOptions.ReuseExisting"/>, finds the matching one created
    /// earlier). The container runs <c>sleep infinity</c> with <c>/work</c> as its working
    /// directory; cargo's registry and git caches are mounted from the workbench's cache volumes
    /// and <c>/work/target</c> from a target volume. Every container and volume is labelled.
    /// With <see cref="BuildContainerOptions.NetworkDisabled"/> the container has no network:
    /// run <c>cargo fetch</c> (<see cref="CargoCommand.Fetch"/>) in a networked container first,
    /// then build with <see cref="CargoBuildOptions.Offline"/> in the network-less one - the two
    /// share the workbench's cargo cache volumes.
    /// </summary>
    /// <param name="image">An image from <see cref="EnsureImageAsync"/>.</param>
    /// <param name="options">Container settings; <c>null</c> uses the defaults.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>The running container. Dispose it (or call
    /// <see cref="BuildContainer.StopAndRemoveAsync"/>) when done.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="image"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="BuildContainerOptions.AndroidApiLevel"/>
    /// is below 21.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="ContainerBuildException">The container could not be created or
    /// started.</exception>
    public async Task<BuildContainer> StartContainerAsync(RustBuildImage image, BuildContainerOptions options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (image == null)
        {
            throw new ArgumentNullException(nameof(image));
        }

        var resolved = (options ?? new BuildContainerOptions()).Clone();
        if (resolved.AndroidApiLevel < 21)
        {
            throw new ArgumentOutOfRangeException(nameof(options), resolved.AndroidApiLevel, "BuildContainerOptions.AndroidApiLevel must be 21 or later (64-bit Android targets start there).");
        }

        BuildContainerFactory.MergeEnvironment(resolved.Env);
        await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        var client = GetClient();
        var container = await RunDockerAsync(
            $"Starting a build container from {image.Reference}",
            token => BuildContainerFactory.StartAsync(client, Options, _labels, image, resolved, token),
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);
        container.AndroidApiLevel = resolved.AndroidApiLevel;
        return container;
    }

    /// <summary>
    /// Runs a command inside a build container (in <c>/work</c>, with the container's
    /// environment) and returns what it wrote to standard output. The
    /// <see cref="DockerRustTimeouts.Command"/> limit applies.
    /// </summary>
    /// <param name="container">A running container from <see cref="StartContainerAsync"/>.</param>
    /// <param name="command">The program and its arguments, for example
    /// <c>["cargo", "--version"]</c>. No shell is involved; use <c>["sh", "-c", "..."]</c> for
    /// shell syntax.</param>
    /// <param name="cancellationToken">Cancels the wait for the command. The process inside the
    /// container is not stopped by cancellation; removing the container stops it.</param>
    /// <returns>The command's standard output.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> or
    /// <paramref name="command"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="command"/> is empty.</exception>
    /// <exception cref="ContainerBuildException">The command exited with a non-zero code (the
    /// exception carries the exit code and both output streams), or could not be run.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    public async Task<string> ExecAsync(BuildContainer container, IReadOnlyList<string> command, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (container == null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        ContainerCommandRunner.ValidateCommand(command);
        if (container.IsRemoved)
        {
            throw new ContainerBuildException($"The build container {container} has been removed.");
        }

        var result = await RunDockerAsync(
            $"Running '{string.Join(" ", command)}' in {container}",
            token => ContainerCommandRunner.RunAsync(GetClient(), container.Id, command, BuildContainer.WorkDirectory, null, token),
            Options.DefaultTimeouts.Command,
            cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new ContainerBuildException(
                $"'{string.Join(" ", command)}' exited with code {result.ExitCode} in {container}.{Describe(result.StandardError)}",
                result.Command,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        return result.StandardOutput;
    }

    /// <summary>
    /// Builds a Rust project inside a build container in one call: checks the engine, prepares
    /// the image (adding the layers the targets need), starts (or reuses) the container, copies
    /// the source in (excluding <see cref="DockerRustWorkbenchOptions.DefaultExcludePatterns"/>
    /// and the request's own patterns), runs the license gate inside the container when
    /// <see cref="CargoBuildOptions.LicensePolicy"/> is set (one <c>cargo metadata
    /// --filter-platform</c> per build target; the audit covers the union of their graphs, and
    /// with <see cref="LicenseGateMode.ReportOnly"/> a failed audit is reported - "License
    /// audit failed (report only): N rejected, M unresolved" - and the build continues), runs
    /// <c>cargo build</c> for each target in order (estimating each target's
    /// <see cref="CargoProgress.Total"/> from its metadata unless
    /// <see cref="CargoBuildOptions.ExpectedCrateCount"/> is set), copies the built files out when
    /// <see cref="ContainerBuildRequest.Retrieval"/> is set, and removes the container unless
    /// it is kept.
    /// </summary>
    /// <param name="request">What to build and how.</param>
    /// <param name="progress">Receives a report at every stage change and every cargo
    /// progress report; may be <c>null</c>. <see cref="Progress"/> is raised as well.</param>
    /// <param name="cancellationToken">Cancels the build; a running cargo is killed inside the
    /// container. The container is removed (unless kept) before the exception
    /// propagates.</param>
    /// <returns>The result. A failed compilation or a failed enforcing license gate (with
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> false) is a result with
    /// <see cref="ContainerBuildResult.Success"/> false, not an exception. A failed
    /// <see cref="LicenseGateMode.ReportOnly"/> audit never fails the result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="request"/> is
    /// <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The request is not valid (no source or more than
    /// one, no targets, a target named twice, retrieval without a host folder, the Flat layout
    /// with several targets).</exception>
    /// <exception cref="CrossTargetNotSupportedException">A target is a macOS target, or an
    /// Android target on an engine that is not x86-64 (checked right after the engine check,
    /// before any image work).</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used (the exception
    /// carries the guidance).</exception>
    /// <exception cref="LicensePolicyViolationException">The license gate failed,
    /// <see cref="CargoBuildOptions.LicenseGateMode"/> is <see cref="LicenseGateMode.Enforce"/>
    /// and <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is true (the
    /// defaults).</exception>
    /// <exception cref="ContainerBuildException">An image, container or copy step failed, or
    /// <see cref="ContainerBuildRequest.Timeout"/> elapsed.</exception>
    /// <exception cref="ArtifactRetrievalException">A built file could not be copied
    /// out.</exception>
    /// <exception cref="SourceArchiveException">The archive could not be extracted or holds
    /// no Cargo project.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/>
    /// was cancelled.</exception>
    public Task<ContainerBuildResult> BuildAsync(ContainerBuildRequest request, IProgress<ContainerBuildProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var copy = request.Clone();
        copy.Validate();
        DockerfileGenerator.Generate(ContainerBuildPipeline.ImageSpecFor(copy).WithDefaults(Options));
        return new ContainerBuildPipeline(this, copy, progress).RunAsync(cancellationToken);
    }

    /// <summary>
    /// Copies the contents of a host project folder into the container's <c>/work</c>. What
    /// <c>/work</c> held before is removed first (except the <c>target</c> volume and anything
    /// mounted there), so files deleted on the host do not linger. Files and folders matching
    /// <see cref="DockerRustWorkbenchOptions.DefaultExcludePatterns"/> or
    /// <paramref name="extraExcludes"/> are skipped; a pattern without a slash (<c>target</c>,
    /// <c>*.log</c>) matches a name at any depth, one with a slash (<c>docs/build</c>) is
    /// relative to the project folder.
    /// </summary>
    /// <param name="container">A running container from <see cref="StartContainerAsync"/>.</param>
    /// <param name="hostDirectory">The project (or workspace) folder on the host.</param>
    /// <param name="extraExcludes">More patterns to skip; may be <c>null</c>.</param>
    /// <param name="progress">Receives one report per file sent; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What was copied.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is
    /// <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="hostDirectory"/> is blank.</exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">The host folder does not
    /// exist.</exception>
    /// <exception cref="ContainerBuildException">The container has been removed, or the copy
    /// failed.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    public async Task<SourceCopyResult> CopySourceAsync(BuildContainer container, string hostDirectory, IEnumerable<string> extraExcludes = null, IProgress<SourceCopyProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        if (string.IsNullOrWhiteSpace(hostDirectory))
        {
            throw new ArgumentException("A host folder is required.", nameof(hostDirectory));
        }

        var source = System.IO.Path.GetFullPath(hostDirectory);
        if (!System.IO.Directory.Exists(source))
        {
            throw new System.IO.DirectoryNotFoundException($"The project folder '{source}' does not exist.");
        }

        var patterns = (Options.DefaultExcludePatterns ?? new List<string>())
            .Concat(extraExcludes ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var client = GetClient();
        return await RunDockerAsync($"Copying '{source}' into {container}", async token =>
        {
            var clear = await ContainerCommandRunner.RunAsync(client, container.Id, ClearWorkCommand, "/", null, token).ConfigureAwait(false);
            if (!clear.Succeeded)
            {
                throw new ContainerBuildException(
                    $"The old contents of {BuildContainer.WorkDirectory} could not be removed in {container}.{Describe(clear.StandardError)}",
                    clear.Command,
                    clear.ExitCode,
                    clear.StandardOutput,
                    clear.StandardError);
            }

            return await new DockerCopyTransport(client, container.Id).CopyDirectoryInAsync(source, BuildContainer.WorkDirectory, patterns, progress, token).ConfigureAwait(false);
        }, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the contents of a host folder into a sub-folder of the container's <c>/work</c>
    /// (<c>/work/&lt;containerSubdirectory&gt;</c>), leaving the rest of <c>/work</c> as it is:
    /// only that sub-folder is emptied first. Use it to put two inputs - for example a tool and
    /// the project it reads - side by side in one container; <see cref="CopySourceAsync"/>
    /// replaces all of <c>/work</c>. Exclusion patterns work as in
    /// <see cref="CopySourceAsync"/>, relative to <paramref name="hostDirectory"/>. Run cargo for
    /// the copied project with <see cref="CargoBuildOptions.WorkingDirectory"/> set to the same
    /// sub-folder.
    /// </summary>
    /// <param name="container">A running container from <see cref="StartContainerAsync"/>.</param>
    /// <param name="hostDirectory">The folder on the host.</param>
    /// <param name="containerSubdirectory">The sub-folder below <c>/work</c>, relative
    /// (<c>tools/extractor</c>, <c>./staged</c>) or absolute below <c>/work</c>
    /// (<c>/work/staged</c>). It must not be <c>/work</c> itself, the target volume
    /// (<c>/work/target</c>) or anything inside it, and must not contain <c>..</c>.</param>
    /// <param name="extraExcludes">More patterns to skip; may be <c>null</c>.</param>
    /// <param name="progress">Receives one report per file sent; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What was copied (its <see cref="SourceCopyResult"/> names the container
    /// folder).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is
    /// <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="hostDirectory"/> is blank, or
    /// <paramref name="containerSubdirectory"/> is blank, not below <c>/work</c>, the target
    /// volume, or contains <c>..</c>.</exception>
    /// <exception cref="System.IO.DirectoryNotFoundException">The host folder does not
    /// exist.</exception>
    /// <exception cref="ContainerBuildException">The container has been removed, or the copy
    /// failed.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    public async Task<SourceCopyResult> CopySourceIntoAsync(BuildContainer container, string hostDirectory, string containerSubdirectory, IEnumerable<string> extraExcludes = null, IProgress<SourceCopyProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        if (string.IsNullOrWhiteSpace(hostDirectory))
        {
            throw new ArgumentException("A host folder is required.", nameof(hostDirectory));
        }

        var destination = ResolveCopySubdirectory(containerSubdirectory);
        var source = System.IO.Path.GetFullPath(hostDirectory);
        if (!System.IO.Directory.Exists(source))
        {
            throw new System.IO.DirectoryNotFoundException($"The folder '{source}' does not exist.");
        }

        var patterns = (Options.DefaultExcludePatterns ?? new List<string>())
            .Concat(extraExcludes ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var client = GetClient();
        return await RunDockerAsync($"Copying '{source}' into {destination} of {container}", async token =>
        {
            var clear = await ContainerCommandRunner.RunAsync(client, container.Id, ClearSubdirectoryCommand(destination), "/", null, token).ConfigureAwait(false);
            if (!clear.Succeeded)
            {
                throw new ContainerBuildException(
                    $"The old contents of {destination} could not be removed in {container}.{Describe(clear.StandardError)}",
                    clear.Command,
                    clear.ExitCode,
                    clear.StandardOutput,
                    clear.StandardError);
            }

            return await new DockerCopyTransport(client, container.Id).CopyDirectoryInAsync(source, destination, patterns, progress, token).ConfigureAwait(false);
        }, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The container folder of <see cref="CopySourceIntoAsync"/>: a folder strictly below
    /// <c>/work</c> that is not the target volume or inside it.
    /// </summary>
    /// <exception cref="ArgumentException">Blank, <c>/work</c> itself, outside <c>/work</c>,
    /// the target volume, or containing <c>..</c>.</exception>
    internal static string ResolveCopySubdirectory(string containerSubdirectory)
    {
        if (string.IsNullOrWhiteSpace(containerSubdirectory))
        {
            throw new ArgumentException("A container sub-folder is required; use CopySourceAsync to fill all of /work.", nameof(containerSubdirectory));
        }

        var resolved = ResolveContainerDirectory(containerSubdirectory);
        var work = BuildContainer.WorkDirectory;
        if (!resolved.StartsWith(work + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The container sub-folder '{containerSubdirectory}' must lie below {work} (use CopySourceAsync for {work} itself).", nameof(containerSubdirectory));
        }

        if (resolved == BuildContainer.TargetPath || resolved.StartsWith(BuildContainer.TargetPath + "/", StringComparison.Ordinal))
        {
            throw new ArgumentException($"The container sub-folder '{containerSubdirectory}' is the target volume ({BuildContainer.TargetPath}).", nameof(containerSubdirectory));
        }

        return resolved;
    }

    /// <summary>The shell command that empties (or creates) one folder below <c>/work</c>
    /// before <see cref="CopySourceIntoAsync"/>, leaving mount points inside it in
    /// place.</summary>
    internal static IReadOnlyList<string> ClearSubdirectoryCommand(string folder) => new[]
    {
        "sh", "-c",
        "d=\"$1\"; mkdir -p \"$d\" || exit 1; for f in \"$d\"/* \"$d\"/.[!.]* \"$d\"/..?*; do [ -e \"$f\" ] || [ -L \"$f\" ] || continue; "
        + "mountpoint -q \"$f\" 2>/dev/null && continue; rm -rf \"$f\" || exit 1; done",
        "codebrix-clear",
        folder,
    };

    /// <summary>
    /// Runs a cargo subcommand inside a build container with <c>--message-format=json</c> and
    /// returns the same result type the sibling package's <c>CargoRunner</c> returns (typed
    /// diagnostics, artifacts, progress, test counts), with CONTAINER paths in artifacts and
    /// diagnostics. cargo runs with <c>CARGO_TARGET_DIR=/work/target</c> (the target volume),
    /// <c>CARGO_TERM_COLOR=never</c> and <c>RUSTUP_AUTO_INSTALL=0</c>, plus
    /// <see cref="CargoBuildOptions.Environment"/> (entries with a <c>null</c> value are
    /// ignored). A <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c> entry there EXTENDS the rustflags
    /// the package itself sets for that target (Windows: <c>+crt-static</c>; macOS: the link
    /// profile's flags) - they come first, the entry's flags after - instead of replacing them;
    /// any other variable the package sets is replaced by an entry of the same name. For a compiling command (build, check, test, doc, clippy) ONE
    /// <c>cargo metadata --filter-platform TRIPLE</c> (the triple of <paramref name="target"/>)
    /// runs first, inside the container, when <see cref="CargoBuildOptions.LicensePolicy"/> is
    /// set or <see cref="CargoBuildOptions.ExpectedCrateCount"/> is not: it serves the license
    /// gate (exactly as on the host, <see cref="CargoBuildOptions.LicenseGateMode"/> included:
    /// <see cref="LicenseGateMode.ReportOnly"/> reports a failed audit as a
    /// <see cref="CargoProgressPhase.Warning"/> and builds anyway) and the
    /// <see cref="CargoProgress.Total"/> estimate (see
    /// <see cref="CargoMetadata.GetBuildClosure"/>). Without a policy the metadata call is
    /// made only when <paramref name="progress"/> is supplied (the total only feeds progress),
    /// and a failed <c>cargo metadata</c> then only leaves the total unknown.
    /// <see cref="CargoCommand.Fetch"/> downloads the dependencies into the container's cargo
    /// cache volume with no metadata call and no license gate.
    /// </summary>
    /// <param name="container">A running container whose <c>/work</c> holds the project (see
    /// <see cref="CopySourceAsync"/>).</param>
    /// <param name="command">The subcommand.</param>
    /// <param name="options">The cargo options; <c>null</c> uses the defaults.
    /// <see cref="CargoBuildOptions.WorkingDirectory"/> is a CONTAINER folder here: a relative
    /// path is taken below <c>/work</c> (for example a workspace member), an absolute one as it
    /// is; <c>null</c> means <c>/work</c>. <see cref="CargoBuildOptions.Timeout"/> defaults to
    /// <see cref="DockerRustTimeouts.Command"/>; on expiry the run is killed and the result has
    /// <see cref="CargoCommandResult.TimedOut"/> set.</param>
    /// <param name="target">The target to compile for. The cross targets need an image with
    /// their layer (see <see cref="BuildImageSpec.CrossTargets"/>); Android targets are linked
    /// with the NDK's clang for the container's <see cref="BuildContainer.AndroidApiLevel"/>
    /// (default 24).</param>
    /// <param name="progress">Receives cargo's progress; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills cargo and every process it started inside the
    /// container.</param>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is
    /// <c>null</c>.</exception>
    /// <exception cref="CrossTargetNotSupportedException"><paramref name="target"/> is a macOS
    /// target.</exception>
    /// <exception cref="LicensePolicyViolationException">The license gate failed,
    /// <see cref="CargoBuildOptions.LicenseGateMode"/> is <see cref="LicenseGateMode.Enforce"/>
    /// and <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is true.</exception>
    /// <exception cref="ContainerBuildException">The container has been removed, or cargo could
    /// not be started.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled
    /// and <see cref="CargoBuildOptions.ThrowOnCancellation"/> is true.</exception>
    public async Task<CargoCommandResult> RunCargoAsync(BuildContainer container, CargoCommand command, CargoBuildOptions options = null, CrossTarget target = CrossTarget.LinuxNative, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        target = CrossTargetInfo.Resolve(target, await EnsureEngineAsync(cancellationToken).ConfigureAwait(false));
        if (!CrossTargetInfo.Get(target).IsSupported)
        {
            throw new CrossTargetNotSupportedException(target);
        }

        if (CrossTargetInfo.Get(target).IsMacOSUniversal)
        {
            throw new ArgumentException("MacOSUniversal is not a cargo target: run cargo for MacOSArm64 and MacOSX64, then RetrieveMacOSUniversalArtifactsAsync; or ask BuildAsync for MacOSUniversal.", nameof(target));
        }

        var effective = options?.Clone() ?? new CargoBuildOptions();
        var directory = ResolveContainerDirectory(effective.WorkingDirectory);
        LicenseAuditReport audit = null;
        var wantEstimate = !effective.ExpectedCrateCount.HasValue;
        var fetchForEstimate = wantEstimate && (progress != null || HasProgressListeners);
        if ((effective.LicensePolicy != null || fetchForEstimate) && command is not (CargoCommand.Clean or CargoCommand.Fmt or CargoCommand.Fetch))
        {
            var metadataOptions = RustWorkbench.ToMetadataOptions(effective, CrateCountEstimator.FilterPlatformFor(CrossTargetInfo.Get(target).Triple));
            var metadata = effective.LicensePolicy != null
                ? await GetMetadataCoreAsync(container, directory + "/Cargo.toml", metadataOptions, cancellationToken).ConfigureAwait(false)
                : await TryGetMetadataCoreAsync(container, directory + "/Cargo.toml", metadataOptions, cancellationToken).ConfigureAwait(false);
            if (wantEstimate)
            {
                effective.ExpectedCrateCount = CrateCountEstimator.Estimate(metadata, command, effective);
            }

            if (effective.LicensePolicy != null)
            {
                audit = LicenseAuditor.Audit(metadata, effective.LicensePolicy);
                if (!audit.Passed)
                {
                    if (effective.LicenseGateMode == LicenseGateMode.ReportOnly)
                    {
                        var warning = new CargoProgress
                        {
                            Phase = CargoProgressPhase.Warning,
                            Total = effective.ExpectedCrateCount,
                            Message = RustWorkbench.ReportOnlyAuditMessage(audit),
                        };
                        try
                        {
                            progress?.Report(warning);
                        }
                        catch (Exception)
                        {
                            // A consumer's handler must not break the cargo run.
                        }

                        Publish(new ContainerBuildProgress { Stage = BuildStage.AuditingLicenses, Message = warning.Message }, null);
                    }
                    else if (effective.ThrowOnLicenseViolation)
                    {
                        throw new LicensePolicyViolationException(
                            $"cargo {CargoRunner.SubcommandName(command)} was not run in {container} because the license audit of '{directory}/Cargo.toml' failed: {audit.RejectedCount} rejected and {audit.UnresolvedCount} unresolved crate(s).",
                            audit);
                    }
                    else
                    {
                        return CargoCommandResult.ForFailedLicenseGate(command, audit, directory);
                    }
                }
            }
        }

        var result = await RunCargoCoreAsync(container, command, effective, target, directory, report =>
        {
            try
            {
                progress?.Report(report);
            }
            catch (Exception)
            {
                // A consumer's handler must not break the cargo run.
            }

            Publish(new ContainerBuildProgress { Stage = BuildStage.Building, Message = report.Message ?? string.Empty, CargoProgress = report }, null);
        }, cancellationToken).ConfigureAwait(false);
        result.LicenseAudit = audit;
        return result;
    }

    /// <summary>
    /// Runs <c>cargo test</c> inside a build container, for the container's own target (test
    /// binaries for another platform cannot run there). See <see cref="RunCargoAsync"/> for the
    /// options, the license gate and the exceptions.
    /// </summary>
    /// <param name="container">A running container whose <c>/work</c> holds the project.</param>
    /// <param name="options">The cargo options (<see cref="CargoBuildOptions.NoFailFast"/>,
    /// <see cref="CargoBuildOptions.TestNameFilter"/>); <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives cargo's progress; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the run inside the container.</param>
    /// <returns>The result with the test counts.</returns>
    public async Task<CargoTestResult> TestAsync(BuildContainer container, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => (CargoTestResult)await RunCargoAsync(container, CargoCommand.Test, options, CrossTarget.LinuxNative, progress, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs <c>cargo metadata --format-version 1</c> for <c>/work/Cargo.toml</c> inside a build
    /// container and parses it into the sibling package's <see cref="CargoMetadata"/>. Every
    /// path in it (manifest paths, workspace root, target folder) is a CONTAINER path, for
    /// example <c>/work/Cargo.toml</c>.
    /// </summary>
    /// <param name="container">A running container whose <c>/work</c> holds the project.</param>
    /// <param name="options">The metadata options (<c>--offline</c>, <c>--locked</c>, features,
    /// ...); <c>null</c> uses the defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ContainerBuildException">cargo metadata failed (for example a
    /// dependency could not be fetched) or the container has been removed.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    public Task<CargoMetadata> GetMetadataAsync(BuildContainer container, CargoMetadataOptions options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        return GetMetadataCoreAsync(container, BuildContainer.WorkDirectory + "/Cargo.toml", options, cancellationToken);
    }

    /// <summary>
    /// Audits the licenses of every crate in the resolved dependency graph of
    /// <c>/work/Cargo.toml</c> against a policy, with <c>cargo metadata</c> run inside the
    /// container (the host needs no Rust tools). The report is returned whether the audit passed
    /// or not.
    /// </summary>
    /// <param name="container">A running container whose <c>/work</c> holds the project.</param>
    /// <param name="policy">The license policy.</param>
    /// <param name="metadataOptions">Options for <c>cargo metadata</c>; <c>null</c> uses the
    /// defaults.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> or
    /// <paramref name="policy"/> is <c>null</c>.</exception>
    /// <exception cref="ContainerBuildException">cargo metadata failed.</exception>
    public async Task<LicenseAuditReport> AuditLicensesAsync(BuildContainer container, LicensePolicy policy, CargoMetadataOptions metadataOptions = null, CancellationToken cancellationToken = default)
    {
        if (policy == null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        var metadata = await GetMetadataAsync(container, metadataOptions, cancellationToken).ConfigureAwait(false);
        return LicenseAuditor.Audit(metadata, policy);
    }

    /// <summary>
    /// Copies built files out of a build container: the executables and libraries of the
    /// artifacts (from <see cref="CargoCommandResult.Artifacts"/>, whose paths are container
    /// paths) selected by the options, arranged in the options' layout, each with its size,
    /// SHA-256 and execute bit. Build scripts, test harnesses and <c>.rmeta</c> files are never
    /// copied. On Linux and macOS hosts the container's permission bits are applied, so a
    /// retrieved program is executable.
    /// </summary>
    /// <param name="container">The container that built the artifacts.</param>
    /// <param name="artifacts">The artifacts, normally a result's
    /// <see cref="CargoCommandResult.Artifacts"/>.</param>
    /// <param name="options">What to copy and where; <see cref="ArtifactRetrievalOptions.HostDirectory"/>
    /// is required.</param>
    /// <param name="target">The target the artifacts were built for (it names the layout's
    /// folder); <see cref="CrossTarget.LinuxNative"/> is resolved to the engine's own Linux
    /// target first, so an ARM64 engine's native output lands in
    /// <c>aarch64-unknown-linux-gnu</c> / <c>runtimes/linux-arm64/native</c>.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The options have no host folder.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="ArtifactRetrievalException">A file does not exist in the container,
    /// already exists on the host with overwriting off, or cannot be written.</exception>
    public async Task<ArtifactRetrievalResult> RetrieveArtifactsAsync(BuildContainer container, IEnumerable<BuildArtifact> artifacts, ArtifactRetrievalOptions options, CrossTarget target = CrossTarget.LinuxNative, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        if (artifacts == null)
        {
            throw new ArgumentNullException(nameof(artifacts));
        }

        ArtifactRetriever.Validate(options);
        var engine = await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        target = CrossTargetInfo.Resolve(target, engine);
        var transport = new DockerCopyTransport(GetClient(), container.Id);
        return await RunDockerAsync(
            $"Copying build output out of {container}",
            token => ArtifactRetriever.RetrieveAsync(transport, artifacts, options, target, engine, token),
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Joins the <see cref="CrossTarget.MacOSArm64"/> and <see cref="CrossTarget.MacOSX64"/>
    /// builds of one project into universal files on the host: every program and dynamic library
    /// that both builds produced becomes one file (see <see cref="MacOSUniversalBinary"/>) in the
    /// layout folder of <see cref="CrossTarget.MacOSUniversal"/> (<c>universal-apple-darwin</c>,
    /// <c>runtimes/osx/native</c>, or the host folder itself for the flat layout). The slices are
    /// copied out to a temporary folder and deleted afterwards; the artifacts' kinds, options
    /// (executables, shared libraries, ...) and overwrite rule apply as in
    /// <see cref="RetrieveArtifactsAsync"/>.
    /// </summary>
    /// <param name="container">The container that built both.</param>
    /// <param name="arm64Artifacts">The artifacts of the <see cref="CrossTarget.MacOSArm64"/> build.</param>
    /// <param name="x64Artifacts">The artifacts of the <see cref="CrossTarget.MacOSX64"/> build.</param>
    /// <param name="options">What to copy and where; <see cref="ArtifactRetrievalOptions.HostDirectory"/>
    /// is required.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The universal files, each with <see cref="RetrievedArtifact.Target"/> =
    /// <see cref="CrossTarget.MacOSUniversal"/> and the arm64 slice as its container path.</returns>
    /// <exception cref="ArgumentNullException">An argument is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">The options have no host folder.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="ArtifactRetrievalException">A file does not exist in the container,
    /// already exists on the host with overwriting off, or cannot be written.</exception>
    public async Task<ArtifactRetrievalResult> RetrieveMacOSUniversalArtifactsAsync(BuildContainer container, IEnumerable<BuildArtifact> arm64Artifacts, IEnumerable<BuildArtifact> x64Artifacts, ArtifactRetrievalOptions options, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        if (arm64Artifacts == null)
        {
            throw new ArgumentNullException(nameof(arm64Artifacts));
        }

        if (x64Artifacts == null)
        {
            throw new ArgumentNullException(nameof(x64Artifacts));
        }

        ArtifactRetriever.Validate(options);
        await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        var transport = new DockerCopyTransport(GetClient(), container.Id);
        return await RunDockerAsync(
            $"Copying macOS slices out of {container} and joining them",
            token => ArtifactRetriever.RetrieveUniversalAsync(transport, arm64Artifacts, x64Artifacts, options, token),
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Copies the macOS sysroot the image's macOS layer generated
    /// (<see cref="DockerfileGenerator.MacOSSysrootDirectory"/>: the libSystem symbol stub, its
    /// aliases, the symbol lists and a README) out of a container into a host folder, for a
    /// <see cref="MacOSLinkProfile"/> on the host. A Mac that builds with the same Rust release,
    /// this stub and rustup's <c>rust-lld</c> produces byte-for-byte the files the container
    /// produces.
    /// </summary>
    /// <param name="container">A container whose image has the macOS layer (a spec with a
    /// macOS target).</param>
    /// <param name="hostDirectory">The folder that receives the sysroot's contents (created
    /// when missing; existing files are replaced). It becomes the profile's sysroot folder:
    /// <c>&lt;hostDirectory&gt;/usr/lib/libSystem.tbd</c> is the stub.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The full path of <paramref name="hostDirectory"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="container"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="hostDirectory"/> is blank.</exception>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    /// <exception cref="ContainerBuildException">The container has no macOS sysroot (its image
    /// was built without a macOS target), or the copy failed.</exception>
    public async Task<string> ExportMacOSSysrootAsync(BuildContainer container, string hostDirectory, CancellationToken cancellationToken = default)
    {
        ThrowIfUnusable(container);
        if (string.IsNullOrWhiteSpace(hostDirectory))
        {
            throw new ArgumentException("The host folder is required.", nameof(hostDirectory));
        }

        await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        var destination = Path.GetFullPath(hostDirectory);
        var transport = new DockerCopyTransport(GetClient(), container.Id);
        return await RunDockerAsync(
            $"Copying the macOS sysroot out of {container}",
            async token =>
            {
                var stat = await transport.StatAsync(DockerfileGenerator.MacOSSysrootDirectory, token).ConfigureAwait(false);
                if (!stat.Exists)
                {
                    throw new ContainerBuildException(
                        $"{container} has no macOS sysroot at {DockerfileGenerator.MacOSSysrootDirectory}: its image was built without a macOS target. "
                        + "Add CrossTarget.MacOSArm64, MacOSX64 or MacOSUniversal to BuildImageSpec.CrossTargets.");
                }

                await transport.CopyOutAsync(new[] { DockerfileGenerator.MacOSSysrootDirectory }, destination, overwrite: true, token).ConfigureAwait(false);
                var stub = Path.Combine(destination, MacOSLinkProfile.StubRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(stub))
                {
                    throw new ContainerBuildException($"The macOS sysroot was copied to '{destination}' but holds no '{MacOSLinkProfile.StubRelativePath}'.");
                }

                return destination;
            },
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);
    }

    internal async Task<CargoCommandResult> RunCargoCoreAsync(BuildContainer container, CargoCommand command, CargoBuildOptions options, CrossTarget target, string containerDirectory, Action<CargoProgress> report, CancellationToken cancellationToken)
    {
        var engine = await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        var invocation = ContainerCargoRunner.CreateInvocation(command, options, target, containerDirectory, engine.NativeLinuxTriple, container.AndroidApiLevel);
        var client = GetClient();
        return await RunDockerAsync(
            $"Running '{invocation.DisplayCommandLine}' in {container}",
            token => ContainerCargoRunner.RunAsync(client, container.Id, invocation, options, Options.DefaultTimeouts.Command, report, token),
            Timeout.InfiniteTimeSpan,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>cargo metadata</c> inside the container for the crate-count estimate only (no
    /// license gate; made only when the caller reports progress): a failure leaves the total
    /// unknown instead of failing the build (cargo reports the real problem itself).
    /// Cancellation still throws.
    /// </summary>
    internal async Task<CargoMetadata> TryGetMetadataCoreAsync(BuildContainer container, string containerManifestPath, CargoMetadataOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await GetMetadataCoreAsync(container, containerManifestPath, options, cancellationToken).ConfigureAwait(false);
        }
        catch (RustToolsException ex) when (ex is not DockerUnavailableException)
        {
            return null;
        }
    }

    internal Task<CargoMetadata> GetMetadataCoreAsync(BuildContainer container, string containerManifestPath, CargoMetadataOptions options, CancellationToken cancellationToken)
    {
        var client = GetClient();
        return RunDockerAsync(
            $"Running 'cargo metadata' in {container}",
            token => ContainerMetadataRunner.RunAsync(client, container.Id, containerManifestPath, options, Options.DefaultTimeouts.Command, token),
            Timeout.InfiniteTimeSpan,
            cancellationToken);
    }

    /// <summary>
    /// The container folder for <see cref="CargoBuildOptions.WorkingDirectory"/>:
    /// <c>/work</c> when unset, the path itself when absolute, otherwise below
    /// <c>/work</c> (only <c>.</c> segments such as a leading <c>./</c> are dropped, so a hidden
    /// folder such as <c>.codebrix-facade/x</c> keeps its dot).
    /// </summary>
    internal static string ResolveContainerDirectory(string workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            return BuildContainer.WorkDirectory;
        }

        var path = workingDirectory.Trim().Replace('\\', '/');
        if (path.Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException($"The container working directory '{workingDirectory}' must not contain '..'.", nameof(workingDirectory));
        }

        if (path.StartsWith("/", StringComparison.Ordinal))
        {
            return path.Length > 1 ? path.TrimEnd('/') : path;
        }

        // Only "./" segments are dropped: the leading dot of a hidden folder (".cache/x") stays.
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(segment => segment != ".").ToList();
        path = string.Join("/", segments);
        return path.Length == 0 ? BuildContainer.WorkDirectory : BuildContainer.WorkDirectory + "/" + path;
    }

    /// <summary>
    /// Raises <see cref="Progress"/> and reports to <paramref name="progress"/>, ignoring
    /// exceptions from either.
    /// </summary>
    internal void Publish(ContainerBuildProgress report, IProgress<ContainerBuildProgress> progress)
    {
        try
        {
            Progress?.Invoke(this, report);
        }
        catch (Exception)
        {
            // A consumer's handler must not break the build.
        }

        try
        {
            progress?.Report(report);
        }
        catch (Exception)
        {
            // Same as above.
        }
    }

    /// <summary>
    /// The shell command that empties <c>/work</c> before a copy, leaving the target volume and
    /// any other mount point in place.
    /// </summary>
    internal static readonly IReadOnlyList<string> ClearWorkCommand = new[]
    {
        "sh", "-c",
        "for f in /work/* /work/.[!.]* /work/..?*; do [ -e \"$f\" ] || [ -L \"$f\" ] || continue; "
        + "[ \"$f\" = /work/target ] && continue; mountpoint -q \"$f\" 2>/dev/null && continue; rm -rf \"$f\" || exit 1; done",
    };

    private void ThrowIfUnusable(BuildContainer container)
    {
        ThrowIfDisposed();
        if (container == null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        if (container.IsRemoved)
        {
            throw new ContainerBuildException($"The build container {container} has been removed.");
        }
    }

    /// <summary>
    /// Removes the containers, derived images and volumes that carry the workbench's labels, as
    /// selected by <paramref name="options"/>. Containers are removed first (forcefully), so
    /// their volumes are free to go. The base image is never touched. With
    /// <see cref="CleanupOptions.PruneBuildCache"/> it then runs <c>docker builder prune
    /// --all --force</c>, which is ENGINE-WIDE: it removes every unused BuildKit build-cache entry, not
    /// only this library's (removing images does not reclaim that cache).
    /// </summary>
    /// <param name="options">What to remove; <c>null</c> removes containers only.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    /// <returns>What was removed and what could not be.</returns>
    /// <exception cref="DockerUnavailableException">The engine cannot be used.</exception>
    public async Task<CleanupResult> CleanupAsync(CleanupOptions options = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        var settings = options ?? new CleanupOptions();
        await EnsureEngineAsync(cancellationToken).ConfigureAwait(false);
        var client = GetClient();
        var filter = settings.OnlyThisWorkbench ? _labels.WorkbenchFilter() : _labels.PresenceFilter();
        var containers = new List<string>();
        var images = new List<string>();
        var volumes = new List<string>();
        var failures = new List<string>();

        await RunDockerAsync("Cleaning up", async token =>
        {
            if (settings.RemoveContainers)
            {
                foreach (var container in await client.Containers.ListAsync(all: true, filter, token).ConfigureAwait(false))
                {
                    try
                    {
                        await client.Containers.RemoveAsync(container.Id, force: true, removeVolumes: false, token).ConfigureAwait(false);
                        containers.Add(container.Id);
                    }
                    catch (DockerContainerNotFoundException)
                    {
                        // Removed concurrently.
                    }
                    catch (DockerApiException ex)
                    {
                        failures.Add($"Container {container.DisplayName}: {ex.Message}");
                    }
                }
            }

            if (settings.RemoveImages)
            {
                foreach (var image in await client.Images.ListAsync(all: false, filter, token).ConfigureAwait(false))
                {
                    try
                    {
                        await client.Images.RemoveAsync(image.Id, force: true, token).ConfigureAwait(false);
                        images.Add(image.Id);
                    }
                    catch (DockerImageNotFoundException)
                    {
                        // Removed concurrently.
                    }
                    catch (DockerApiException ex)
                    {
                        failures.Add($"Image {image.DisplayName}: {ex.Message}");
                    }
                }
            }

            if (settings.RemoveVolumes)
            {
                foreach (var volume in await client.Volumes.ListAsync(filter, token).ConfigureAwait(false))
                {
                    try
                    {
                        await client.Volumes.RemoveAsync(volume.Name, force: false, token).ConfigureAwait(false);
                        volumes.Add(volume.Name);
                    }
                    catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Removed concurrently.
                    }
                    catch (DockerApiException ex)
                    {
                        failures.Add($"Volume {volume.Name}: {ex.Message}");
                    }
                }
            }

            return true;
        }, Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);

        BuildCachePruneResult prune = null;
        if (settings.PruneBuildCache)
        {
            try
            {
                prune = await Client.Images.PruneBuildCacheAsync(new BuildCachePruneOptions { All = true }, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerCliException ex)
            {
                failures.Add("Build cache: " + ex.Message);
            }
        }

        return new CleanupResult
        {
            RemovedContainers = containers,
            RemovedImages = images,
            RemovedVolumes = volumes,
            Failures = failures,
            BuildCachePruned = prune != null,
            BuildCacheReclaimedSpace = prune?.ReclaimedSpaceText,
            BuildCacheReclaimedBytes = prune?.ReclaimedBytes,
            BuildCachePruneOutput = prune?.Output,
        };
    }

    /// <summary>
    /// Releases the Docker client. Containers the workbench started are not removed; dispose
    /// them first, or use <see cref="CleanupAsync"/>.
    /// </summary>
    public void Dispose()
    {
        lock (_clientLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _client?.Dispose();
            _client = null;
        }
    }

    private DockerClient GetClient()
    {
        lock (_clientLock)
        {
            ThrowIfDisposed();
            if (_client != null)
            {
                return _client;
            }

            var clientOptions = Options.CreateClientOptions();
            try
            {
                _client = DockerClient.Create(clientOptions);
            }
            catch (Exception ex) when (ex is NotSupportedException || ex is DockerException || ex is ArgumentException || ex is UriFormatException)
            {
                var info = new DockerEngineInfo
                {
                    IsAvailable = false,
                    Endpoint = clientOptions.Endpoint ?? Environment.GetEnvironmentVariable("DOCKER_HOST"),
                    HostOperatingSystem = CodeBrix.RustTools.Toolchain.HostOperatingSystem.Detect(),
                    DockerCliFound = DockerAvailability.IsCliOnPath(clientOptions.DockerCliPath),
                    Issues = new[] { DockerEngineIssue.EndpointNotSupported },
                    ErrorMessage = ex.Message,
                };
                throw new DockerUnavailableException(
                    "The Docker endpoint cannot be used: " + ex.Message,
                    info,
                    DockerGuidanceProvider.ForEngine(info),
                    ex);
            }

            return _client;
        }
    }

    private async Task<DockerEngineInfo> EnsureEngineAsync(CancellationToken cancellationToken)
    {
        var cached = _engineInfo;
        if (_engineVerified && cached != null)
        {
            return cached;
        }

        var info = await CheckEngineAsync(cancellationToken).ConfigureAwait(false);
        if (!info.IsAvailable)
        {
            throw Unavailable(info, null);
        }

        return info;
    }

    internal DockerUnavailableException Unavailable(DockerEngineInfo info, Exception inner)
    {
        var reason = info.Issues.Contains(DockerEngineIssue.NotLinuxDaemon)
            ? "The Docker engine runs Windows containers; Rust builds need Linux containers."
            : info.Issues.Contains(DockerEngineIssue.PermissionDenied)
                ? "Access to the Docker engine was refused."
                : "The Docker engine is not reachable.";
        var message = string.IsNullOrEmpty(info.ErrorMessage) ? reason : reason + " " + info.ErrorMessage;
        return new DockerUnavailableException(message, info, DockerGuidanceProvider.ForEngine(info), inner);
    }

    internal async Task<T> RunDockerAsync<T>(string what, Func<CancellationToken, Task<T>> action, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = timeout > TimeSpan.Zero && timeout != Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource(timeout)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        try
        {
            return await action(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ContainerBuildException($"{what} did not finish within {timeout}.", new TimeoutException(ex.Message, ex));
        }
        catch (DockerRustToolsException)
        {
            throw;
        }
        catch (DockerCliException ex)
        {
            throw new ContainerBuildException($"{what} failed: {ex.Message}{Describe(ex.StdErr)}", ex);
        }
        catch (DockerApiException ex)
        {
            throw new ContainerBuildException($"{what} failed: {ex.Message}", ex);
        }
        catch (DockerException ex)
        {
            _engineVerified = false;
            var info = await DockerAvailability.CheckAsync(GetClient(), Options.CreateClientOptions().DockerCliPath, cancellationToken).ConfigureAwait(false);
            if (!info.IsAvailable)
            {
                throw Unavailable(info, ex);
            }

            throw new ContainerBuildException($"{what} failed: {ex.Message}", ex);
        }
    }

    private static string Describe(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return string.Empty;
        }

        var lines = standardError.TrimEnd().Split('\n');
        var tail = lines.Skip(Math.Max(0, lines.Length - 20)).Select(line => line.TrimEnd('\r'));
        return Environment.NewLine + string.Join(Environment.NewLine, tail);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
