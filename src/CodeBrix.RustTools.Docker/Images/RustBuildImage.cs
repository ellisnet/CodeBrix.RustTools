namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// An image that build containers can run, as prepared by
/// <see cref="DockerRustWorkbench.EnsureImageAsync"/>: either the base image itself (when the
/// spec adds no layers) or a derived image built from the generated Dockerfile.
/// </summary>
public sealed class RustBuildImage
{
    /// <summary>The image repository, for example <c>rust</c> or
    /// <c>codebrix-rusttools-docker</c>.</summary>
    public string ImageName { get; init; } = string.Empty;

    /// <summary>The image tag: the base tag (for example <c>latest</c>) for the base image, the
    /// 16-digit Dockerfile hash for a derived image.</summary>
    public string Tag { get; init; } = string.Empty;

    /// <summary>The image reference, <c>&lt;ImageName&gt;:&lt;Tag&gt;</c>.</summary>
    public string Reference => ImageName + ":" + Tag;

    /// <summary>The engine's id for the image (<c>sha256:...</c>).</summary>
    public string ImageId { get; init; } = string.Empty;

    /// <summary>The spec the image was prepared from, with the defaults filled in.</summary>
    public BuildImageSpec Spec { get; init; }

    /// <summary>The Dockerfile text generated from <see cref="Spec"/> (also produced for a base
    /// image, where it documents what the image would contain).</summary>
    public string Dockerfile { get; init; } = string.Empty;

    /// <summary>The hash of <see cref="Dockerfile"/> (see
    /// <see cref="DockerfileGenerator.ComputeSpecHash"/>).</summary>
    public string SpecHash { get; init; } = string.Empty;

    /// <summary>The base image reference, for example <c>rust:latest</c>.</summary>
    public string BaseImageReference { get; init; } = string.Empty;

    /// <summary>Whether this is the base image used directly (the spec added no layers) - the
    /// pulled base image, or the image built from <see cref="BuildImageSpec.Containerfile"/>.</summary>
    public bool IsBaseImage { get; init; }

    /// <summary>Whether the base image was pulled during this call.</summary>
    public bool WasBasePulled { get; init; }

    /// <summary>Whether the derived image, or the Containerfile image, was built during this
    /// call.</summary>
    public bool WasBuilt { get; init; }

    /// <summary>The image built from <see cref="BuildImageSpec.Containerfile"/>
    /// (<c>&lt;ImageName&gt;:containerfile-&lt;hash&gt;</c>), or <c>null</c> when the spec names
    /// no Containerfile. Without layers it is this image itself (<see cref="IsBaseImage"/>);
    /// with layers it is the base they were built on.</summary>
    public string ContainerfileImageReference { get; init; }

    /// <summary>Whether the Containerfile image was built during this call (a second call with
    /// the same Containerfile, build arguments and context reuses it).</summary>
    public bool WasContainerfileImageBuilt { get; init; }

    /// <summary>
    /// Whether the image was already on the engine: nothing had to be built, and for a base
    /// image nothing had to be pulled.
    /// </summary>
    public bool WasReused => !WasBuilt && !(IsBaseImage && WasBasePulled);

    /// <summary>Returns <see cref="Reference"/>.</summary>
    public override string ToString() => Reference;
}
