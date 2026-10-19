using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Archives;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Dependencies;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Running;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools;

/// <summary>
/// The entry point of CodeBrix.RustTools: detects the Rust toolchain installed on this machine,
/// decides whether it can build, explains how to fix it when it cannot, checks for newer Rust
/// releases, opens Cargo projects, audits their dependency licenses, runs cargo (build, check,
/// test, clean, doc, clippy, fmt, fetch) with typed diagnostics and progress, inspects dependency
/// health, extracts and builds source archives, and runs the programs it built. The library
/// uses the tools that are already installed and never installs or updates anything itself.
/// </summary>
public sealed class RustWorkbench
{
    private readonly RustToolchainDetector _detector;
    private readonly RustReleaseClient _releases;
    private readonly CargoRunner _runner;
    private readonly BuiltBinaryRunner _binaryRunner;
    private readonly Lazy<CratesIoClient> _cratesIo;
    private readonly Func<string, CancellationToken, Task<RustToolchainInfo>> _detect;
    private readonly SemaphoreSlim _detectionGate = new SemaphoreSlim(1, 1);
    private readonly object _cacheLock = new object();
    private readonly Dictionary<string, ToolchainCacheEntry> _toolchainCache = new Dictionary<string, ToolchainCacheEntry>(StringComparer.Ordinal);
    private long _cacheGeneration;

    /// <summary>
    /// Creates a workbench.
    /// </summary>
    /// <param name="options">Tool paths, environment, timeouts and HTTP settings; <c>null</c>
    /// uses the defaults (tools found on PATH or in the cargo bin folder).</param>
    public RustWorkbench(RustWorkbenchOptions options = null)
        : this(options, null)
    {
    }

    /// <summary>
    /// Creates a workbench whose toolchain detection is replaced by <paramref name="detect"/>
    /// (tests use it to feed synthetic environments); <c>null</c> uses the real detector.
    /// </summary>
    internal RustWorkbench(RustWorkbenchOptions options, Func<string, CancellationToken, Task<RustToolchainInfo>> detect)
    {
        Options = options ?? new RustWorkbenchOptions();
        _detector = new RustToolchainDetector(Options);
        _detect = detect ?? _detector.DetectAsync;
        _releases = new RustReleaseClient(Options);
        _runner = new CargoRunner(Options);
        _runner.Progress += (_, report) => OnProgress(report);
        _binaryRunner = new BuiltBinaryRunner(Options);
        _cratesIo = new Lazy<CratesIoClient>(() => new CratesIoClient(Options));
    }

    /// <summary>
    /// Raised for every progress report of every cargo command this workbench runs (in
    /// addition to the per-call <see cref="IProgress{T}"/>). Raised on a thread-pool thread;
    /// exceptions thrown by handlers are ignored.
    /// </summary>
    public event EventHandler<CargoProgress> Progress;

    /// <summary>
    /// The settings this workbench was created with. Changes made after construction are seen
    /// by later calls that read them (paths, environment, timeouts), but not by the HTTP client
    /// selection, which is fixed at construction.
    /// </summary>
    public RustWorkbenchOptions Options { get; }

    /// <summary>
    /// The clock used to age cached toolchain detections (tests replace it).
    /// </summary>
    internal Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    /// <summary>
    /// Detects rustup, cargo, rustc, clippy, rustfmt, rustdoc and the C toolchain, plus the
    /// active toolchain, installed targets and components and the project's
    /// <c>rust-toolchain.toml</c>.
    /// </summary>
    /// <remarks>
    /// Detection runs several processes, so this workbench caches the result. The cache is keyed
    /// by the project folder together with the path and content of the
    /// <c>rust-toolchain.toml</c> (or <c>rust-toolchain</c>) that applies to it, so a different
    /// folder, or an edited toolchain file, is detected afresh. A cached result is served until
    /// <see cref="RustWorkbenchOptions.ToolchainCacheDuration"/> has passed (by default it never
    /// expires), until <see cref="InvalidateToolchainCache"/> is called, or until
    /// <see cref="RefreshToolchainAsync"/> replaces it. Install or update a tool while the
    /// workbench is alive and call <see cref="RefreshToolchainAsync"/> (or
    /// <see cref="InvalidateToolchainCache"/>) to see the change. The compiling cargo verbs use
    /// the same cache for their readiness check. Concurrent calls are safe; detections run one
    /// at a time.
    /// </remarks>
    /// <param name="projectDirectory">A project folder to probe from (so its toolchain override
    /// applies), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels detection; running probes are killed.</param>
    /// <returns>The detected environment (possibly from the cache); missing tools are
    /// reported, not thrown.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException"><paramref name="projectDirectory"/>
    /// does not exist.</exception>
    public Task<RustToolchainInfo> DetectToolchainAsync(string projectDirectory = null, CancellationToken cancellationToken = default)
        => GetToolchainAsync(projectDirectory, forceRefresh: false, cancellationToken);

    /// <summary>
    /// Detects the toolchain again, ignoring any cached result, and caches the new result for
    /// <paramref name="projectDirectory"/>. Call it after installing or updating a tool.
    /// </summary>
    /// <param name="projectDirectory">A project folder to probe from (so its toolchain override
    /// applies), or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels detection; running probes are killed. A
    /// cancelled refresh leaves the cache unchanged.</param>
    /// <returns>The freshly detected environment.</returns>
    /// <exception cref="System.IO.DirectoryNotFoundException"><paramref name="projectDirectory"/>
    /// does not exist.</exception>
    public Task<RustToolchainInfo> RefreshToolchainAsync(string projectDirectory = null, CancellationToken cancellationToken = default)
        => GetToolchainAsync(projectDirectory, forceRefresh: true, cancellationToken);

    /// <summary>
    /// Forgets every cached toolchain detection, so the next
    /// <see cref="DetectToolchainAsync"/> or compiling cargo verb detects again. A detection
    /// already running when this is called is returned to its caller but not cached.
    /// </summary>
    public void InvalidateToolchainCache()
    {
        lock (_cacheLock)
        {
            _toolchainCache.Clear();
            _cacheGeneration++;
        }
    }

    /// <summary>
    /// Checks a detected environment against build requirements.
    /// </summary>
    /// <param name="info">The result of <see cref="DetectToolchainAsync"/>.</param>
    /// <param name="requirements">What the build needs, or <c>null</c> for
    /// <see cref="ToolchainRequirements.Default"/> (cargo, rustc and a C toolchain).</param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is null.</exception>
    public ToolchainReadiness EvaluateReadiness(RustToolchainInfo info, ToolchainRequirements requirements = null)
        => ToolchainReadiness.Evaluate(info, requirements);

    /// <summary>
    /// Returns the operating-system-specific instructions that resolve every blocking problem in
    /// <paramref name="readiness"/>; empty when it is ready. Nothing is executed.
    /// </summary>
    /// <param name="readiness">The result of <see cref="EvaluateReadiness"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="readiness"/> is null.</exception>
    public IReadOnlyList<InstallGuidance> GetInstallGuidance(ToolchainReadiness readiness)
        => InstallGuidanceProvider.ForReadiness(readiness);

    /// <summary>
    /// Compares the installed rustc, cargo, rustup, clippy and rustfmt with the latest stable
    /// Rust release and the latest rustup release (two small downloads from
    /// static.rust-lang.org). A download failure is recorded in
    /// <see cref="ToolUpdateReport.Errors"/> rather than thrown.
    /// </summary>
    /// <param name="info">The result of <see cref="DetectToolchainAsync"/>.</param>
    /// <param name="cancellationToken">Cancels the downloads.</param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is null.</exception>
    public async Task<ToolUpdateReport> CheckForToolUpdatesAsync(RustToolchainInfo info, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(info);
        var errors = new List<string>();

        var releaseTask = _releases.GetLatestStableReleaseAsync(cancellationToken);
        var rustupTask = _releases.GetLatestRustupVersionAsync(cancellationToken);

        RustReleaseInfo release = null;
        ToolVersion rustup = null;
        try
        {
            release = await releaseTask.ConfigureAwait(false);
        }
        catch (RustToolsException ex)
        {
            errors.Add("Latest stable release: " + ex.Message);
        }

        try
        {
            rustup = await rustupTask.ConfigureAwait(false);
        }
        catch (RustToolsException ex)
        {
            errors.Add("Latest rustup release: " + ex.Message);
        }

        return ToolUpdateReport.Create(info, release, rustup, errors);
    }

    /// <summary>
    /// Opens a Cargo project (reads Cargo.toml and Cargo.lock; nothing is run). The project
    /// uses this workbench's <see cref="Options"/> when it runs cargo.
    /// </summary>
    /// <param name="pathToCargoTomlOrDirectory">A Cargo.toml path, or a folder that contains
    /// Cargo.toml.</param>
    /// <exception cref="ArgumentException">The path is blank.</exception>
    /// <exception cref="System.IO.FileNotFoundException">There is no Cargo.toml at the
    /// path.</exception>
    /// <exception cref="RustToolsException">Cargo.toml (or Cargo.lock) is not valid TOML.</exception>
    public CargoProject OpenProject(string pathToCargoTomlOrDirectory)
        => CargoProject.Open(pathToCargoTomlOrDirectory, Options);

    /// <summary>
    /// Runs <c>cargo metadata</c> for the project and checks the license of every crate in the
    /// resolved graph against <paramref name="policy"/>. The report is returned whether it
    /// passed or not; call <see cref="LicenseAuditReport.ThrowIfFailed"/> to turn a failure into
    /// an exception.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="policy">The allowed licenses and how to treat unresolved ones.</param>
    /// <param name="cancellationToken">Cancels the cargo run (the process tree is killed).</param>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> or
    /// <paramref name="policy"/> is null.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException"><c>cargo metadata</c> failed (for example it
    /// needed the network and could not reach it).</exception>
    public Task<LicenseAuditReport> AuditLicensesAsync(CargoProject project, LicensePolicy policy, CancellationToken cancellationToken = default)
        => LicenseAuditor.AuditAsync(project, policy, null, cancellationToken);

    /// <summary>
    /// Runs <c>cargo metadata</c> with the given options (for example
    /// <see cref="CargoMetadataOptions.Offline"/> or a feature selection) and audits the
    /// licenses of the resolved graph. See
    /// <see cref="AuditLicensesAsync(CargoProject, LicensePolicy, CancellationToken)"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="policy">The allowed licenses and how to treat unresolved ones.</param>
    /// <param name="metadataOptions">Options for <c>cargo metadata</c>, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the cargo run (the process tree is killed).</param>
    /// <exception cref="ArgumentException"><paramref name="metadataOptions"/> sets
    /// <see cref="CargoMetadataOptions.NoDependencies"/>.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException"><c>cargo metadata</c> failed.</exception>
    public Task<LicenseAuditReport> AuditLicensesAsync(CargoProject project, LicensePolicy policy, CargoMetadataOptions metadataOptions, CancellationToken cancellationToken = default)
        => LicenseAuditor.AuditAsync(project, policy, metadataOptions, cancellationToken);

    /// <summary>
    /// Runs a cargo subcommand on a project. For every compiling command (build, check, test,
    /// doc, clippy) the workbench first (1) detects the toolchain from the project folder (served
    /// from the per-workbench cache, see <see cref="DetectToolchainAsync"/>), evaluates it
    /// against <see cref="ToolchainRequirements.Default"/> and throws
    /// <see cref="ToolchainNotFoundException"/> (with install guidance) when cargo, rustc or the
    /// C compiler / linker is missing - a C toolchain whose status could not be determined is a
    /// non-blocking <see cref="ReadinessIssueKind.ToolStatusUnknown"/> issue in
    /// <see cref="CargoCommandResult.Readiness"/> and does not stop the run - then (2) runs ONE
    /// <c>cargo metadata --filter-platform TRIPLE</c> (the build's
    /// <see cref="CargoBuildOptions.TargetTriple"/>, else the host triple) when
    /// <see cref="CargoBuildOptions.LicensePolicy"/> is set, or when
    /// <see cref="CargoBuildOptions.ExpectedCrateCount"/> is not set AND someone receives the
    /// progress (a <paramref name="progress"/> reporter is passed or the <see cref="Progress"/>
    /// event has a handler) - with neither a policy nor a progress receiver no metadata call is
    /// made and <see cref="CargoProgress.Total"/> stays unknown - and (3) when a policy is set
    /// audits the licenses of that graph - with <see cref="LicenseGateMode.Enforce"/> (the
    /// default) a failed audit throws <see cref="LicensePolicyViolationException"/>, or, when
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is false, returns a failed result
    /// with <see cref="CargoCommandResult.LicenseGateFailed"/> set, without running cargo; with
    /// <see cref="LicenseGateMode.ReportOnly"/> a failed audit is reported as a
    /// <see cref="CargoProgressPhase.Warning"/> progress ("License audit failed (report only):
    /// N rejected, M unresolved") and cargo runs anyway - and (4) ESTIMATES
    /// <see cref="CargoProgress.Total"/> from the same graph when
    /// <see cref="CargoBuildOptions.ExpectedCrateCount"/> is not set and the graph was read
    /// (the packages reachable
    /// from the selected packages over normal and build dependencies; see
    /// <see cref="CargoMetadata.GetBuildClosure"/>). Without a policy a failed
    /// <c>cargo metadata</c> only leaves the total unknown. When
    /// <see cref="CargoBuildOptions.TargetTriple"/> is a Linux target
    /// (<see cref="LinuxLinkProfile.IsLinuxTriple"/>) other than the host's own triple, step
    /// (1) also requires that target's standard library (on a rustup-managed toolchain) and,
    /// when <see cref="CargoBuildOptions.Environment"/> carries the
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c> of a <see cref="LinuxLinkProfile"/>
    /// (<c>&lt;prefix&gt;-gcc</c>), the profile's cross <c>gcc</c> and <c>ar</c>, and throws
    /// <see cref="ToolchainNotFoundException"/> with
    /// <see cref="InstallGuidanceTopic.InstallLinuxCrossTools"/> guidance when one is missing,
    /// instead of letting cargo fail with "can't find crate for `std`" or a linker error.
    /// <c>clean</c> and <c>fmt</c> skip every check. <c>fetch</c> only checks that cargo and
    /// rustc are installed (a C toolchain is not needed to download) and skips steps (2) to (4)
    /// and the Linux cross check. The caller's options object is never modified.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="command">The subcommand to run.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults (no license
    /// gate).</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <returns>The result (a <see cref="CargoTestResult"/> for <see cref="CargoCommand.Test"/>).
    /// A failed build is a result with <see cref="CargoCommandResult.Success"/> false, not an
    /// exception; <see cref="CargoCommandResult.LicenseAudit"/> holds the audit when a gate
    /// ran (passed or not with <see cref="LicenseGateMode.ReportOnly"/>).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc or the C compiler / linker is
    /// not installed, or a foreign Linux target's standard library or cross tools are missing
    /// (compiling commands only; <c>fetch</c> needs only cargo and rustc); cargo is not
    /// started.</exception>
    /// <exception cref="LicensePolicyViolationException">The license gate failed,
    /// <see cref="CargoBuildOptions.LicenseGateMode"/> is <see cref="LicenseGateMode.Enforce"/>
    /// and <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is true; the exception
    /// carries the report.</exception>
    /// <exception cref="CargoCommandException">cargo could not be started, <c>cargo
    /// metadata</c> failed for the license gate, or the clippy / rustfmt component is not
    /// installed.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled
    /// and <see cref="CargoBuildOptions.ThrowOnCancellation"/> is true.</exception>
    public async Task<CargoCommandResult> RunCargoAsync(CargoProject project, CargoCommand command, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var effective = options?.Clone() ?? new CargoBuildOptions();
        LicenseAuditReport audit = null;
        ToolchainReadiness readiness = null;

        if (command == CargoCommand.Fetch)
        {
            readiness = await EnsureToolchainAsync(project, requireCCompiler: false, cancellationToken).ConfigureAwait(false);
        }
        else if (command is not (CargoCommand.Clean or CargoCommand.Fmt))
        {
            readiness = await EnsureToolchainAsync(project, requireCCompiler: true, cancellationToken).ConfigureAwait(false);
            EnsureLinuxCrossReady(project, effective, readiness.Toolchain);

            var estimate = !effective.ExpectedCrateCount.HasValue && (effective.LicensePolicy != null || progress != null || Progress != null);
            if (effective.LicensePolicy != null || estimate)
            {
                var metadataOptions = ToMetadataOptions(effective, CrateCountEstimator.FilterPlatformFor(effective.TargetTriple ?? readiness.Toolchain?.HostTriple));
                var metadata = effective.LicensePolicy != null
                    ? await project.GetMetadataAsync(metadataOptions, cancellationToken).ConfigureAwait(false)
                    : await TryGetMetadataAsync(project, metadataOptions, cancellationToken).ConfigureAwait(false);
                if (estimate)
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
                            _runner.Publish(
                                new CargoProgress
                                {
                                    Phase = CargoProgressPhase.Warning,
                                    Total = effective.ExpectedCrateCount,
                                    Message = ReportOnlyAuditMessage(audit),
                                },
                                progress);
                        }
                        else if (effective.ThrowOnLicenseViolation)
                        {
                            throw new LicensePolicyViolationException(
                                $"cargo {CargoRunner.SubcommandName(command)} was not run because the license audit of '{project.ManifestPath}' failed: {audit.RejectedCount} rejected and {audit.UnresolvedCount} unresolved crate(s).",
                                audit);
                        }
                        else
                        {
                            var gateResult = CargoCommandResult.ForFailedLicenseGate(command, audit, project.Directory);
                            gateResult.Readiness = readiness;
                            return gateResult;
                        }
                    }
                }
            }
        }

        var result = await _runner.RunAsync(project.Directory, command, effective, progress, cancellationToken).ConfigureAwait(false);
        result.LicenseAudit = audit;
        result.Readiness = readiness;
        return result;
    }

    /// <summary>
    /// Runs <c>cargo build</c> on a project, with the readiness check and the optional license
    /// gate described in <see cref="RunCargoAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <returns>The result; <see cref="CargoCommandResult.RunnableArtifacts"/> lists the built
    /// executables.</returns>
    public Task<CargoCommandResult> BuildAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Build, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo check</c> (type-check only, no executables), with the checks described in
    /// <see cref="RunCargoAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> CheckAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Check, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo test</c> and parses the libtest output of every test binary, with the
    /// checks described in <see cref="RunCargoAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options (<see cref="CargoBuildOptions.NoFailFast"/>,
    /// <see cref="CargoBuildOptions.TestNameFilter"/>); <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public async Task<CargoTestResult> TestAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => (CargoTestResult)await RunCargoAsync(project, CargoCommand.Test, options, progress, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Runs <c>cargo clean</c> (no readiness check, no license gate).
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> CleanAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Clean, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo doc</c> (with <c>--no-deps</c> unless
    /// <see cref="CargoBuildOptions.NoDeps"/> is false), with the checks described in
    /// <see cref="RunCargoAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> DocAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Doc, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo clippy</c> (lints arrive as diagnostics with <c>clippy::</c> codes), with
    /// the checks described in <see cref="RunCargoAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <exception cref="CargoCommandException">The clippy component is not installed (the
    /// exception carries install guidance).</exception>
    public Task<CargoCommandResult> ClippyAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Clippy, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo fmt --check</c> (or <c>cargo fmt</c> when
    /// <see cref="CargoBuildOptions.CheckOnly"/> is false); each unformatted region is a
    /// warning diagnostic with code <c>rustfmt</c>. No readiness check, no license gate.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <exception cref="CargoCommandException">The rustfmt component is not installed (the
    /// exception carries install guidance).</exception>
    public Task<CargoCommandResult> FmtAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Fmt, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo fetch</c>: downloads every dependency of the project into the local cache
    /// (CARGO_HOME) - for every platform, or with <see cref="CargoBuildOptions.TargetTriple"/>
    /// for that target only - so that later commands, the license gate's <c>cargo metadata</c>
    /// included, can run with <see cref="CargoBuildOptions.Offline"/>. Compiles nothing. The
    /// readiness check needs only cargo and rustc (no C toolchain); there is no license gate
    /// and no crate-count estimate. Of the options only
    /// <see cref="CargoBuildOptions.TargetTriple"/>, <see cref="CargoBuildOptions.Locked"/>,
    /// <see cref="CargoBuildOptions.Offline"/>, <see cref="CargoBuildOptions.Frozen"/>,
    /// <see cref="CargoBuildOptions.Verbose"/>, the environment, timeout and
    /// <see cref="CargoBuildOptions.ExtraArguments"/> apply. Each download is a
    /// <see cref="CargoProgressPhase.Downloading"/> report naming the crate; nothing is reported
    /// for crates that are already cached. A failed download is a result with
    /// <see cref="CargoCommandResult.Success"/> false and cargo's message in
    /// <see cref="CargoCommandResult.Errors"/>, not an exception.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <exception cref="ToolchainNotFoundException">cargo or rustc is not installed; cargo is
    /// not started.</exception>
    public Task<CargoCommandResult> FetchAsync(CargoProject project, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunCargoAsync(project, CargoCommand.Fetch, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo metadata</c> and reports dependency health: a <c>cargo tree</c>-style
    /// listing, crates present in several versions and, when <paramref name="checkOutdated"/>
    /// is true, crates.io crates that are outdated or yanked (one crates.io request per crate
    /// name, spaced by <see cref="RustWorkbenchOptions.CratesIoRequestInterval"/>).
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="checkOutdated">Ask crates.io about every registry crate.</param>
    /// <param name="metadataOptions">Options for <c>cargo metadata</c>, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels cargo and the crates.io requests.</param>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is null.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException"><c>cargo metadata</c> failed.</exception>
    /// <exception cref="RustToolsException">A crates.io request failed.</exception>
    public async Task<DependencyReport> InspectDependenciesAsync(CargoProject project, bool checkOutdated = false, CargoMetadataOptions metadataOptions = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        var metadata = await project.GetMetadataAsync(metadataOptions, cancellationToken).ConfigureAwait(false);
        var inspector = new DependencyInspector(metadata);
        var outdated = checkOutdated
            ? await inspector.CheckOutdatedAsync(_cratesIo.Value, cancellationToken).ConfigureAwait(false)
            : Array.Empty<OutdatedCrate>();
        return new DependencyReport
        {
            Metadata = metadata,
            Inspector = inspector,
            DuplicateVersions = inspector.FindDuplicateVersions(),
            Outdated = outdated,
            OutdatedChecked = checkOutdated,
            TreeText = string.Join("\n", inspector.GetDependencyTree()),
        };
    }

    /// <summary>
    /// Extracts a source archive (zip, tar, tar.gz / tgz, with the safety checks of
    /// <see cref="SourceArchiveExtractor"/>), opens the project found in it (the workspace root
    /// for a workspace, otherwise the shallowest package) and builds it with
    /// <see cref="BuildAsync"/> - readiness check and optional license gate included.
    /// </summary>
    /// <param name="archivePath">The archive file.</param>
    /// <param name="destinationDirectory">The folder to extract into (kept on dispose), or
    /// <c>null</c> for a new temporary folder (deleted on dispose).</param>
    /// <param name="options">The build options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives build progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Stops extraction, or kills the cargo process tree.</param>
    /// <returns>The extracted source, project and build result. Dispose it when done (after
    /// running the built programs).</returns>
    /// <exception cref="ArgumentException"><paramref name="archivePath"/> is blank.</exception>
    /// <exception cref="System.IO.FileNotFoundException">The archive does not exist.</exception>
    /// <exception cref="SourceArchiveException">The archive is unreadable or unsafe, or holds
    /// no Cargo.toml.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc or the C compiler / linker is
    /// not installed.</exception>
    /// <exception cref="LicensePolicyViolationException">The license gate failed and
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is true.</exception>
    /// <remarks>Whenever this method throws after extracting, the extraction is disposed
    /// first. Extraction uses the default <see cref="SourceArchiveOptions"/>; see the overload
    /// that takes them.</remarks>
    public Task<ExtractAndBuildResult> ExtractAndBuildAsync(string archivePath, string destinationDirectory = null, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => ExtractAndBuildAsync(archivePath, destinationDirectory, null, options, progress, cancellationToken);

    /// <summary>
    /// Extracts a source archive with the given extraction settings (size and entry-count
    /// limits, root-folder stripping, overwrite, delete-on-dispose), opens the project found in
    /// it and builds it with <see cref="BuildAsync"/> - readiness check and optional license
    /// gate included. See
    /// <see cref="ExtractAndBuildAsync(string, string, CargoBuildOptions, IProgress{CargoProgress}, CancellationToken)"/>.
    /// </summary>
    /// <param name="archivePath">The archive file.</param>
    /// <param name="destinationDirectory">The folder to extract into, or <c>null</c> for a new
    /// temporary folder.</param>
    /// <param name="archiveOptions">The extraction settings, or <c>null</c> for the
    /// defaults.</param>
    /// <param name="options">The build options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives build progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Stops extraction, or kills the cargo process tree.</param>
    /// <returns>The extracted source, project and build result. Dispose it when done.</returns>
    /// <exception cref="ArgumentException"><paramref name="archivePath"/> is blank.</exception>
    /// <exception cref="System.IO.FileNotFoundException">The archive does not exist.</exception>
    /// <exception cref="SourceArchiveException">The archive is unreadable or unsafe, exceeds a
    /// limit in <paramref name="archiveOptions"/>, or holds no Cargo.toml.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc or the C compiler / linker is
    /// not installed.</exception>
    /// <exception cref="LicensePolicyViolationException">The license gate failed and
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> is true.</exception>
    /// <remarks>Whenever this method throws after extracting, the extraction is disposed
    /// first.</remarks>
    public async Task<ExtractAndBuildResult> ExtractAndBuildAsync(string archivePath, string destinationDirectory, SourceArchiveOptions archiveOptions, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        var source = await new SourceArchiveExtractor().ExtractAsync(archivePath, destinationDirectory, archiveOptions, cancellationToken).ConfigureAwait(false);
        try
        {
            if (source.ManifestPath == null)
            {
                throw new SourceArchiveException($"The archive '{source.ArchivePath}' does not contain a Cargo.toml, so there is nothing to build.", source.ArchivePath, null);
            }

            var project = OpenProject(source.ManifestPath);
            var build = await BuildAsync(project, options, progress, cancellationToken).ConfigureAwait(false);
            return new ExtractAndBuildResult(source, project, build);
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a program that cargo built (see <see cref="CargoCommandResult.RunnableArtifacts"/>).
    /// </summary>
    /// <param name="artifact">A runnable artifact from a build result.</param>
    /// <param name="options">Arguments, folder, environment, input and timeout; <c>null</c>
    /// uses the defaults.</param>
    /// <param name="cancellationToken">Kills the program (and its child processes) when
    /// cancelled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="artifact"/> is null.</exception>
    /// <exception cref="RustToolsException">The artifact has no executable, the file no longer
    /// exists, or it could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled
    /// and <see cref="RunOptions.ThrowOnCancellation"/> is true.</exception>
    public Task<RunResult> RunAsync(BuildArtifact artifact, RunOptions options = null, CancellationToken cancellationToken = default)
        => _binaryRunner.RunAsync(artifact, options, cancellationToken);

    /// <summary>
    /// The <c>cargo metadata</c> options that match a build's dependency selection (offline /
    /// locked / frozen and the feature flags), used by the license gate.
    /// </summary>
    internal static CargoMetadataOptions ToMetadataOptions(CargoBuildOptions options)
        => ToMetadataOptions(options, null);

    /// <summary>
    /// The <c>cargo metadata</c> options that match a build's dependency selection, plus
    /// <c>--filter-platform</c> for the build's target triple (<c>null</c>: every platform).
    /// One such call serves both the license gate and the
    /// <see cref="CargoProgress.Total"/> estimate.
    /// </summary>
    internal static CargoMetadataOptions ToMetadataOptions(CargoBuildOptions options, string filterPlatform)
    {
        var metadataOptions = new CargoMetadataOptions
        {
            FilterPlatform = filterPlatform,
            Offline = options.Offline,
            Locked = options.Locked,
            Frozen = options.Frozen,
            AllFeatures = options.AllFeatures,
            NoDefaultFeatures = options.NoDefaultFeatures,
        };
        foreach (var feature in options.Features.Where(f => !string.IsNullOrWhiteSpace(f)))
        {
            metadataOptions.Features.Add(feature.Trim());
        }

        return metadataOptions;
    }

    /// <summary>
    /// The cache key for a detection: the full project folder plus the path and content of the
    /// toolchain file that applies to it; <c>null</c> when the folder does not exist (the
    /// detector then reports that, and nothing is cached).
    /// </summary>
    internal static string ToolchainCacheKey(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            return string.Empty;
        }

        string directory;
        try
        {
            directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(projectDirectory));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }

        if (!Directory.Exists(directory))
        {
            return null;
        }

        string file = null;
        string content = null;
        try
        {
            file = RustToolchainFile.Find(directory);
            if (file != null)
            {
                content = File.ReadAllText(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            content = "<unreadable>";
        }

        return directory + "\n" + file + "\n" + content;
    }

    private async Task<RustToolchainInfo> GetToolchainAsync(string projectDirectory, bool forceRefresh, CancellationToken cancellationToken)
    {
        var key = ToolchainCacheKey(projectDirectory);
        if (key == null)
        {
            return await _detect(projectDirectory, cancellationToken).ConfigureAwait(false);
        }

        if (!forceRefresh && TryGetCachedToolchain(key, out var cached))
        {
            return cached;
        }

        await _detectionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && TryGetCachedToolchain(key, out cached))
            {
                return cached;
            }

            long generation;
            lock (_cacheLock)
            {
                generation = _cacheGeneration;
            }

            var info = await _detect(projectDirectory, cancellationToken).ConfigureAwait(false);
            lock (_cacheLock)
            {
                if (generation == _cacheGeneration)
                {
                    _toolchainCache[key] = new ToolchainCacheEntry(info, Clock());
                }
            }

            return info;
        }
        finally
        {
            _detectionGate.Release();
        }
    }

    private bool TryGetCachedToolchain(string key, out RustToolchainInfo info)
    {
        lock (_cacheLock)
        {
            if (_toolchainCache.TryGetValue(key, out var entry))
            {
                var duration = Options.ToolchainCacheDuration;
                if (!duration.HasValue || Clock() - entry.DetectedAt < duration.Value)
                {
                    info = entry.Info;
                    return true;
                }

                _toolchainCache.Remove(key);
            }
        }

        info = null;
        return false;
    }

    /// <summary>
    /// Builds a project for both macOS architectures (<c>aarch64-apple-darwin</c>, then
    /// <c>x86_64-apple-darwin</c>; both targets must be installed, <c>rustup target add</c>) and
    /// joins every program and dynamic library that both builds produced into a universal file
    /// in <paramref name="outputDirectory"/>, with <see cref="MacOSUniversalBinary"/>. Each build
    /// goes through <see cref="RunCargoAsync"/> (readiness check, optional license gate).
    /// Without a <paramref name="profile"/> the builds use the machine's own linker and SDK, as
    /// any local build does (on a Mac: Apple's); with one, they use rustup's <c>rust-lld</c> and
    /// the profile's libSystem stub, which is what gives files identical to a
    /// CodeBrix.RustTools.Docker container build.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="outputDirectory">The folder that receives the universal files (created
    /// when missing; files of the same name are replaced).</param>
    /// <param name="options">The build options; <c>null</c> uses the defaults. The caller's
    /// object is never modified; <see cref="CargoBuildOptions.TargetTriple"/> is set per build.
    /// With a profile, <see cref="CargoBuildOptions.MacOSInstallName"/> defaults to
    /// <see cref="MacOSLinkProfile.DefaultInstallName"/> when unset.</param>
    /// <param name="profile">The link profile to apply to both builds, or <c>null</c>.</param>
    /// <param name="progress">Receives the progress of both builds; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the running cargo when cancelled.</param>
    /// <returns>Both build results and the universal files; <see cref="MacOSUniversalBuildResult.Success"/>
    /// is false when either build failed (no files are written then).</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="outputDirectory"/> is blank.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc, the C toolchain or one of the
    /// two macOS targets is missing; the exception's guidance names the <c>rustup target add</c>
    /// command.</exception>
    /// <exception cref="LicensePolicyViolationException">See <see cref="RunCargoAsync"/>.</exception>
    public async Task<MacOSUniversalBuildResult> BuildMacOSUniversalAsync(CargoProject project, string outputDirectory, CargoBuildOptions options = null, MacOSLinkProfile profile = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("The output folder is required.", nameof(outputDirectory));
        }

        var info = await DetectToolchainAsync(project.Directory, cancellationToken).ConfigureAwait(false);
        var requirements = new ToolchainRequirements();
        foreach (var triple in MacOSLinkProfile.Triples)
        {
            requirements.RequiredTargets.Add(triple);
        }

        var readiness = EvaluateReadiness(info, requirements);
        var blocking = readiness.Issues.Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0)
        {
            throw new ToolchainNotFoundException(
                $"'{project.ManifestPath}' cannot be built for both macOS architectures: {string.Join("; ", blocking.Select(i => i.Message))}. See Guidance for what to install.",
                readiness,
                GetInstallGuidance(readiness));
        }

        var results = new Dictionary<string, CargoCommandResult>(StringComparer.Ordinal);
        foreach (var triple in MacOSLinkProfile.Triples)
        {
            var effective = options?.Clone() ?? new CargoBuildOptions();
            if (profile != null)
            {
                profile.ApplyTo(effective, triple, effective.MacOSInstallName ?? MacOSLinkProfile.DefaultInstallName(project));
            }
            else
            {
                effective.TargetTriple = triple;
            }

            results[triple] = await RunCargoAsync(project, CargoCommand.Build, effective, progress, cancellationToken).ConfigureAwait(false);
        }

        var arm64 = results[MacOSLinkProfile.Arm64Triple];
        var x64 = results[MacOSLinkProfile.X64Triple];
        var files = new List<MacOSUniversalFile>();
        var output = Path.GetFullPath(outputDirectory);
        if (arm64.Success && x64.Success)
        {
            Directory.CreateDirectory(output);
            var armFiles = JoinableFiles(arm64);
            var x64Files = JoinableFiles(x64);
            foreach (var name in armFiles.Keys.Where(x64Files.ContainsKey).OrderBy(n => n, StringComparer.Ordinal))
            {
                var path = Path.Combine(output, name);
                var slicePaths = new[] { armFiles[name].Path, x64Files[name].Path };
                var slices = MacOSUniversalBinary.Create(slicePaths, path);
                var executable = armFiles[name].IsExecutable || x64Files[name].IsExecutable;
                if (executable && !OperatingSystem.IsWindows())
                {
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
                }

                files.Add(new MacOSUniversalFile
                {
                    Path = path,
                    FileName = name,
                    SlicePaths = slicePaths,
                    Slices = slices,
                    IsExecutable = executable,
                });
            }
        }

        return new MacOSUniversalBuildResult
        {
            Arm64Build = arm64,
            X64Build = x64,
            OutputDirectory = output,
            Files = files,
        };
    }

    /// <summary>
    /// Builds a project for Android with the NDK found on this machine (or the profile in
    /// <paramref name="androidOptions"/>) - by default both 64-bit targets,
    /// <c>aarch64-linux-android</c> then <c>x86_64-linux-android</c>, each through
    /// <see cref="RunCargoAsync"/> - and copies the shared libraries (and, when asked, the
    /// programs) of every successful build into <paramref name="outputDirectory"/> in the
    /// requested <see cref="AndroidOutputLayout"/>. Pure-Rust crates and crates that compile C
    /// both work: the NDK provides the C toolchain.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="outputDirectory">The folder that receives the files (created when missing;
    /// files of the same name are replaced).</param>
    /// <param name="options">The build options; <c>null</c> uses the defaults. The caller's
    /// object is never modified; <see cref="CargoBuildOptions.TargetTriple"/> is set per build.</param>
    /// <param name="androidOptions">The targets, API level or profile, layout and whether
    /// programs are copied; <c>null</c> uses the defaults (both targets, API 24, jniLibs,
    /// libraries only).</param>
    /// <param name="progress">Receives the progress of every build; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the running cargo when cancelled.</param>
    /// <returns>Every build's result and the copied files; <see cref="AndroidBuildResult.Success"/>
    /// is false when any build failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="outputDirectory"/> is blank, or a
    /// requested triple is not an Android target.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc, a requested target's standard
    /// library or (without a profile) the NDK is missing, or this machine cannot run the NDK;
    /// the exception's guidance names what to install.</exception>
    /// <exception cref="LicensePolicyViolationException">See <see cref="RunCargoAsync"/>.</exception>
    public async Task<AndroidBuildResult> BuildAndroidAsync(CargoProject project, string outputDirectory, CargoBuildOptions options = null, AndroidBuildOptions androidOptions = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("The output folder is required.", nameof(outputDirectory));
        }

        androidOptions ??= new AndroidBuildOptions();
        var triples = androidOptions.Triples.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        if (triples.Count == 0)
        {
            throw new ArgumentException("At least one Android target triple is required.", nameof(androidOptions));
        }

        var unknown = triples.FirstOrDefault(t => !AndroidLinkProfile.IsAndroidTriple(t));
        if (unknown != null)
        {
            throw new ArgumentException($"'{unknown}' is not an Android target triple ({string.Join(", ", AndroidLinkProfile.Triples)}).", nameof(androidOptions));
        }

        var info = await DetectToolchainAsync(project.Directory, cancellationToken).ConfigureAwait(false);
        var requirements = new ToolchainRequirements { RequireAndroidNdk = androidOptions.Profile == null };
        foreach (var triple in triples)
        {
            requirements.RequiredTargets.Add(triple);
        }

        var readiness = EvaluateReadiness(info, requirements);
        var blocking = readiness.Issues.Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0)
        {
            throw new ToolchainNotFoundException(
                $"'{project.ManifestPath}' cannot be built for Android: {string.Join("; ", blocking.Select(i => i.Message))}. See Guidance for what to install.",
                readiness,
                GetInstallGuidance(readiness));
        }

        var profile = androidOptions.Profile ?? AndroidLinkProfile.ForNdk(info.AndroidNdk, androidOptions.ApiLevel);
        var builds = new Dictionary<string, CargoCommandResult>(StringComparer.Ordinal);
        foreach (var triple in triples)
        {
            var effective = options?.Clone() ?? new CargoBuildOptions();
            profile.ApplyTo(effective, triple);
            builds[triple] = await RunCargoAsync(project, CargoCommand.Build, effective, progress, cancellationToken).ConfigureAwait(false);
        }

        var output = Path.GetFullPath(outputDirectory);
        var files = new List<AndroidBuildFile>();
        foreach (var pair in builds.Where(b => b.Value.Success))
        {
            var abi = AndroidLinkProfile.AbiOf(pair.Key);
            var rid = AndroidLinkProfile.RuntimeIdentifierOf(pair.Key);
            var folder = androidOptions.Layout switch
            {
                AndroidOutputLayout.JniLibs => Path.Combine(output, "jniLibs", abi),
                AndroidOutputLayout.NuGetRuntimes => Path.Combine(output, "runtimes", rid, "native"),
                AndroidOutputLayout.ByTarget => Path.Combine(output, pair.Key),
                _ => throw new ArgumentOutOfRangeException(nameof(androidOptions), androidOptions.Layout, "Unknown Android output layout."),
            };
            foreach (var (source, isExecutable) in SharedObjectFiles(pair.Value, androidOptions.IncludeExecutables))
            {
                Directory.CreateDirectory(folder);
                var destination = Path.Combine(folder, Path.GetFileName(source));
                File.Copy(source, destination, overwrite: true);
                files.Add(new AndroidBuildFile
                {
                    Path = destination,
                    FileName = Path.GetFileName(source),
                    Triple = pair.Key,
                    Abi = abi,
                    RuntimeIdentifier = rid,
                    SourcePath = source,
                    IsExecutable = isExecutable,
                });
            }
        }

        return new AndroidBuildResult
        {
            Builds = builds,
            OutputDirectory = output,
            Layout = androidOptions.Layout,
            Files = files,
        };
    }

    /// <summary>
    /// Builds a project on this machine for Linux targets of another processor architecture with
    /// the GNU cross toolchain (<see cref="LinuxLinkProfile"/>) - by default every Linux target
    /// other than the host's own triple (on an x86-64 Linux host <c>aarch64-unknown-linux-gnu</c>
    /// then <c>riscv64gc-unknown-linux-gnu</c>), each through <see cref="RunCargoAsync"/> - and
    /// copies the shared libraries (and, when asked, the programs) of every successful build into
    /// <paramref name="outputDirectory"/> in the requested <see cref="LinuxOutputLayout"/>. Before
    /// any cargo runs it evaluates <see cref="ToolchainReadiness"/> for each target's standard
    /// library and each profile's cross <c>gcc</c> and <c>ar</c>, and throws
    /// <see cref="ToolchainNotFoundException"/> with install guidance when one is missing. The
    /// host's own triple is not built here: build it natively with <see cref="BuildAsync"/>.
    /// </summary>
    /// <param name="project">The project (from <see cref="OpenProject"/>).</param>
    /// <param name="outputDirectory">The folder that receives the files (created when missing;
    /// files of the same name are replaced).</param>
    /// <param name="options">The build options; <c>null</c> uses the defaults. The caller's
    /// object is never modified; <see cref="CargoBuildOptions.TargetTriple"/> and the profile's
    /// variables are set per build.</param>
    /// <param name="linuxOptions">The targets, profile or tool folder, layout and whether
    /// programs are copied; <c>null</c> uses the defaults (the foreign targets, tools on PATH,
    /// NuGet runtimes layout, libraries only).</param>
    /// <param name="progress">Receives the progress of every build; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the running cargo when cancelled.</param>
    /// <returns>Every build's result and the copied files; <see cref="LinuxBuildResult.Success"/>
    /// is false when any build failed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="project"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="outputDirectory"/> is blank, a
    /// requested triple is not a Linux target or is the host's own triple, the profile's tool
    /// prefix does not fit a requested triple, or no target is left to build.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo, rustc, the C toolchain, a target's
    /// standard library or a cross tool is missing; the exception's guidance names the
    /// <c>rustup target add</c> command and the Debian packages to install.</exception>
    /// <exception cref="LicensePolicyViolationException">See <see cref="RunCargoAsync"/>.</exception>
    public async Task<LinuxBuildResult> BuildLinuxAsync(CargoProject project, string outputDirectory, CargoBuildOptions options = null, LinuxBuildOptions linuxOptions = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("The output folder is required.", nameof(outputDirectory));
        }

        linuxOptions ??= new LinuxBuildOptions();
        var requested = linuxOptions.Triples.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var unknown = requested.FirstOrDefault(t => !LinuxLinkProfile.IsLinuxTriple(t));
        if (unknown != null)
        {
            throw new ArgumentException($"'{unknown}' is not a Linux target triple ({string.Join(", ", LinuxLinkProfile.Triples)}).", nameof(linuxOptions));
        }

        var profile = linuxOptions.Profile;
        var mismatched = profile == null ? null : requested.FirstOrDefault(t => !string.Equals(LinuxLinkProfile.ToolPrefixOf(t), profile.ToolPrefix, StringComparison.Ordinal));
        if (mismatched != null)
        {
            throw new ArgumentException($"'{mismatched}' is built with the {LinuxLinkProfile.ToolPrefixOf(mismatched)} tools, not the profile's {profile.ToolPrefix}; leave Profile null (and use ToolDirectory) to build several targets.", nameof(linuxOptions));
        }

        var info = await DetectToolchainAsync(project.Directory, cancellationToken).ConfigureAwait(false);
        var host = info.HostTriple;
        if (host != null && requested.Contains(host, StringComparer.Ordinal))
        {
            throw new ArgumentException($"'{host}' is this machine's own target; build it natively with BuildAsync, not through a cross profile.", nameof(linuxOptions));
        }

        var triples = requested.Count > 0
            ? requested
            : LinuxLinkProfile.Triples
                .Where(t => !string.Equals(t, host, StringComparison.Ordinal))
                .Where(t => profile == null || string.Equals(LinuxLinkProfile.ToolPrefixOf(t), profile.ToolPrefix, StringComparison.Ordinal))
                .ToList();
        if (triples.Count == 0)
        {
            throw new ArgumentException($"No Linux target other than this machine's own ({host}) is left to build" + (profile == null ? "." : $" with the {profile.ToolPrefix} profile."), nameof(linuxOptions));
        }

        var profiles = triples.ToDictionary(t => t, t => profile ?? LinuxLinkProfile.ForTriple(t, linuxOptions.ToolDirectory), StringComparer.Ordinal);
        var requirements = new ToolchainRequirements();
        foreach (var triple in triples)
        {
            requirements.RequiredTargets.Add(triple);
            requirements.RequiredLinuxCrossTools[triple] = profiles[triple];
        }

        var readiness = EvaluateReadiness(info, requirements);
        var blocking = readiness.Issues.Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0)
        {
            throw new ToolchainNotFoundException(
                $"'{project.ManifestPath}' cannot be built for {string.Join(" and ", triples)}: {string.Join("; ", blocking.Select(i => i.Message))}. See Guidance for what to install.",
                readiness,
                GetInstallGuidance(readiness));
        }

        var builds = new Dictionary<string, CargoCommandResult>(StringComparer.Ordinal);
        foreach (var triple in triples)
        {
            var effective = options?.Clone() ?? new CargoBuildOptions();
            profiles[triple].ApplyTo(effective, triple);
            builds[triple] = await RunCargoAsync(project, CargoCommand.Build, effective, progress, cancellationToken).ConfigureAwait(false);
        }

        var output = Path.GetFullPath(outputDirectory);
        var files = new List<LinuxBuildFile>();
        foreach (var pair in builds.Where(b => b.Value.Success))
        {
            var rid = LinuxLinkProfile.RuntimeIdentifierOf(pair.Key);
            var folder = linuxOptions.Layout switch
            {
                LinuxOutputLayout.NuGetRuntimes => Path.Combine(output, "runtimes", rid, "native"),
                LinuxOutputLayout.ByTriple => Path.Combine(output, pair.Key),
                _ => throw new ArgumentOutOfRangeException(nameof(linuxOptions), linuxOptions.Layout, "Unknown Linux output layout."),
            };
            foreach (var (source, isExecutable) in SharedObjectFiles(pair.Value, linuxOptions.IncludeExecutables))
            {
                Directory.CreateDirectory(folder);
                var destination = Path.Combine(folder, Path.GetFileName(source));
                File.Copy(source, destination, overwrite: true);
                files.Add(new LinuxBuildFile
                {
                    Path = destination,
                    FileName = Path.GetFileName(source),
                    Triple = pair.Key,
                    RuntimeIdentifier = rid,
                    SourcePath = source,
                    IsExecutable = isExecutable,
                });
            }
        }

        return new LinuxBuildResult
        {
            Builds = builds,
            OutputDirectory = output,
            Layout = linuxOptions.Layout,
            Files = files,
        };
    }

    /// <summary>
    /// The readiness requirements of a plain build for a Linux target of another architecture:
    /// the target's standard library (rustup-managed toolchains only, where the installed
    /// targets are known) and, when the options (or the workbench options) carry a
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_LINKER</c> of the <c>&lt;prefix&gt;-gcc</c> form, that
    /// profile's cross <c>gcc</c> and <c>ar</c>. <c>null</c> when nothing needs checking (no
    /// target, not a Linux target, the host's own triple, or an unknown host).
    /// </summary>
    internal ToolchainRequirements LinuxCrossRequirements(CargoBuildOptions options, RustToolchainInfo info)
    {
        var triple = options?.TargetTriple?.Trim();
        if (info == null
            || string.IsNullOrWhiteSpace(info.HostTriple)
            || !LinuxLinkProfile.IsLinuxTriple(triple)
            || string.Equals(triple, info.HostTriple, StringComparison.Ordinal))
        {
            return null;
        }

        var requirements = new ToolchainRequirements { RequireCCompiler = false, RequireToolchainFileChannel = false };
        if (info.IsRustupManaged)
        {
            requirements.RequiredTargets.Add(triple);
        }

        var linkerKey = "CARGO_TARGET_" + TargetTripleNames.Upper(triple) + "_LINKER";
        if (!options.Environment.TryGetValue(linkerKey, out var linker))
        {
            Options.Environment.TryGetValue(linkerKey, out linker);
        }

        var profile = ProfileFromLinker(linker);
        if (profile != null)
        {
            requirements.RequiredLinuxCrossTools[triple] = profile;
        }

        return requirements.RequiredTargets.Count == 0 && requirements.RequiredLinuxCrossTools.Count == 0 ? null : requirements;
    }

    /// <summary>The <see cref="LinuxLinkProfile"/> whose <see cref="LinuxLinkProfile.CompilerPath"/>
    /// is <paramref name="linker"/>, or <c>null</c> when the linker is not a
    /// <c>&lt;prefix&gt;-gcc</c>.</summary>
    internal static LinuxLinkProfile ProfileFromLinker(string linker)
    {
        if (string.IsNullOrWhiteSpace(linker))
        {
            return null;
        }

        var trimmed = linker.Trim();
        var name = Path.GetFileName(trimmed);
        const string suffix = "-gcc";
        if (!name.EndsWith(suffix, StringComparison.Ordinal) || name.Length == suffix.Length)
        {
            return null;
        }

        var directory = trimmed.Length > name.Length ? trimmed.Substring(0, trimmed.Length - name.Length) : null;
        var profile = new LinuxLinkProfile(name.Substring(0, name.Length - suffix.Length), directory);
        return string.Equals(profile.CompilerPath, trimmed, StringComparison.Ordinal) ? profile : null;
    }

    private void EnsureLinuxCrossReady(CargoProject project, CargoBuildOptions options, RustToolchainInfo info)
    {
        var requirements = LinuxCrossRequirements(options, info);
        if (requirements == null)
        {
            return;
        }

        var readiness = EvaluateReadiness(info, requirements);
        var blocking = readiness.Issues.Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0)
        {
            throw new ToolchainNotFoundException(
                $"'{project.ManifestPath}' cannot be built for {options.TargetTriple.Trim()}: {string.Join("; ", blocking.Select(i => i.Message))}. See Guidance for what to install.",
                readiness,
                GetInstallGuidance(readiness));
        }
    }

    /// <summary>The shared libraries (<c>.so</c>, and optionally programs) an Android or Linux
    /// build produced, in artifact order.</summary>
    private static IEnumerable<(string Path, bool IsExecutable)> SharedObjectFiles(CargoCommandResult result, bool includeExecutables)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var artifact in result.Artifacts)
        {
            if (artifact.IsBuildScript || artifact.Profile.Test)
            {
                continue;
            }

            foreach (var file in artifact.Filenames.Where(f => f != null && f.EndsWith(".so", StringComparison.OrdinalIgnoreCase) && File.Exists(f)))
            {
                if (seen.Add(file))
                {
                    yield return (file, false);
                }
            }

            if (includeExecutables && artifact.Executable != null && File.Exists(artifact.Executable) && seen.Add(artifact.Executable))
            {
                yield return (artifact.Executable, true);
            }
        }
    }

    /// <summary>The programs and dynamic libraries a build produced, by file name.</summary>
    private static Dictionary<string, (string Path, bool IsExecutable)> JoinableFiles(CargoCommandResult result)
    {
        var files = new Dictionary<string, (string Path, bool IsExecutable)>(StringComparer.Ordinal);
        foreach (var artifact in result.Artifacts)
        {
            if (artifact.IsBuildScript || artifact.Profile.Test)
            {
                continue;
            }

            if (artifact.Executable != null && File.Exists(artifact.Executable))
            {
                files[Path.GetFileName(artifact.Executable)] = (artifact.Executable, true);
            }

            foreach (var file in artifact.Filenames.Where(f => f != null && f.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase) && File.Exists(f)))
            {
                files[Path.GetFileName(file)] = (file, false);
            }
        }

        return files;
    }

    /// <summary>
    /// The progress message of a <see cref="LicenseGateMode.ReportOnly"/> audit that failed.
    /// </summary>
    internal static string ReportOnlyAuditMessage(LicenseAuditReport audit)
        => $"License audit failed (report only): {audit.RejectedCount} rejected, {audit.UnresolvedCount} unresolved";

    /// <summary>
    /// <c>cargo metadata</c> for the crate-count estimate only (no license gate): a failure is
    /// not the build's problem - cargo reports it itself - so it yields <c>null</c> (no
    /// total). Cancellation still throws.
    /// </summary>
    private static async Task<CargoMetadata> TryGetMetadataAsync(CargoProject project, CargoMetadataOptions options, CancellationToken cancellationToken)
    {
        try
        {
            return await project.GetMetadataAsync(options, cancellationToken).ConfigureAwait(false);
        }
        catch (RustToolsException)
        {
            return null;
        }
    }

    private async Task<ToolchainReadiness> EnsureToolchainAsync(CargoProject project, bool requireCCompiler, CancellationToken cancellationToken)
    {
        var info = await DetectToolchainAsync(project.Directory, cancellationToken).ConfigureAwait(false);
        var readiness = EvaluateReadiness(info, new ToolchainRequirements { RequireCCompiler = requireCCompiler });
        var missing = readiness.MissingTools.Where(t => t is RustTool.Cargo or RustTool.Rustc || (requireCCompiler && t == RustTool.CCompiler)).ToList();
        if (missing.Count > 0)
        {
            var names = missing.Select(t => t == RustTool.CCompiler ? "the C compiler / linker" : t.ToString().ToLowerInvariant());
            var what = requireCCompiler ? "cannot be built" : "cannot have its dependencies fetched";
            throw new ToolchainNotFoundException(
                $"{string.Join(" and ", names)} not found, so '{project.ManifestPath}' {what}. See Guidance for what to install.",
                readiness,
                GetInstallGuidance(readiness));
        }

        return readiness;
    }

    private void OnProgress(CargoProgress report)
    {
        try
        {
            Progress?.Invoke(this, report);
        }
        catch (Exception)
        {
            // A consumer's handler must not break the cargo run.
        }
    }

    private sealed record ToolchainCacheEntry(RustToolchainInfo Info, DateTimeOffset DetectedAt);
}
