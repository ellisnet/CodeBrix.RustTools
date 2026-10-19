using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// A running Linux build container: <c>sleep infinity</c> as its main process, <c>/work</c> as
/// its working directory, cargo's registry and git caches on named volumes that outlive it, and
/// cargo's <c>target</c> folder on a volume of its own. Commands run inside it through
/// <see cref="DockerRustWorkbench.ExecAsync"/>.
/// </summary>
/// <remarks>
/// Disposing the object stops and removes the container and its target volume, unless
/// <see cref="KeepOnDispose"/> is set. Dispose build containers before the workbench that
/// created them; once the workbench is disposed the container can no longer be reached and
/// disposal leaves it in place.
/// </remarks>
public sealed class BuildContainer : IAsyncDisposable
{
    /// <summary>The container's working directory, where project sources go.</summary>
    public const string WorkDirectory = DockerfileGenerator.WorkDirectory;

    /// <summary>Where the cargo registry cache volume is mounted.</summary>
    public const string CargoRegistryPath = "/usr/local/cargo/registry";

    /// <summary>Where the cargo git cache volume is mounted.</summary>
    public const string CargoGitPath = "/usr/local/cargo/git";

    /// <summary>Where the target volume is mounted (cargo's default output folder for a project
    /// in <see cref="WorkDirectory"/>).</summary>
    public const string TargetPath = "/work/target";

    private readonly DockerClient _client;
    private int _removed;
    private int _androidApiLevel = AndroidLinkProfile.DefaultApiLevel;

    internal BuildContainer(
        DockerClient client,
        string id,
        string name,
        RustBuildImage image,
        string cacheVolumeName,
        string targetVolumeName,
        string reuseKey,
        bool keepOnDispose,
        bool wasReused)
    {
        _client = client;
        Id = id;
        Name = name;
        Image = image;
        CacheVolumeName = cacheVolumeName;
        TargetVolumeName = targetVolumeName;
        ReuseKey = reuseKey;
        KeepOnDispose = keepOnDispose;
        WasReused = wasReused;
        IsRunning = true;
    }

    /// <summary>The engine's id for the container.</summary>
    public string Id { get; }

    /// <summary>The container's name.</summary>
    public string Name { get; }

    /// <summary>The image the container runs.</summary>
    public RustBuildImage Image { get; }

    /// <summary>The named volume holding cargo's registry cache, mounted at
    /// <see cref="CargoRegistryPath"/>.</summary>
    public string CacheVolumeName { get; }

    /// <summary>The named volume holding cargo's git cache, mounted at
    /// <see cref="CargoGitPath"/>: <see cref="CacheVolumeName"/> followed by
    /// <c>-git</c>.</summary>
    public string CacheGitVolumeName => CacheVolumeName + "-git";

    /// <summary>The named volume mounted at <see cref="TargetPath"/>.</summary>
    public string TargetVolumeName { get; }

    /// <summary>The hash of the image and settings that identifies the container for
    /// <see cref="BuildContainerOptions.ReuseExisting"/>.</summary>
    public string ReuseKey { get; }

    /// <summary>Whether this object wraps a container that already existed (see
    /// <see cref="BuildContainerOptions.ReuseExisting"/>).</summary>
    public bool WasReused { get; }

    /// <summary>Whether disposing leaves the container (and its target volume) in place.</summary>
    public bool KeepOnDispose { get; set; }

    /// <summary>
    /// The Android API level cargo links the Android targets for in this container (the NDK's
    /// <c>&lt;triple&gt;&lt;level&gt;-clang</c> wrappers, see <c>AndroidLinkProfile</c>). Starts
    /// at <see cref="BuildContainerOptions.AndroidApiLevel"/> (default 24) and may be changed
    /// between cargo runs.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Set below 21 (the first level with 64-bit
    /// Android targets).</exception>
    public int AndroidApiLevel
    {
        get => _androidApiLevel;
        set
        {
            if (value < 21)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "64-bit Android targets start at API level 21.");
            }

            _androidApiLevel = value;
        }
    }

    /// <summary>Whether the container was running when last observed (at start, after
    /// <see cref="RefreshStateAsync"/>, or after removal).</summary>
    public bool IsRunning { get; private set; }

    /// <summary>Whether <see cref="StopAndRemoveAsync"/> (or disposal) has removed the
    /// container.</summary>
    public bool IsRemoved => Volatile.Read(ref _removed) != 0;

    internal DockerClient Client => _client;

    /// <summary>
    /// Asks the engine whether the container is running, and updates
    /// <see cref="IsRunning"/>.
    /// </summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>Whether the container exists and is running.</returns>
    public async Task<bool> RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var inspect = await _client.Containers.InspectAsync(Id, cancellationToken).ConfigureAwait(false);
            IsRunning = inspect.IsRunning;
        }
        catch (DockerContainerNotFoundException)
        {
            IsRunning = false;
        }

        return IsRunning;
    }

    /// <summary>
    /// Stops and removes the container (it is force-removed, which kills every process in it:
    /// the main process is <c>sleep infinity</c>, so there is nothing to shut down gracefully)
    /// and, by default, its
    /// target volume. The cargo cache volumes are never removed here; use
    /// <see cref="DockerRustWorkbench.CleanupAsync"/>. Calling it again does nothing.
    /// </summary>
    /// <param name="removeTargetVolume">Whether to remove the target volume as well. A target
    /// volume still in use by another container is left in place.</param>
    /// <param name="cancellationToken">Cancels the requests.</param>
    public async Task StopAndRemoveAsync(bool removeTargetVolume = true, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _removed, 1) != 0)
        {
            return;
        }

        try
        {
            try
            {
                await _client.Containers.RemoveAsync(Id, force: true, removeVolumes: false, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerContainerNotFoundException)
            {
                // Already removed.
            }
        }
        catch
        {
            Interlocked.Exchange(ref _removed, 0);
            throw;
        }

        IsRunning = false;
        if (removeTargetVolume && !string.IsNullOrEmpty(TargetVolumeName))
        {
            try
            {
                await _client.Volumes.RemoveAsync(TargetVolumeName, force: false, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound || ex.StatusCode == HttpStatusCode.Conflict)
            {
                // Gone already, or still mounted by another container that reuses it.
            }
        }
    }

    /// <summary>
    /// Stops and removes the container and its target volume unless
    /// <see cref="KeepOnDispose"/> is set. Docker errors during disposal are swallowed; the
    /// labels let <see cref="DockerRustWorkbench.CleanupAsync"/> find anything left behind.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (KeepOnDispose || IsRemoved)
        {
            return;
        }

        try
        {
            await StopAndRemoveAsync(removeTargetVolume: true, CancellationToken.None).ConfigureAwait(false);
        }
        catch (DockerException)
        {
            // Best effort; see the summary.
        }
        catch (ObjectDisposedException)
        {
            // The workbench (and its Docker client) was disposed first.
        }
    }

    /// <summary>Returns the container's name and short id.</summary>
    public override string ToString() => $"{Name} ({(Id != null && Id.Length > 12 ? Id.Substring(0, 12) : Id)})";
}
