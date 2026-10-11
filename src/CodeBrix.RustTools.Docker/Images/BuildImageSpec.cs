using System;
using System.Collections.Generic;
using System.IO;
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
    /// A Containerfile (Dockerfile) on this machine to build the base image from, instead of
    /// pulling <see cref="BaseImage"/>: "bring your own image". Use it for a base the generated
    /// layers cannot make - a non-Debian distribution such as manylinux / AlmaLinux (whose
    /// packages come from <c>dnf</c>, not <c>apt</c>), pinned system toolchains, cross sysroots.
    /// <c>null</c> (the default) pulls <see cref="BaseImage"/>.
    /// <para>
    /// The image is built through the engine's CLI (CodeBrix.Docker's image build: <c>docker
    /// build</c>, or <c>podman build</c> when <see cref="CodeBrix.Docker.DockerClientOptions.DockerCliPath"/>
    /// is <c>podman</c>), tagged <c>&lt;ImageName&gt;:containerfile-&lt;hash&gt;</c> and labelled
    /// like every image the workbench builds. The hash covers the Containerfile's bytes,
    /// <see cref="ContainerfileBuildArgs"/> and every file in <see cref="ContainerfileContext"/>,
    /// so a second <see cref="DockerRustWorkbench.EnsureImageAsync"/> reuses the image until one of
    /// them changes (files a <c>.dockerignore</c> excludes still count: changing one causes a
    /// rebuild that was not strictly needed, never a stale image). With
    /// <see cref="RefreshBaseImage"/> it is rebuilt every time, pulling its <c>FROM</c> image
    /// afresh.
    /// </para>
    /// <para>
    /// Two ways to use it: (1) as the COMPLETE build image, when the Containerfile installs Rust
    /// itself (rustup with <c>CARGO_HOME=/usr/local/cargo</c>, as in the official <c>rust</c>
    /// image, so the cargo cache volume mounted at <c>/usr/local/cargo/registry</c> is cargo's
    /// registry) and the spec adds no layers; (2) as the base of the generated layers, when it does
    /// (<see cref="RustVersion"/> and <see cref="InstallClippyRustfmt"/> then need rustup in the
    /// image, <see cref="AptPackages"/> and the cross-target layers need a Debian-based image).
    /// <see cref="BaseImage"/> and <see cref="Tag"/> must be left unset.
    /// </para>
    /// </summary>
    public string Containerfile { get; set; }

    /// <summary>
    /// The build context folder of <see cref="Containerfile"/> (what its <c>COPY</c> and
    /// <c>ADD</c> instructions read). <c>null</c> (the default) means the Containerfile's own
    /// folder.
    /// </summary>
    public string ContainerfileContext { get; set; }

    /// <summary>
    /// Build arguments for <see cref="Containerfile"/> (its <c>ARG</c> values, passed as
    /// <c>--build-arg name=value</c>). Part of the image hash.
    /// </summary>
    public IDictionary<string, string> ContainerfileBuildArgs { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Whether <see cref="Containerfile"/> is set.
    /// </summary>
    public bool UsesContainerfile => !string.IsNullOrWhiteSpace(Containerfile);

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
        Containerfile = Containerfile,
        ContainerfileContext = ContainerfileContext,
        ContainerfileBuildArgs = ContainerfileBuildArgs == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(ContainerfileBuildArgs, StringComparer.Ordinal),
    };

    /// <summary>
    /// A copy of this spec with the unset base image, tag and image name filled in from the
    /// workbench options.
    /// </summary>
    internal BuildImageSpec WithDefaults(DockerRustWorkbenchOptions options)
    {
        var copy = Clone();
        if (string.IsNullOrWhiteSpace(copy.BaseImage) && !copy.UsesContainerfile)
        {
            copy.BaseImage = options.DefaultImage;
        }

        if (string.IsNullOrWhiteSpace(copy.Tag) && !copy.UsesContainerfile)
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

    /// <summary>
    /// Checks the Containerfile settings (nothing to check when <see cref="Containerfile"/> is
    /// unset).
    /// </summary>
    /// <exception cref="ArgumentException"><see cref="BaseImage"/> or <see cref="Tag"/> is set
    /// together with a Containerfile, or a build argument has an invalid name.</exception>
    /// <exception cref="FileNotFoundException">The Containerfile does not exist.</exception>
    /// <exception cref="DirectoryNotFoundException">The context folder does not exist.</exception>
    internal void ValidateContainerfile()
    {
        if (!UsesContainerfile)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(BaseImage) || !string.IsNullOrWhiteSpace(Tag))
        {
            throw new ArgumentException("BaseImage and Tag must be left unset when Containerfile is set: the Containerfile's FROM decides the base.", nameof(Containerfile));
        }

        var path = Path.GetFullPath(Containerfile);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The Containerfile '{path}' does not exist.", path);
        }

        var context = ResolveContainerfileContext();
        if (!Directory.Exists(context))
        {
            throw new DirectoryNotFoundException($"The Containerfile build context '{context}' does not exist.");
        }

        if (ContainerfileBuildArgs != null)
        {
            foreach (var name in ContainerfileBuildArgs.Keys)
            {
                if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Any(char.IsWhiteSpace))
                {
                    throw new ArgumentException($"'{name}' is not a valid build argument name.", nameof(ContainerfileBuildArgs));
                }
            }
        }
    }

    /// <summary>The full path of the Containerfile's build context folder.</summary>
    internal string ResolveContainerfileContext()
        => Path.GetFullPath(string.IsNullOrWhiteSpace(ContainerfileContext)
            ? Path.GetDirectoryName(Path.GetFullPath(Containerfile))
            : ContainerfileContext);

    internal static void ThrowIfNull(BuildImageSpec spec)
    {
        if (spec == null)
        {
            throw new ArgumentNullException(nameof(spec));
        }
    }
}
