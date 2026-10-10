using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// The files a retrieval copied out of a build container.
/// </summary>
public sealed class ArtifactRetrievalResult
{
    /// <summary>The files, in the order they were copied.</summary>
    public IReadOnlyList<RetrievedArtifact> Artifacts { get; init; } = Array.Empty<RetrievedArtifact>();

    /// <summary>The sum of the files' sizes.</summary>
    public long TotalBytes => Artifacts.Sum(a => a.Size);

    /// <summary>The host folder the files were copied into (the layout's sub-folders are below
    /// it).</summary>
    public string HostDirectory { get; init; } = string.Empty;

    /// <summary>Returns the file count, size and folder, for logs.</summary>
    public override string ToString() => $"{Artifacts.Count} file(s), {TotalBytes} byte(s) in {HostDirectory}";

    internal static ArtifactRetrievalResult Combine(string hostDirectory, IEnumerable<ArtifactRetrievalResult> parts)
        => new ArtifactRetrievalResult
        {
            HostDirectory = hostDirectory,
            Artifacts = parts.Where(p => p != null).SelectMany(p => p.Artifacts).ToList(),
        };
}
