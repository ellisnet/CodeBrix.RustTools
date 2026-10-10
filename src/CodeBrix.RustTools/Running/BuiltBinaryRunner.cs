using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Running;

/// <summary>
/// Runs a program that cargo built (or any executable) with arguments, a working folder,
/// environment variables, standard input, a timeout and cancellation; cancellation and timeouts
/// kill the program together with its child processes.
/// </summary>
public sealed class BuiltBinaryRunner
{
    /// <summary>
    /// Creates a runner.
    /// </summary>
    /// <param name="options">Shared settings (its <see cref="RustWorkbenchOptions.Environment"/>
    /// is applied to every program); <c>null</c> uses the defaults.</param>
    public BuiltBinaryRunner(RustWorkbenchOptions options = null)
    {
        Options = options ?? new RustWorkbenchOptions();
    }

    /// <summary>
    /// The settings this runner uses.
    /// </summary>
    public RustWorkbenchOptions Options { get; }

    /// <summary>
    /// Runs the executable of a build artifact (see <see cref="BuildArtifact.Executable"/>).
    /// </summary>
    /// <param name="artifact">An artifact from <see cref="CargoCommandResult.Artifacts"/>.</param>
    /// <param name="options">Arguments, folder, environment, input and timeout; <c>null</c> uses
    /// the defaults.</param>
    /// <param name="cancellationToken">Kills the program when cancelled.</param>
    /// <exception cref="ArgumentNullException"><paramref name="artifact"/> is null.</exception>
    /// <exception cref="RustToolsException">The artifact has no executable (a library or build
    /// script), the file no longer exists, or it could not be started.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired
    /// and <see cref="RunOptions.ThrowOnCancellation"/> is true.</exception>
    public Task<RunResult> RunAsync(BuildArtifact artifact, RunOptions options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        if (string.IsNullOrWhiteSpace(artifact.Executable))
        {
            throw new RustToolsException($"The artifact '{artifact}' has no executable; only binaries, examples and test harnesses can be run.");
        }

        return RunAsync(artifact.Executable, options, cancellationToken);
    }

    /// <summary>
    /// Runs an executable file.
    /// </summary>
    /// <param name="executablePath">The program to run.</param>
    /// <param name="options">Arguments, folder, environment, input and timeout; <c>null</c> uses
    /// the defaults.</param>
    /// <param name="cancellationToken">Kills the program when cancelled.</param>
    /// <exception cref="ArgumentException"><paramref name="executablePath"/> is blank.</exception>
    /// <exception cref="RustToolsException">The file does not exist or could not be started.</exception>
    /// <exception cref="DirectoryNotFoundException">The working folder does not exist.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired
    /// and <see cref="RunOptions.ThrowOnCancellation"/> is true.</exception>
    public async Task<RunResult> RunAsync(string executablePath, RunOptions options = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new ArgumentException("An executable path is required.", nameof(executablePath));
        }

        options ??= new RunOptions();
        var fullPath = Path.GetFullPath(executablePath);
        if (!File.Exists(fullPath))
        {
            throw new RustToolsException($"The executable '{fullPath}' does not exist. Build the project first.");
        }

        var workingDirectory = string.IsNullOrWhiteSpace(options.WorkingDirectory)
            ? Path.GetDirectoryName(fullPath)
            : Path.GetFullPath(options.WorkingDirectory);
        if (!Directory.Exists(workingDirectory))
        {
            throw new DirectoryNotFoundException($"The working directory '{workingDirectory}' does not exist.");
        }

        var request = new ProcessRunRequest(fullPath)
        {
            WorkingDirectory = workingDirectory,
            Timeout = options.Timeout,
            StandardInput = options.StandardInput,
            CaptureOutput = options.CaptureOutput,
            ThrowOnCancellation = options.ThrowOnCancellation,
            OnStandardOutputLine = options.OnStandardOutputLine,
            OnStandardErrorLine = options.OnStandardErrorLine,
        };
        foreach (var argument in options.Arguments)
        {
            request.Arguments.Add(argument ?? string.Empty);
        }

        foreach (var pair in Options.Environment)
        {
            request.Environment[pair.Key] = pair.Value;
        }

        foreach (var pair in options.Environment)
        {
            request.Environment[pair.Key] = pair.Value;
        }

        var run = await ProcessRunner.RunAsync(request, cancellationToken).ConfigureAwait(false);
        return new RunResult
        {
            ExitCode = run.ExitCode,
            StandardOutput = run.StandardOutput,
            StandardError = run.StandardError,
            Duration = run.Duration,
            TimedOut = run.TimedOut,
            WasCancelled = run.WasCancelled,
            CommandLine = run.CommandLine,
        };
    }
}
