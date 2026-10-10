using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Moves files between the host and one build container. The implementation
/// (<see cref="DockerCopyTransport"/>) uses the Docker client's archive (copy-in / copy-out)
/// operations; this interface keeps the rest of the library independent of how the bytes
/// travel.
/// </summary>
internal interface IContainerFileTransport
{
    /// <summary>
    /// Copies the contents of a host folder into the container, skipping every file or folder
    /// whose relative path matches one of the exclusion patterns.
    /// </summary>
    /// <param name="hostDirectory">The host folder whose contents are copied.</param>
    /// <param name="containerPath">The container folder that receives them (created when
    /// missing).</param>
    /// <param name="excludePatterns">Glob patterns to skip; a pattern without a slash (for
    /// example <c>.git</c> or <c>target</c>) matches a name at any depth.</param>
    /// <param name="progress">Receives one report per file sent; may be <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>The number of files and bytes copied.</returns>
    Task<SourceCopyResult> CopyDirectoryInAsync(string hostDirectory, string containerPath, IReadOnlyList<string> excludePatterns, IProgress<SourceCopyProgress> progress, CancellationToken cancellationToken);

    /// <summary>
    /// Copies files or folders out of the container into a host folder. A folder's contents land
    /// directly in <paramref name="hostDirectory"/> (its own name is not repeated); a file keeps
    /// its name.
    /// </summary>
    /// <param name="containerPaths">The container paths to copy.</param>
    /// <param name="hostDirectory">The host folder that receives them (created when
    /// missing).</param>
    /// <param name="overwrite">Whether existing host files are replaced.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>Every file written on the host, with its Unix mode from the container.</returns>
    Task<IReadOnlyList<TransportedFile>> CopyOutAsync(IReadOnlyList<string> containerPaths, string hostDirectory, bool overwrite, CancellationToken cancellationToken);

    /// <summary>
    /// Describes a path inside the container without copying it.
    /// </summary>
    /// <param name="containerPath">The path to describe.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The description; <see cref="ContainerPathStat.Exists"/> is <c>false</c> when the
    /// path does not exist.</returns>
    Task<ContainerPathStat> StatAsync(string containerPath, CancellationToken cancellationToken);
}
