using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// Runs cargo subcommands (build, check, test, clean, doc, clippy, fmt, fetch) on a project with
/// the cargo installed on this machine, and turns cargo's <c>--message-format=json</c> output into
/// typed diagnostics, artifacts, test counts and progress reports. Cancellation and timeouts
/// kill the whole cargo process tree (rustc, build scripts, linkers). Every cargo process runs
/// with <c>RUSTUP_AUTO_INSTALL=0</c> and <c>CARGO_TERM_COLOR=never</c> unless the options set
/// them.
/// </summary>
public sealed class CargoRunner
{
    /// <summary>
    /// Creates a runner.
    /// </summary>
    /// <param name="options">Tool locations, environment and other shared settings;
    /// <c>null</c> uses the defaults.</param>
    public CargoRunner(RustWorkbenchOptions options = null)
    {
        Options = options ?? new RustWorkbenchOptions();
    }

    /// <summary>
    /// The settings this runner uses.
    /// </summary>
    public RustWorkbenchOptions Options { get; }

    /// <summary>
    /// Raised for every progress report of every command this runner runs (in addition to the
    /// per-call <see cref="IProgress{T}"/>). Raised on a thread-pool thread; exceptions thrown
    /// by handlers are ignored.
    /// </summary>
    public event EventHandler<CargoProgress> Progress;

    /// <summary>
    /// Runs a cargo subcommand on a project.
    /// </summary>
    /// <param name="projectDirectory">The project (or workspace) folder, or the path of its
    /// Cargo.toml.</param>
    /// <param name="command">The subcommand to run.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <returns>The result; for <see cref="CargoCommand.Test"/> it is a
    /// <see cref="CargoTestResult"/>. A failed build is a result with
    /// <see cref="CargoCommandResult.Success"/> false, not an exception.</returns>
    /// <exception cref="ArgumentException"><paramref name="projectDirectory"/> is blank, or
    /// <see cref="CargoBuildOptions.Jobs"/> is zero.</exception>
    /// <exception cref="DirectoryNotFoundException">The project folder does not exist.</exception>
    /// <exception cref="ToolchainNotFoundException">cargo cannot be found.</exception>
    /// <exception cref="CargoCommandException">cargo could not be started, or the clippy /
    /// rustfmt component needed by <see cref="CargoCommand.Clippy"/> /
    /// <see cref="CargoCommand.Fmt"/> is not installed (the exception carries install
    /// guidance).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired
    /// and <see cref="CargoBuildOptions.ThrowOnCancellation"/> is true.</exception>
    public async Task<CargoCommandResult> RunAsync(string projectDirectory, CargoCommand command, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
    {
        options ??= new CargoBuildOptions();
        var (projectFolder, manifestPath) = ResolveProject(projectDirectory);
        var workingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory) ? projectFolder : Path.GetFullPath(options.WorkingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"The working directory '{workingDirectory}' does not exist.");
        }

        if (manifestPath == null && !PathsEqual(workingDirectory, projectFolder))
        {
            var candidate = Path.Combine(projectFolder, "Cargo.toml");
            manifestPath = File.Exists(candidate) ? candidate : null;
        }

        var arguments = BuildArguments(command, options, manifestPath);
        var cargo = LocateCargo();

        var request = new ProcessRunRequest(cargo)
        {
            WorkingDirectory = workingDirectory,
            Timeout = options.Timeout,
            ThrowOnCancellation = options.ThrowOnCancellation,
        };
        foreach (var argument in arguments)
        {
            request.Arguments.Add(argument);
        }

        foreach (var pair in BuildEnvironment(Options, options))
        {
            request.Environment[pair.Key] = pair.Value;
        }

        var collector = new CargoOutputCollector(command, options, report => Publish(report, progress));
        request.OnStandardOutputLine = collector.OnStandardOutputLine;
        request.OnStandardErrorLine = collector.OnStandardErrorLine;

        ProcessRunResult run;
        try
        {
            run = await ProcessRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (RustToolsException ex)
        {
            throw new CargoCommandException(ex.Message, ex);
        }

        var result = collector.CreateResult(run.ExitCode, run.TimedOut, run.WasCancelled, run.Duration, run.CommandLine, run.StandardError, workingDirectory);
        if (!result.Success && !result.TimedOut && !result.WasCancelled)
        {
            ThrowIfComponentMissing(command, run.ExitCode, run.StandardError);
        }

        return result;
    }

    /// <summary>
    /// Runs <c>cargo build</c>. See <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> BuildAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Build, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo check</c>. See <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> CheckAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Check, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo clean</c>. See <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> CleanAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Clean, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo doc</c> (with <c>--no-deps</c> unless <see cref="CargoBuildOptions.NoDeps"/>
    /// is false). See <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> DocAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Doc, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo clippy</c>; lints arrive as diagnostics with <c>clippy::</c> codes. See
    /// <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <exception cref="CargoCommandException">The clippy component is not installed.</exception>
    public Task<CargoCommandResult> ClippyAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Clippy, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo fmt --check</c> (or <c>cargo fmt</c> when
    /// <see cref="CargoBuildOptions.CheckOnly"/> is false). Each unformatted region is a warning
    /// diagnostic with code <c>rustfmt</c>; the result fails when any file needs formatting.
    /// See <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    /// <exception cref="CargoCommandException">The rustfmt component is not installed.</exception>
    public Task<CargoCommandResult> FmtAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Fmt, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo fetch</c>: downloads every dependency of the project into the local cache
    /// (CARGO_HOME), for every platform or - with <see cref="CargoBuildOptions.TargetTriple"/> -
    /// for that target only, so that later commands can run with
    /// <see cref="CargoBuildOptions.Offline"/>. Compiles nothing. Of the options only
    /// <see cref="CargoBuildOptions.TargetTriple"/>, <see cref="CargoBuildOptions.Locked"/>,
    /// <see cref="CargoBuildOptions.Offline"/>, <see cref="CargoBuildOptions.Frozen"/>,
    /// <see cref="CargoBuildOptions.Verbose"/>, the environment, timeout and
    /// <see cref="CargoBuildOptions.ExtraArguments"/> apply. Each download is a
    /// <see cref="CargoProgressPhase.Downloading"/> report. See <see cref="RunAsync"/> for the
    /// parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options; <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public Task<CargoCommandResult> FetchAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => RunAsync(projectDirectory, CargoCommand.Fetch, options, progress, cancellationToken);

    /// <summary>
    /// Runs <c>cargo test</c> and parses the libtest output of every test binary. See
    /// <see cref="RunAsync"/> for the parameters and exceptions.
    /// </summary>
    /// <param name="projectDirectory">The project folder or its Cargo.toml.</param>
    /// <param name="options">The command options (<see cref="CargoBuildOptions.NoFailFast"/>,
    /// <see cref="CargoBuildOptions.TestNameFilter"/>); <c>null</c> uses the defaults.</param>
    /// <param name="progress">Receives progress reports; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Kills the cargo process tree when cancelled.</param>
    public async Task<CargoTestResult> TestAsync(string projectDirectory, CargoBuildOptions options = null, IProgress<CargoProgress> progress = null, CancellationToken cancellationToken = default)
        => (CargoTestResult)await RunAsync(projectDirectory, CargoCommand.Test, options, progress, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The cargo subcommand word for a <see cref="CargoCommand"/>.
    /// </summary>
    internal static string SubcommandName(CargoCommand command) => command switch
    {
        CargoCommand.Build => "build",
        CargoCommand.Check => "check",
        CargoCommand.Test => "test",
        CargoCommand.Clean => "clean",
        CargoCommand.Doc => "doc",
        CargoCommand.Clippy => "clippy",
        CargoCommand.Fmt => "fmt",
        CargoCommand.Fetch => "fetch",
        _ => throw new ArgumentOutOfRangeException(nameof(command), command, "Unknown cargo command."),
    };

    /// <summary>
    /// Builds the cargo argument list (without the cargo executable) for a command.
    /// </summary>
    internal static IReadOnlyList<string> BuildArguments(CargoCommand command, CargoBuildOptions options, string manifestPath = null)
    {
        options ??= new CargoBuildOptions();
        if (options.Jobs == 0)
        {
            throw new ArgumentException("CargoBuildOptions.Jobs must not be zero.", nameof(options));
        }

        var compiles = command is CargoCommand.Build or CargoCommand.Check or CargoCommand.Test or CargoCommand.Doc or CargoCommand.Clippy;
        var arguments = new List<string> { SubcommandName(command) };

        if (compiles)
        {
            arguments.Add("--message-format=json");
        }

        if (!string.IsNullOrWhiteSpace(manifestPath))
        {
            arguments.Add("--manifest-path");
            arguments.Add(manifestPath);
        }

        if (options.Verbose)
        {
            arguments.Add("-v");
        }

        if (command is not (CargoCommand.Fmt or CargoCommand.Fetch))
        {
            if (!string.IsNullOrWhiteSpace(options.CustomProfileName))
            {
                arguments.Add("--profile");
                arguments.Add(options.CustomProfileName.Trim());
            }
            else if (options.Profile == CargoProfile.Release)
            {
                arguments.Add("--release");
            }
        }

        if (compiles)
        {
            var features = options.Features.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).ToList();
            if (features.Count > 0)
            {
                arguments.Add("--features");
                arguments.Add(string.Join(",", features));
            }

            if (options.AllFeatures)
            {
                arguments.Add("--all-features");
            }

            if (options.NoDefaultFeatures)
            {
                arguments.Add("--no-default-features");
            }
        }

        if (command != CargoCommand.Fetch)
        {
            AddRepeated(arguments, "-p", options.Packages);
        }

        if (compiles)
        {
            AddRepeated(arguments, "--bin", options.Bins);
            AddRepeated(arguments, "--example", options.Examples);
            if (options.AllTargets && command != CargoCommand.Doc)
            {
                arguments.Add("--all-targets");
            }
        }

        if (command != CargoCommand.Fmt)
        {
            if (!string.IsNullOrWhiteSpace(options.TargetTriple))
            {
                arguments.Add("--target");
                arguments.Add(options.TargetTriple.Trim());
            }

            if (options.Locked)
            {
                arguments.Add("--locked");
            }

            if (options.Offline)
            {
                arguments.Add("--offline");
            }

            if (options.Frozen)
            {
                arguments.Add("--frozen");
            }
        }

        if (compiles && options.Jobs.HasValue)
        {
            arguments.Add("-j");
            arguments.Add(options.Jobs.Value.ToString(CultureInfo.InvariantCulture));
        }

        if (command == CargoCommand.Test)
        {
            if (options.NoFailFast)
            {
                arguments.Add("--no-fail-fast");
            }

            if (!string.IsNullOrWhiteSpace(options.TestNameFilter))
            {
                arguments.Add(options.TestNameFilter);
            }
        }

        if (command == CargoCommand.Doc && options.NoDeps)
        {
            arguments.Add("--no-deps");
        }

        if (command == CargoCommand.Fmt && options.CheckOnly)
        {
            arguments.Add("--check");
        }

        foreach (var extra in options.ExtraArguments)
        {
            if (extra != null)
            {
                arguments.Add(extra);
            }
        }

        return arguments;
    }

    /// <summary>
    /// The environment for a cargo process: the shared probe environment
    /// (<c>RUSTUP_AUTO_INSTALL=0</c>, CARGO_HOME / RUSTUP_HOME overrides, the workbench
    /// variables), <c>CARGO_TERM_COLOR=never</c>, then the command's own variables.
    /// </summary>
    internal static IDictionary<string, string> BuildEnvironment(RustWorkbenchOptions workbenchOptions, CargoBuildOptions options)
    {
        var environment = RustToolchainDetector.BuildProbeEnvironment(workbenchOptions ?? new RustWorkbenchOptions());
        if (!environment.ContainsKey("CARGO_TERM_COLOR"))
        {
            environment["CARGO_TERM_COLOR"] = "never";
        }

        if (options != null)
        {
            foreach (var pair in options.Environment)
            {
                environment[pair.Key] = pair.Value;
            }
        }

        return environment;
    }

    /// <summary>
    /// True when cargo's standard error says the component needed by
    /// <paramref name="command"/> (clippy or rustfmt) is not installed.
    /// </summary>
    internal static bool IsComponentMissing(CargoCommand command, string standardError)
    {
        if (string.IsNullOrEmpty(standardError))
        {
            return false;
        }

        bool NotInstalled(string tool)
            => standardError.Contains("'" + tool + "' is not installed", StringComparison.OrdinalIgnoreCase)
                || standardError.Contains("\"" + tool + "\" is not installed", StringComparison.OrdinalIgnoreCase)
                || standardError.Contains("`" + tool + "` is not installed", StringComparison.OrdinalIgnoreCase);

        return command switch
        {
            CargoCommand.Clippy => standardError.Contains("no such command: `clippy`", StringComparison.Ordinal)
                || NotInstalled("cargo-clippy") || NotInstalled("clippy-driver"),
            CargoCommand.Fmt => standardError.Contains("no such command: `fmt`", StringComparison.Ordinal)
                || NotInstalled("cargo-fmt") || NotInstalled("rustfmt"),
            _ => false,
        };
    }

    private static void ThrowIfComponentMissing(CargoCommand command, int exitCode, string standardError)
    {
        if (!IsComponentMissing(command, standardError))
        {
            return;
        }

        var component = command == CargoCommand.Clippy ? "clippy" : "rustfmt";
        var guidance = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallComponents, HostOperatingSystem.Detect(), new[] { component });
        throw new CargoCommandException(
            $"cargo {SubcommandName(command)} cannot run because the {component} component is not installed for the active toolchain. See Guidance for how to install it.",
            exitCode,
            standardError,
            new[] { guidance });
    }

    private string LocateCargo()
    {
        var locator = new ExecutableLocator(Options);
        var cargo = locator.Locate(RustTool.Cargo);
        if (cargo == null)
        {
            var where = string.IsNullOrWhiteSpace(Options.CargoPath)
                ? "Searched: " + locator.DescribeSearch()
                : $"The configured CargoPath '{Options.CargoPath}' is not an executable file.";
            throw new ToolchainNotFoundException("cargo was not found, so the command cannot run. Install Rust with rustup (https://rustup.rs) or set RustWorkbenchOptions.CargoPath. " + where);
        }

        return cargo;
    }

    private static (string Folder, string ManifestPath) ResolveProject(string projectDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectDirectory))
        {
            throw new ArgumentException("A project folder or Cargo.toml path is required.", nameof(projectDirectory));
        }

        var fullPath = Path.GetFullPath(projectDirectory);
        if (File.Exists(fullPath))
        {
            return (Path.GetDirectoryName(fullPath), fullPath);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"The project folder '{fullPath}' does not exist.");
        }

        return (fullPath, null);
    }

    private static bool PathsEqual(string a, string b)
        => string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void AddRepeated(List<string> arguments, string flag, IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                arguments.Add(flag);
                arguments.Add(value.Trim());
            }
        }
    }

    internal void Publish(CargoProgress report, IProgress<CargoProgress> progress)
    {
        try
        {
            Progress?.Invoke(this, report);
        }
        catch (Exception)
        {
            // A consumer's handler must not break the cargo run (it is called on a pipe-reader thread).
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
}
