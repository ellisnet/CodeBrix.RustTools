using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// What <see cref="DockerRustWorkbench.CleanupAsync"/> removed, and what it could not.
/// </summary>
public sealed class CleanupResult
{
    /// <summary>The ids of the containers that were removed.</summary>
    public IReadOnlyList<string> RemovedContainers { get; init; } = Array.Empty<string>();

    /// <summary>The ids of the images that were removed.</summary>
    public IReadOnlyList<string> RemovedImages { get; init; } = Array.Empty<string>();

    /// <summary>The names of the volumes that were removed.</summary>
    public IReadOnlyList<string> RemovedVolumes { get; init; } = Array.Empty<string>();

    /// <summary>One message per resource that could not be removed (for example a volume still
    /// used by a container that was not part of the cleanup).</summary>
    public IReadOnlyList<string> Failures { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether <c>docker builder prune --all --force</c> ran and succeeded
    /// (<see cref="CleanupOptions.PruneBuildCache"/>). <c>false</c> when it was not asked for or
    /// failed (the reason is then in <see cref="Failures"/>).
    /// </summary>
    public bool BuildCachePruned { get; init; }

    /// <summary>
    /// The reclaimed space the build-cache prune reported, as the docker CLI printed it (for
    /// example <c>1.234GB</c>, or <c>0B</c>). <c>null</c> when no prune ran or the output had no
    /// total.
    /// </summary>
    public string BuildCacheReclaimedSpace { get; init; }

    /// <summary>
    /// <see cref="BuildCacheReclaimedSpace"/> as a byte count (the CLI's decimal units are
    /// powers of 1000, so the value is as precise as the printed text). <c>null</c> when it is
    /// not known.
    /// </summary>
    public long? BuildCacheReclaimedBytes { get; init; }

    /// <summary>
    /// The raw standard output and standard error of the build-cache prune. <c>null</c> when no
    /// prune ran or the docker CLI could not be started.
    /// </summary>
    public string BuildCachePruneOutput { get; init; }

    /// <summary>Whether everything matched was removed.</summary>
    public bool Succeeded => Failures.Count == 0;
}
