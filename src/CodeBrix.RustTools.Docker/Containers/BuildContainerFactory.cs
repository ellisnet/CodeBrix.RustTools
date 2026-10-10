using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Internal;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Creates (or finds again) and starts build containers: labelled, running
/// <c>sleep infinity</c>, with the cargo cache and target volumes mounted.
/// </summary>
internal static class BuildContainerFactory
{
    /// <summary>The environment every build container gets unless the options override
    /// it.</summary>
    public static readonly IReadOnlyDictionary<string, string> StandardEnvironment = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CARGO_TERM_COLOR"] = "never",
        ["RUSTUP_AUTO_INSTALL"] = "0",
    };

    public static async Task<BuildContainer> StartAsync(
        DockerClient client,
        DockerRustWorkbenchOptions workbenchOptions,
        ResourceLabels labels,
        RustBuildImage image,
        BuildContainerOptions options,
        CancellationToken cancellationToken)
    {
        var reuseKey = ComputeReuseKey(image, options, workbenchOptions.CacheVolumeName);
        var keep = options.KeepOnDispose ?? workbenchOptions.KeepContainerByDefault;

        if (options.ReuseExisting)
        {
            var reused = await TryReuseAsync(client, workbenchOptions, labels, image, options, reuseKey, keep, cancellationToken).ConfigureAwait(false);
            if (reused != null)
            {
                return reused;
            }
        }

        var name = ResolveName(workbenchOptions, options, reuseKey);
        var targetVolume = ResolveTargetVolume(options, name);
        await EnsureVolumeAsync(client, workbenchOptions.CacheVolumeName, labels.For(ResourceLabels.KindCargoCache), cancellationToken).ConfigureAwait(false);
        await EnsureVolumeAsync(client, workbenchOptions.CacheVolumeName + "-git", labels.For(ResourceLabels.KindCargoCache), cancellationToken).ConfigureAwait(false);
        await EnsureVolumeAsync(client, targetVolume, labels.For(ResourceLabels.KindTarget), cancellationToken).ConfigureAwait(false);

        var spec = BuildSpec(image, options, labels, workbenchOptions.CacheVolumeName, name, targetVolume, reuseKey);
        var id = await client.Containers.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
        try
        {
            await client.Containers.StartAsync(id, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            try
            {
                await client.Containers.RemoveAsync(id, force: true, removeVolumes: false, CancellationToken.None).ConfigureAwait(false);
            }
            catch (DockerException)
            {
                // Report the start failure, not the clean-up failure.
            }

            throw;
        }

        return new BuildContainer(client, id, name, image, workbenchOptions.CacheVolumeName, targetVolume, reuseKey, keep, wasReused: false);
    }

    /// <summary>
    /// The container specification for a build container (no engine calls).
    /// </summary>
    public static ContainerSpec BuildSpec(
        RustBuildImage image,
        BuildContainerOptions options,
        ResourceLabels labels,
        string cacheVolumeName,
        string name,
        string targetVolumeName,
        string reuseKey)
    {
        var spec = new ContainerSpec
        {
            Image = image.Reference,
            Name = name,
            Command = new[] { "sleep", "infinity" },
            WorkingDir = BuildContainer.WorkDirectory,
            Limits = options.ResourceLimits,
        };

        foreach (var pair in MergeEnvironment(options.Env))
        {
            spec.Env.Add(pair.Key + "=" + pair.Value);
        }

        foreach (var pair in labels.For(ResourceLabels.KindContainer))
        {
            spec.Labels[pair.Key] = pair.Value;
        }

        spec.Labels[labels.ReuseKeyKey] = reuseKey;
        spec.Labels[labels.SpecHashKey] = image.SpecHash ?? string.Empty;
        spec.Mounts.Add(MountSpec.Volume(cacheVolumeName, BuildContainer.CargoRegistryPath));
        spec.Mounts.Add(MountSpec.Volume(cacheVolumeName + "-git", BuildContainer.CargoGitPath));
        spec.Mounts.Add(MountSpec.Volume(targetVolumeName, BuildContainer.TargetPath));
        if (options.Mounts != null)
        {
            foreach (var mount in options.Mounts.Where(m => m != null))
            {
                spec.Mounts.Add(mount);
            }
        }

        return spec;
    }

    /// <summary>
    /// The standard environment overlaid with the caller's variables, sorted by name.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>> MergeEnvironment(IDictionary<string, string> extra)
    {
        var merged = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in StandardEnvironment)
        {
            merged[pair.Key] = pair.Value;
        }

        if (extra != null)
        {
            foreach (var pair in extra)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Key.Contains('='))
                {
                    throw new ArgumentException($"'{pair.Key}' is not a valid environment variable name.", nameof(extra));
                }

                merged[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        return merged.ToList();
    }

    /// <summary>
    /// A hash of everything that makes two build containers interchangeable: the image, the
    /// cache volume, the requested name and target volume, the environment, the extra mounts
    /// and the resource limits.
    /// </summary>
    public static string ComputeReuseKey(RustBuildImage image, BuildContainerOptions options, string cacheVolumeName)
    {
        var text = new StringBuilder();
        text.Append("image=").Append(image.Reference).Append('@').Append(image.ImageId).Append('\n');
        text.Append("cache=").Append(cacheVolumeName).Append('\n');
        text.Append("name=").Append(options.Name ?? string.Empty).Append('\n');
        text.Append("target=").Append(options.TargetVolumeName ?? string.Empty).Append('\n');
        foreach (var pair in MergeEnvironment(options.Env))
        {
            text.Append("env=").Append(pair.Key).Append('=').Append(pair.Value).Append('\n');
        }

        if (options.Mounts != null)
        {
            foreach (var mount in options.Mounts.Where(m => m != null))
            {
                text.Append("mount=").Append(mount.Kind).Append('|').Append(mount.Source).Append('|')
                    .Append(mount.Target).Append('|').Append(mount.ReadOnly).Append('|')
                    .Append(mount.TmpfsSizeBytes?.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        var limits = options.ResourceLimits;
        if (limits != null && !limits.IsEmpty)
        {
            text.Append("limits=")
                .Append(limits.Cpus?.ToString("R", CultureInfo.InvariantCulture)).Append('|')
                .Append(limits.CpusetCpus).Append('|')
                .Append(limits.CpuShares?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(limits.MemoryBytes?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(limits.MemoryReservationBytes?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(limits.MemorySwapBytes?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(limits.PidsLimit?.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return Hashing.Short(text.ToString());
    }

    public static string ResolveName(DockerRustWorkbenchOptions workbenchOptions, BuildContainerOptions options, string reuseKey)
    {
        if (!string.IsNullOrWhiteSpace(options.Name))
        {
            return options.Name.Trim();
        }

        var suffix = options.ReuseExisting ? reuseKey : Guid.NewGuid().ToString("N").Substring(0, 12);
        return workbenchOptions.ContainerNamePrefix + "-" + suffix;
    }

    public static string ResolveTargetVolume(BuildContainerOptions options, string containerName)
        => string.IsNullOrWhiteSpace(options.TargetVolumeName) ? containerName + "-target" : options.TargetVolumeName.Trim();

    private static async Task<BuildContainer> TryReuseAsync(
        DockerClient client,
        DockerRustWorkbenchOptions workbenchOptions,
        ResourceLabels labels,
        RustBuildImage image,
        BuildContainerOptions options,
        string reuseKey,
        bool keep,
        CancellationToken cancellationToken)
    {
        var filter = labels.PresenceFilter();
        filter[labels.ReuseKeyKey] = reuseKey;
        var candidates = await client.Containers.ListAsync(all: true, filter, cancellationToken).ConfigureAwait(false);
        var match = candidates.FirstOrDefault(c => c.IsRunning) ?? candidates.FirstOrDefault();
        if (match == null)
        {
            return null;
        }

        if (!match.IsRunning)
        {
            try
            {
                await client.Containers.StartAsync(match.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerApiException)
            {
                await client.Containers.RemoveAsync(match.Id, force: true, removeVolumes: false, cancellationToken).ConfigureAwait(false);
                return null;
            }
        }

        var inspect = await client.Containers.InspectAsync(match.Id, cancellationToken).ConfigureAwait(false);
        var target = inspect.Mounts?.FirstOrDefault(m => m.Destination == BuildContainer.TargetPath)?.Name
                     ?? ResolveTargetVolume(options, inspect.DisplayName);
        return new BuildContainer(client, inspect.Id, inspect.DisplayName, image, workbenchOptions.CacheVolumeName, target, reuseKey, keep, wasReused: true);
    }

    private static async Task EnsureVolumeAsync(DockerClient client, string name, IDictionary<string, string> volumeLabels, CancellationToken cancellationToken)
        => await client.Volumes.CreateAsync(name, volumeLabels, cancellationToken).ConfigureAwait(false);
}
