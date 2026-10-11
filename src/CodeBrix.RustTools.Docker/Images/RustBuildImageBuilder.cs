using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Internal;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// Makes sure the image a spec describes exists on the engine: pulls the base image when it is
/// missing, uses it directly when the spec adds no layers, and otherwise builds (once) the
/// derived image tagged with the hash of its generated Dockerfile.
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
        var hash = DockerfileGenerator.ComputeSpecHash(dockerfile);
        var baseReference = layered.BaseImageReference;

        if (containerfileReference == null)
        {
            baseImage = await TryInspectAsync(baseReference, cancellationToken).ConfigureAwait(false);
            if (baseImage == null || spec.RefreshBaseImage)
            {
                progress?.Report($"Pulling {baseReference} ...");
                await _client.Images.PullAsync(baseReference, progress, cancellationToken).ConfigureAwait(false);
                pulled = true;
                baseImage = await _client.Images.InspectAsync(baseReference, cancellationToken).ConfigureAwait(false);
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
            progress?.Report($"Building {reference} from {baseReference} ...");
            await BuildAsync(reference, dockerfile, hash, progress, cancellationToken).ConfigureAwait(false);
            built = true;
            existing = await _client.Images.InspectAsync(reference, cancellationToken).ConfigureAwait(false);
        }

        return new RustBuildImage
        {
            ImageName = repository,
            Tag = hash,
            ImageId = existing.Id,
            Spec = spec,
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
    {
        var labels = _labels.For(ResourceLabels.KindImage);
        labels[_labels.SpecHashKey] = hash;
        var buildSpec = new ImageBuildSpec
        {
            ContextDirectory = spec.ResolveContainerfileContext(),
            DockerfilePath = Path.GetFullPath(spec.Containerfile),
            Pull = spec.RefreshBaseImage,
            Output = progress,
        };
        buildSpec.Tags.Add(reference);
        foreach (var pair in labels)
        {
            buildSpec.Labels[pair.Key] = pair.Value;
        }

        if (spec.ContainerfileBuildArgs != null)
        {
            foreach (var pair in spec.ContainerfileBuildArgs)
            {
                buildSpec.BuildArgs[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        await _client.Images.BuildAsync(buildSpec, cancellationToken).ConfigureAwait(false);
    }

    private async Task BuildAsync(string reference, string dockerfile, string hash, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var context = Path.Combine(Path.GetTempPath(), "CodeBrix.RustTools.Docker", "image-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(context);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(context, "Dockerfile"), dockerfile, cancellationToken).ConfigureAwait(false);
            var labels = _labels.For(ResourceLabels.KindImage);
            labels[_labels.SpecHashKey] = hash;
            var buildSpec = new ImageBuildSpec
            {
                ContextDirectory = context,
                Output = progress,
            };
            buildSpec.Tags.Add(reference);
            foreach (var pair in labels)
            {
                buildSpec.Labels[pair.Key] = pair.Value;
            }

            await _client.Images.BuildAsync(buildSpec, cancellationToken).ConfigureAwait(false);
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
