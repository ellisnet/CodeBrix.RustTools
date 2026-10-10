using System;

namespace CodeBrix.RustTools.Running;

/// <summary>
/// The outcome of running a built program with <see cref="BuiltBinaryRunner"/>.
/// </summary>
public sealed class RunResult
{
    /// <summary>
    /// The program's exit code; -1 when it was killed (timeout or cancellation).
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Everything the program wrote to standard output (lines joined with '\n'); empty when
    /// capture was off.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Everything the program wrote to standard error (lines joined with '\n'); empty when
    /// capture was off.
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// Wall-clock run time.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// True when the program was killed because <see cref="RunOptions.Timeout"/> elapsed.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// True when the program was killed because the cancellation token fired (only when
    /// <see cref="RunOptions.ThrowOnCancellation"/> is false).
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// The display form of the command line that was run.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// True when the program ran to completion with exit code 0.
    /// </summary>
    public bool Succeeded => !TimedOut && !WasCancelled && ExitCode == 0;

    /// <summary>
    /// Returns a one-line summary.
    /// </summary>
    public override string ToString()
        => TimedOut ? $"{CommandLine}: timed out after {Duration.TotalSeconds:0.0} s"
            : WasCancelled ? $"{CommandLine}: cancelled"
            : $"{CommandLine}: exit code {ExitCode} in {Duration.TotalSeconds:0.0} s";
}
