using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// The outcome of one command run inside a build container.
/// </summary>
internal sealed class ContainerCommandResult
{
    /// <summary>The command and its arguments.</summary>
    public IReadOnlyList<string> Command { get; init; } = Array.Empty<string>();

    /// <summary>The command's exit code.</summary>
    public long ExitCode { get; init; }

    /// <summary>Everything the command wrote to standard output.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>Everything the command wrote to standard error.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>How long the command ran.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Whether the exit code is 0.</summary>
    public bool Succeeded => ExitCode == 0;
}
