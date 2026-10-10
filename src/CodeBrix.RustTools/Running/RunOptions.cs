using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Running;

/// <summary>
/// Options for running a built program with <see cref="BuiltBinaryRunner"/>.
/// </summary>
public sealed class RunOptions
{
    /// <summary>
    /// The program's arguments, one element per argument (no shell quoting is needed).
    /// </summary>
    public IList<string> Arguments { get; } = new List<string>();

    /// <summary>
    /// The folder to run in; <c>null</c> (the default) uses the executable's folder.
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables for the program, applied after
    /// <see cref="RustWorkbenchOptions.Environment"/>; a <c>null</c> value removes a variable.
    /// </summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The maximum run time; <c>null</c> (the default) for no limit. On expiry the program and
    /// its child processes are killed and <see cref="RunResult.TimedOut"/> is set.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Text written to the program's standard input, which is then closed; <c>null</c> (the
    /// default) closes standard input immediately, so a program that reads it sees end of input.
    /// </summary>
    public string StandardInput { get; set; }

    /// <summary>
    /// When true (the default) standard output and standard error are captured into the
    /// <see cref="RunResult"/>.
    /// </summary>
    public bool CaptureOutput { get; set; } = true;

    /// <summary>
    /// When true (the default) cancellation kills the program and throws
    /// <see cref="OperationCanceledException"/>; when false a result with
    /// <see cref="RunResult.WasCancelled"/> set is returned instead.
    /// </summary>
    public bool ThrowOnCancellation { get; set; } = true;

    /// <summary>
    /// Called for every line the program writes to standard output (on a thread-pool thread);
    /// <c>null</c> by default.
    /// </summary>
    public Action<string> OnStandardOutputLine { get; set; }

    /// <summary>
    /// Called for every line the program writes to standard error (on a thread-pool thread);
    /// <c>null</c> by default.
    /// </summary>
    public Action<string> OnStandardErrorLine { get; set; }
}
