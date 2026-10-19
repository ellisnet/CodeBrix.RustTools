using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Internal;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// Makes sure the image a spec describes exists on the engine: pulls the base image when it is
/// missing, uses it directly when the spec adds no layers, and otherwise builds (once) the
/// derived image tagged with the hash of its generated Dockerfile. A spec with a
/// <see cref="BuildImageSpec.Platform"/> pulls and builds for that platform, and the platform is
/// part of both image hashes.
/// </summary>
internal sealed class RustBuildImageBuilder
{
    private readonly DockerClient _client;
    private readonly ResourceLabels _labels;

    public RustBuildImageBuilder(DockerClient client, ResourceLabels labels)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _labels = labels ?? throw new ArgumentNullException(nameof(labels));
    }

    /// <summary>
    /// Ensures the image exists and describes it.
    /// </summary>
    /// <param name="spec">A spec whose base image, tag and image name are already
    /// resolved.</param>
    /// <param name="progress">Receives pull and build output lines; may be
    /// <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the pull or build.</param>
    public async Task<RustBuildImage> EnsureAsync(BuildImageSpec spec, IProgress<string> progress, CancellationToken cancellationToken)
    {
        BuildImageSpec.ThrowIfNull(spec);
        var platform = DockerPlatforms.Normalize(spec.Platform);
        var layered = spec;
        string containerfileReference = null;
        var containerfileBuilt = false;
        ImageInspectResult baseImage = null;
        var pulled = false;
        if (spec.UsesContainerfile)
        {
            // The Containerfile's image is the base: build it (once per content hash), then
            // generate the layers FROM it.
            spec.ValidateContainerfile();
            var containerfileHash = ContainerfileImage.ComputeHash(spec);
            var containerfileTag = ContainerfileImage.TagPrefix + containerfileHash;
            containerfileReference = spec.ImageName + ":" + containerfileTag;
            baseImage = spec.RefreshBaseImage ? null : await TryInspectAsync(containerfileReference, cancellationToken).ConfigureAwait(false);
            if (baseImage == null)
            {
                progress?.Report($"Building {containerfileReference} from {Path.GetFullPath(spec.Containerfile)} ...");
                await BuildContainerfileAsync(spec, containerfileReference, containerfileHash, progress, cancellationToken).ConfigureAwait(false);
                containerfileBuilt = true;
                baseImage = await _client.Images.InspectAsync(containerfileReference, cancellationToken).ConfigureAwait(false);
            }

            layered = spec.Clone();
            layered.BaseImage = spec.ImageName;
            layered.Tag = containerfileTag;
        }

        var dockerfile = DockerfileGenerator.Generate(layered);
        var hash = DockerfileGenerator.ComputeSpecHash(dockerfile, platform);
        var baseReference = layered.BaseImageReference;

        if (containerfileReference == null)
        {
            baseImage = await TryInspectAsync(baseReference, cancellationToken).ConfigureAwait(false);
            if (baseImage == null || spec.RefreshBaseImage)
            {
                progress?.Report($"Pulling {baseReference}{(platform == null ? string.Empty : " for " + platform)} ...");
                await PullAsync(baseReference, platform, progress, cancellationToken).ConfigureAwait(false);
                pulled = true;
                baseImage = await _client.Images.InspectAsync(baseReference, cancellationToken).ConfigureAwait(false);
            }
            else if (platform != null && !spec.RequiresDerivedImage && !DockerPlatforms.AreSame("linux/" + (baseImage.Architecture ?? "unknown"), platform))
            {
                // The base image is used directly and its local tag is there, but the inspect
                // shows another platform's variant: a multi-platform tag on the containerd image
                // store reports the engine's own variant, whether or not the requested one is
                // present, and container creation never pulls. Pull that variant (a quick
                // manifest check when it is already local); offline, carry on with what is there
                // and let the container creation say what is missing. (A derived image's build
                // resolves its FROM for the platform itself.)
                progress?.Report($"Pulling {baseReference} for {platform} ...");
                try
                {
                    await PullAsync(baseReference, platform, progress, cancellationToken).ConfigureAwait(false);
                    pulled = true;
                    baseImage = await _client.Images.InspectAsync(baseReference, cancellationToken).ConfigureAwait(false);
                }
                catch (DockerException ex) when (ex is not DockerImageNotFoundException)
                {
                    progress?.Report($"Could not pull {baseReference} for {platform} ({ex.Message}); using the local image.");
                }
            }
        }

        if (!spec.RequiresDerivedImage)
        {
            var separator = baseReference.LastIndexOf(':');
            return new RustBuildImage
            {
                ImageName = baseReference.Substring(0, separator),
                Tag = baseReference.Substring(separator + 1),
                ImageId = baseImage.Id,
                Spec = spec,
                Platform = platform,
                Dockerfile = dockerfile,
                SpecHash = hash,
                BaseImageReference = baseReference,
                IsBaseImage = true,
                WasBasePulled = pulled,
                WasBuilt = containerfileBuilt,
                ContainerfileImageReference = containerfileReference,
                WasContainerfileImageBuilt = containerfileBuilt,
            };
        }

        var repository = spec.ImageName;
        var reference = repository + ":" + hash;
        var existing = await TryInspectAsync(reference, cancellationToken).ConfigureAwait(false);
        var built = false;
        if (existing == null)
        {
            progress?.Report($"Building {reference} from {baseReference}{(platform == null ? string.Empty : " for " + platform)} ...");
            await BuildAsync(reference, dockerfile, hash, platform, progress, cancellationToken).ConfigureAwait(false);
            built = true;
            existing = await _client.Images.InspectAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        return new RustBuildImage
        {
            ImageName = repository,
            Tag = hash,
            ImageId = existing.Id,
            Spec = spec,
            Platform = platform,
            Dockerfile = dockerfile,
            SpecHash = hash,
            BaseImageReference = baseReference,
            IsBaseImage = false,
            WasBasePulled = pulled,
            WasBuilt = built || containerfileBuilt,
            ContainerfileImageReference = containerfileReference,
            WasContainerfileImageBuilt = containerfileBuilt,
        };
    }

    /// <summary>
    /// Builds the image of a spec's Containerfile through the engine's CLI, labelled like every
    /// image the workbench builds (the spec-hash label holds the Containerfile hash).
    /// </summary>
    private async Task BuildContainerfileAsync(BuildImageSpec spec, string reference, string hash, IProgress<string> progress, CancellationToken cancellationToken)
        => await _client.Images.BuildAsync(ContainerfileBuildSpec(spec, reference, hash, _labels, progress), cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// The CodeBrix.Docker build of a spec's Containerfile: its context, file, build arguments,
    /// the workbench labels (the spec-hash label holds the Containerfile hash), <c>--pull</c> for
    /// <see cref="BuildImageSpec.RefreshBaseImage"/> and <c>--platform</c> for
    /// <see cref="BuildImageSpec.Platform"/>.
    /// </summary>
    internal static ImageBuildSpec ContainerfileBuildSpec(BuildImageSpec spec, string reference, string hash, ResourceLabels labels, IProgress<string> progress)
    {
        var buildSpec = new ImageBuildSpec
        {
            ContextDirectory = spec.ResolveContainerfileContext(),
            DockerfilePath = Path.GetFullPath(spec.Containerfile),
            Pull = spec.RefreshBaseImage,
            Output = progress,
            Platform = DockerPlatforms.Normalize(spec.Platform),
        };
        AddTagAndLabels(buildSpec, reference, hash, labels);
        if (spec.ContainerfileBuildArgs != null)
        {
            foreach (var pair in spec.ContainerfileBuildArgs)
            {
                buildSpec.BuildArgs[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        return buildSpec;
    }

    /// <summary>
    /// The CodeBrix.Docker build of a generated Dockerfile written into
    /// <paramref name="context"/>: the workbench labels and <c>--platform</c> for
    /// <paramref name="platform"/> (<c>null</c> = the engine's own).
    /// </summary>
    internal static ImageBuildSpec DockerfileBuildSpec(string context, string reference, string hash, string platform, ResourceLabels labels, IProgress<string> progress)
    {
        var buildSpec = new ImageBuildSpec
        {
            ContextDirectory = context,
            Output = progress,
            Platform = DockerPlatforms.Normalize(platform),
        };
        AddTagAndLabels(buildSpec, reference, hash, labels);
        return buildSpec;
    }

    /// <summary>The pull options of a base image pull for <paramref name="platform"/>, or
    /// <c>null</c> for the engine's own platform (the plain pull, exactly as before).</summary>
    internal static ImagePullOptions PullOptionsFor(string platform)
    {
        var normalized = DockerPlatforms.Normalize(platform);
        return normalized == null ? null : new ImagePullOptions { Platform = normalized };
    }

    private static void AddTagAndLabels(ImageBuildSpec buildSpec, string reference, string hash, ResourceLabels labels)
    {
        buildSpec.Tags.Add(reference);
        foreach (var pair in labels.For(ResourceLabels.KindImage))
        {
            buildSpec.Labels[pair.Key] = pair.Value;
        }

        buildSpec.Labels[labels.SpecHashKey] = hash;
    }

    private async Task BuildAsync(string reference, string dockerfile, string hash, string platform, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var context = Path.Combine(Path.GetTempPath(), "CodeBrix.RustTools.Docker", "image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(context);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), dockerfile, cancellationToken).ConfigureAwait(false);
            await _client.Images.BuildAsync(DockerfileBuildSpec(context, reference, hash, platform, _labels, progress), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                Directory.Delete(context, recursive: true);
            }
            catch (IOException)
            {
                // A temporary folder that cannot be deleted is not worth failing the build for.
            }
            catch (UnauthorizedAccessException)
            {
                // Same.
            }
        }
    }

    /// <summary>Pulls an image, for <paramref name="platform"/> when it is set (the
    /// <see cref="ImagePullOptions"/> overload) and exactly as before otherwise.</summary>
    private Task PullAsync(string reference, string platform, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var options = PullOptionsFor(platform);
        return options == null
            ? _client.Images.PullAsync(reference, progress, cancellationToken)
            : _client.Images.PullAsync(reference, options, progress, cancellationToken);
    }

    private async Task<ImageInspectResult> TryInspectAsync(string reference, CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Images.InspectAsync(reference, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerImageNotFoundException)
        {
            return null;
        }
    }
}
