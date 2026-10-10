using System;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// The outcome of a <see cref="ProcessRunner"/> run.
/// </summary>
internal sealed class ProcessRunResult
{
    /// <summary>
    /// The process exit code; -1 when the process was killed (timeout or cancellation).
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Everything written to standard output (lines joined with '\n'); empty when capture is off.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// Everything written to standard error (lines joined with '\n'); empty when capture is off.
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// Wall-clock time from start to exit.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// True when the process was killed because the request's timeout elapsed.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// True when the process was killed because the cancellation token fired.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// The display form of the command line that was run.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// True when the process ran to completion with exit code 0.
    /// </summary>
    public bool Succeeded => !TimedOut && !WasCancelled && ExitCode == 0;
}
