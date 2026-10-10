namespace CodeBrix.RustTools.Docker;

/// <summary>
/// What <see cref="DockerRustWorkbench.CleanupAsync"/> removes. Only resources carrying the
/// workbench's labels are ever touched; the base image (for example <c>rust:latest</c>) is never
/// removed.
/// </summary>
public sealed class CleanupOptions
{
    /// <summary>Remove build containers (running or stopped). Default <c>true</c>.</summary>
    public bool RemoveContainers { get; set; } = true;

    /// <summary>Remove derived build images. Default <c>false</c> (they are what makes the next
    /// build start quickly).</summary>
    public bool RemoveImages { get; set; }

    /// <summary>Remove the cargo cache volumes and target volumes. Default <c>false</c> (the
    /// cache is what makes repeat builds fast).</summary>
    public bool RemoveVolumes { get; set; }

    /// <summary>
    /// Remove only what THIS workbench instance created (matched by its
    /// <see cref="DockerRustWorkbench.InstanceId"/> label). Default <c>false</c>: everything
    /// with the workbench's label prefix is removed, whichever workbench or process created
    /// it.
    /// </summary>
    public bool OnlyThisWorkbench { get; set; }

    /// <summary>
    /// Also run <c>docker builder prune --all --force</c>, through the same docker CLI (and endpoint)
    /// that builds the derived images, after the other removals. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// THE PRUNE IS ENGINE-WIDE. It removes every unused BuildKit build-cache entry on the
    /// engine (or the builder the docker CLI selects) - this library's AND every other
    /// project's, whatever their labels - because build-cache entries carry no labels and
    /// <c>docker builder prune --filter</c> cannot select by label. It ignores
    /// <see cref="OnlyThisWorkbench"/> and the workbench's label prefix. That is why it is off by
    /// default: other builds on the same engine lose their cache and run slower next time.
    /// </para>
    /// <para>
    /// <see cref="RemoveImages"/> does NOT reclaim this cache: the apt, llvm-mingw and Android
    /// NDK layers of a derived image (hundreds of MB to several GB per spec) stay in
    /// <c>docker buildx du</c> after the image is removed. Only this prune frees them.
    /// </para>
    /// <para>
    /// A docker CLI that is missing, cannot be started or exits non-zero is reported in
    /// <see cref="CleanupResult.Failures"/> (with <see cref="CleanupResult.BuildCachePruned"/>
    /// <c>false</c>), not thrown. The outcome is in <see cref="CleanupResult.BuildCachePruned"/>,
    /// <see cref="CleanupResult.BuildCacheReclaimedSpace"/>,
    /// <see cref="CleanupResult.BuildCacheReclaimedBytes"/> and
    /// <see cref="CleanupResult.BuildCachePruneOutput"/>.
    /// </para>
    /// </remarks>
    public bool PruneBuildCache { get; set; }
}
