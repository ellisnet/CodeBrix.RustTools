using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// Describes one external process for <see cref="ProcessRunner"/> to start.
/// </summary>
internal sealed class ProcessRunRequest
{
    /// <summary>
    /// Creates a request for the given executable.
    /// </summary>
    public ProcessRunRequest(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("An executable file name is required.", nameof(fileName));
        }

        FileName = fileName;
    }

    /// <summary>
    /// The executable to run (a full path, or a name resolved by the operating system).
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// The arguments, one element per argument; quoting is handled by the runtime.
    /// </summary>
    public IList<string> Arguments { get; } = new List<string>();

    /// <summary>
    /// The working directory, or <c>null</c> for the current directory.
    /// </summary>
    public string WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables to set; a <c>null</c> value removes the variable.
    /// </summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// The maximum run time; <c>null</c> for no limit. On expiry the whole process tree is
    /// killed and <see cref="ProcessRunResult.TimedOut"/> is set (no exception is thrown).
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Called on a thread-pool thread for every line written to standard output.
    /// </summary>
    public Action<string> OnStandardOutputLine { get; set; }

    /// <summary>
    /// Called on a thread-pool thread for every line written to standard error.
    /// </summary>
    public Action<string> OnStandardErrorLine { get; set; }

    /// <summary>
    /// Text written to the process's standard input before it is closed; <c>null</c> closes
    /// standard input immediately so the child can never block waiting for input.
    /// </summary>
    public string StandardInput { get; set; }

    /// <summary>
    /// When <c>true</c> (the default) the output lines are also accumulated into
    /// <see cref="ProcessRunResult.StandardOutput"/> / <see cref="ProcessRunResult.StandardError"/>.
    /// </summary>
    public bool CaptureOutput { get; set; } = true;

    /// <summary>
    /// When <c>true</c> (the default) cancellation kills the process tree and then throws
    /// <see cref="OperationCanceledException"/>; when <c>false</c> it returns a result with
    /// <see cref="ProcessRunResult.WasCancelled"/> set instead.
    /// </summary>
    public bool ThrowOnCancellation { get; set; } = true;

    /// <summary>
    /// Builds a display form of the command line (for logs and results; not for execution).
    /// </summary>
    public string ToCommandLine()
    {
        var parts = new List<string> { Quote(FileName) };
        foreach (var argument in Arguments)
        {
            parts.Add(Quote(argument));
        }

        return string.Join(" ", parts);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        return value.IndexOfAny(new[] { ' ', '\t', '"' }) >= 0
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }
}
