using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// What was copied when a project folder was copied into a build container.
/// </summary>
public sealed class SourceCopyResult
{
    /// <summary>The host folder whose contents were copied.</summary>
    public string HostDirectory { get; init; } = string.Empty;

    /// <summary>The container folder that received them (<c>/work</c>).</summary>
    public string ContainerDirectory { get; init; } = string.Empty;

    /// <summary>The number of files copied.</summary>
    public long FileCount { get; init; }

    /// <summary>The number of bytes copied.</summary>
    public long TotalBytes { get; init; }

    /// <summary>The exclusion patterns that applied (the workbench defaults plus the call's
    /// own).</summary>
    public IReadOnlyList<string> ExcludePatterns { get; init; } = Array.Empty<string>();

    /// <summary>How long the copy took.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Returns the counts and folders, for logs.</summary>
    public override string ToString() => $"{FileCount} file(s), {TotalBytes} byte(s) from {HostDirectory} to {ContainerDirectory}";
}
