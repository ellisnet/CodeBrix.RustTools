using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Docker.Engine;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// Describes the image a build container runs: the base image and the optional layers the
/// <see cref="DockerfileGenerator"/> adds on top of it. A spec with no layers uses the base image
/// as it is; any layer produces a derived image whose tag is a hash of the generated Dockerfile,
/// so identical specs share one image.
/// </summary>
public sealed class BuildImageSpec
{
    /// <summary>
    /// The base image repository, for example <c>rust</c>. <c>null</c> (the default) means the
    /// workbench's <see cref="DockerRustWorkbenchOptions.DefaultImage"/>.
    /// </summary>
    public string BaseImage { get; set; }

    /// <summary>
    /// The base image tag, for example <c>latest</c>, <c>1-bookworm</c> or <c>1.94</c>.
    /// <c>null</c> (the default) means the workbench's
    /// <see cref="DockerRustWorkbenchOptions.DefaultTag"/>.
    /// </summary>
    public string Tag { get; set; }

    /// <summary>
    /// A Rust toolchain to make the default inside the image (for example <c>1.94.1</c>,
    /// <c>stable</c> or <c>nightly-2026-09-01</c>); adds a <c>rustup default</c> layer, which
    /// installs that toolchain. <c>null</c> keeps the base image's toolchain.
    /// </summary>
    public string RustVersion { get; set; }

    /// <summary>
    /// Debian packages to install in the image (for example <c>pkg-config</c>,
    /// <c>libssl-dev</c>, <c>libasound2-dev</c>), for crates whose build scripts need system
    /// libraries. Order and duplicates do not matter.
    /// </summary>
    public IList<string> AptPackages { get; set; } = new List<string>();

    /// <summary>
    /// Whether to add the clippy and rustfmt components to the image's toolchain.
    /// </summary>
    public bool InstallClippyRustfmt { get; set; }

    /// <summary>
    /// The cross-compilation targets the image must be able to build, in addition to the
    /// container's own Linux target. Targets that need a layer (Windows, Android, macOS, a
    /// Linux target of another architecture) add it; <see cref="CrossTarget.LinuxNative"/> and
    /// the Linux target of the engine's own architecture add nothing (the workbench removes
    /// them before the image is built). The macOS layer produces development-and-testing-only
    /// binaries (see <see cref="CrossTargetInfo.MacOSDevelopmentOnlyNotice"/>).
    /// </summary>
    public IList<CrossTarget> CrossTargets { get; set; } = new List<CrossTarget>();

    /// <summary>
    /// Extra Dockerfile instructions appended after every generated layer, one instruction per
    /// entry (for example <c>RUN cargo install cargo-about --locked</c>). Each entry must be a
    /// single line.
    /// </summary>
    public IList<string> ExtraDockerfileLines { get; set; } = new List<string>();

    /// <summary>
    /// The repository name given to the derived image. <c>null</c> (the default) means the
    /// workbench's <see cref="DockerRustWorkbenchOptions.BuildImageRepository"/>, which follows
    /// the workbench's <see cref="DockerRustWorkbenchOptions.LabelPrefix"/> unless it is set
    /// explicitly.
    /// </summary>
    public string ImageName { get; set; }

    /// <summary>
    /// Whether to pull the base image even when it is already present, to pick up a newer
    /// build of a moving tag such as <c>latest</c>. Default <c>false</c>: the base image is
    /// pulled only when it is missing. A derived image is reused for as long as its generated
    /// Dockerfile is unchanged, whatever this setting.
    /// </summary>
    public bool RefreshBaseImage { get; set; }

    /// <summary>
    /// The base image reference, <c>&lt;BaseImage&gt;:&lt;Tag&gt;</c>, using the library defaults
    /// (<c>rust:latest</c>) for unset parts.
    /// </summary>
    public string BaseImageReference
        => (string.IsNullOrWhiteSpace(BaseImage) ? DockerRustWorkbenchOptions.DefaultBaseImage : BaseImage.Trim())
           + ":" + (string.IsNullOrWhiteSpace(Tag) ? DockerRustWorkbenchOptions.DefaultBaseTag : Tag.Trim());

    /// <summary>
    /// Whether the spec adds anything to the base image. When <c>false</c> the base image is
    /// used directly and no image is built.
    /// </summary>
    public bool RequiresDerivedImage
        => !string.IsNullOrWhiteSpace(RustVersion)
           || (AptPackages != null && AptPackages.Any(p => !string.IsNullOrWhiteSpace(p)))
           || InstallClippyRustfmt
           || (CrossTargets != null && CrossTargets.Any(t => CrossTargetInfo.Get(t).RequiresLayer))
           || (ExtraDockerfileLines != null && ExtraDockerfileLines.Any(l => !string.IsNullOrWhiteSpace(l)));

    /// <summary>
    /// Returns a deep copy of this spec.
    /// </summary>
    public BuildImageSpec Clone() => new BuildImageSpec
    {
        BaseImage = BaseImage,
        Tag = Tag,
        RustVersion = RustVersion,
        AptPackages = AptPackages == null ? new List<string>() : AptPackages.ToList(),
        InstallClippyRustfmt = InstallClippyRustfmt,
        CrossTargets = CrossTargets == null ? new List<CrossTarget>() : CrossTargets.ToList(),
        ExtraDockerfileLines = ExtraDockerfileLines == null ? new List<string>() : ExtraDockerfileLines.ToList(),
        ImageName = ImageName,
        RefreshBaseImage = RefreshBaseImage,
    };

    /// <summary>
    /// A copy of this spec with the unset base image, tag and image name filled in from the
    /// workbench options.
    /// </summary>
    internal BuildImageSpec WithDefaults(DockerRustWorkbenchOptions options)
    {
        var copy = Clone();
        if (string.IsNullOrWhiteSpace(copy.BaseImage))
        {
            copy.BaseImage = options.DefaultImage;
        }

        if (string.IsNullOrWhiteSpace(copy.Tag))
        {
            copy.Tag = options.DefaultTag;
        }

        if (string.IsNullOrWhiteSpace(copy.ImageName))
        {
            copy.ImageName = options.BuildImageRepository;
        }

        return copy;
    }

    /// <summary>
    /// A copy of this spec without <see cref="CrossTarget.LinuxNative"/> and without the Linux
    /// target that is the engine's own architecture: those build natively and need no layer.
    /// </summary>
    internal BuildImageSpec WithoutNativeLinuxTargets(DockerEngineInfo engine)
    {
        var copy = Clone();
        copy.CrossTargets = (CrossTargets ?? new List<CrossTarget>())
            .Where(t => !CrossTargetInfo.Get(t).IsLinuxNative && !CrossTargetInfo.Get(t).IsNativeOn(engine))
            .ToList();
        return copy;
    }

    internal static void ThrowIfNull(BuildImageSpec spec)
    {
        if (spec == null)
        {
            throw new ArgumentNullException(nameof(spec));
        }
    }
}
