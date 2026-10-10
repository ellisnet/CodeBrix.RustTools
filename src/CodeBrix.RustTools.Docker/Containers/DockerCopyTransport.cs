using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// The <see cref="IContainerFileTransport"/> over the Docker Engine API's archive endpoint
/// (<c>/containers/{id}/archive</c>), through the Docker client's copy operations. No
/// <c>docker</c> executable and no shared host folders are involved, so it works with remote
/// (<c>ssh://</c>, <c>tcp://</c>) engines too.
/// </summary>
internal sealed class DockerCopyTransport : IContainerFileTransport
{
    private readonly DockerClient _client;
    private readonly string _containerId;

    public DockerCopyTransport(DockerClient client, string containerId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _containerId = string.IsNullOrWhiteSpace(containerId) ? throw new ArgumentException("A container id is required.", nameof(containerId)) : containerId;
    }

    /// <inheritdoc />
    public async Task<SourceCopyResult> CopyDirectoryInAsync(string hostDirectory, string containerPath, IReadOnlyList<string> excludePatterns, IProgress<SourceCopyProgress> progress, CancellationToken cancellationToken)
    {
        var source = Path.GetFullPath(hostDirectory);
        if (!Directory.Exists(source))
        {
            throw new DirectoryNotFoundException($"The folder '{source}' does not exist.");
        }

        // The engine extracts INTO an existing folder and never creates it.
        var mkdir = await ContainerCommandRunner.RunAsync(_client, _containerId, new[] { "mkdir", "-p", containerPath }, "/", null, cancellationToken).ConfigureAwait(false);
        if (!mkdir.Succeeded)
        {
            throw new ContainerBuildException(
                $"The folder '{containerPath}' could not be created in the build container.",
                mkdir.Command,
                mkdir.ExitCode,
                mkdir.StandardOutput,
                mkdir.StandardError);
        }

        var patterns = (excludePatterns ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        long files = 0;
        long bytes = 0;
        var counter = new CountingProgress((count, total, path) =>
        {
            files = count;
            bytes = total;
            progress?.Report(new SourceCopyProgress { FilesCopied = count, BytesCopied = total, CurrentPath = path ?? string.Empty });
        });
        var options = new CopyToContainerOptions
        {
            Progress = counter,
        };
        foreach (var pattern in patterns)
        {
            options.ExcludePatterns.Add(pattern);
        }

        var stopwatch = Stopwatch.StartNew();
        await _client.Containers.CopyDirectoryToContainerAsync(_containerId, source, containerPath, options, cancellationToken).ConfigureAwait(false);
        return new SourceCopyResult
        {
            HostDirectory = source,
            ContainerDirectory = containerPath,
            FileCount = files,
            TotalBytes = bytes,
            ExcludePatterns = patterns,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TransportedFile>> CopyOutAsync(IReadOnlyList<string> containerPaths, string hostDirectory, bool overwrite, CancellationToken cancellationToken)
    {
        if (containerPaths == null)
        {
            throw new ArgumentNullException(nameof(containerPaths));
        }

        var destination = Path.GetFullPath(hostDirectory);
        Directory.CreateDirectory(destination);
        var files = new List<TransportedFile>();
        foreach (var containerPath in containerPaths)
        {
            CopyFromContainerResult result;
            try
            {
                result = await _client.Containers.CopyFromContainerToDirectoryAsync(
                    _containerId,
                    containerPath,
                    destination,
                    new CopyFromContainerOptions { StripRootFolder = true, Overwrite = overwrite },
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DockerContainerNotFoundException ex)
            {
                throw new ArtifactRetrievalException($"The build container no longer exists, so '{containerPath}' cannot be retrieved.", containerPath, ex);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                throw new ArtifactRetrievalException($"'{containerPath}' does not exist in the build container.", containerPath, ex);
            }
            catch (Exception ex) when (ex is DockerException || ex is IOException || ex is UnauthorizedAccessException)
            {
                throw new ArtifactRetrievalException($"'{containerPath}' could not be copied to '{destination}': {ex.Message}", containerPath, ex);
            }

            if (result.SkippedEntries.Count > 0 && !overwrite)
            {
                throw new ArtifactRetrievalException(
                    $"'{containerPath}' was not copied because '{string.Join("', '", result.SkippedEntries)}' already exists in '{destination}' and overwriting is off.",
                    containerPath);
            }

            foreach (var entry in result.Entries.Where(e => !e.IsDirectory))
            {
                files.Add(new TransportedFile
                {
                    ContainerPath = entry.ContainerPath,
                    HostPath = entry.HostPath,
                    Size = entry.Size,
                    Mode = (int)entry.Mode,
                });
            }
        }

        return files;
    }

    /// <inheritdoc />
    public async Task<ContainerPathStat> StatAsync(string containerPath, CancellationToken cancellationToken)
    {
        try
        {
            var stat = await _client.Containers.StatPathAsync(_containerId, containerPath, cancellationToken).ConfigureAwait(false);
            return new ContainerPathStat
            {
                Exists = true,
                Name = stat.Name ?? string.Empty,
                Size = stat.Size,
                UnixMode = (int)stat.PermissionBits,
                IsDirectory = stat.IsDirectory,
                LinkTarget = stat.IsSymlink ? stat.LinkTarget : null,
            };
        }
        catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound && ex is not DockerContainerNotFoundException)
        {
            return new ContainerPathStat { Exists = false, Name = Path.GetFileName(containerPath.TrimEnd('/')) };
        }
    }

    /// <summary>
    /// Runs its callback synchronously on the reporting thread, so the counts are complete when
    /// the copy's task finishes (<see cref="Progress{T}"/> would post them later).
    /// </summary>
    private sealed class CountingProgress : IProgress<CopyProgress>
    {
        private readonly Action<long, long, string> _callback;
        private readonly object _gate = new object();

        public CountingProgress(Action<long, long, string> callback)
        {
            _callback = callback;
        }

        public void Report(CopyProgress value)
        {
            lock (_gate)
            {
                _callback(value.FilesCopied, value.BytesCopied, value.CurrentPath);
            }
        }
    }
}
