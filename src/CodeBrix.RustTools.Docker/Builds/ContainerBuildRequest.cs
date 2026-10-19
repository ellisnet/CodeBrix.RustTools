using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Archives;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// Everything <see cref="DockerRustWorkbench.BuildAsync"/> needs for a one-call container
/// build: where the source is, how to build it, for which targets, in which image and
/// container, and what to bring back.
/// </summary>
public sealed class ContainerBuildRequest
{
    /// <summary>
    /// A project folder on the host (a package, a workspace root or a workspace member; for a
    /// member the whole workspace is copied and the member is built). Set exactly one of
    /// <see cref="ProjectDirectory"/>, <see cref="ArchivePath"/> and
    /// <see cref="ExtractedSource"/>.
    /// </summary>
    public string ProjectDirectory { get; set; }

    /// <summary>
    /// A source archive on the host (zip, tar, tar.gz or tgz). It is extracted on the host
    /// with the sibling package's <see cref="SourceArchiveExtractor"/> (using
    /// <see cref="ArchiveOptions"/>), copied in, and the extracted folder is deleted after the
    /// build.
    /// </summary>
    public string ArchivePath { get; set; }

    /// <summary>
    /// Source already extracted on the host by <see cref="SourceArchiveExtractor"/>. Its
    /// <see cref="ExtractedSource.ProjectDirectory"/> is built; it is NOT disposed by the
    /// build.
    /// </summary>
    public ExtractedSource ExtractedSource { get; set; }

    /// <summary>
    /// How <see cref="ArchivePath"/> is extracted; <c>null</c> uses the defaults.
    /// </summary>
    public SourceArchiveOptions ArchiveOptions { get; set; }

    /// <summary>
    /// The cargo options (the sibling package's type). <see cref="CargoBuildOptions.LicensePolicy"/>,
    /// <see cref="CargoBuildOptions.LicenseGateMode"/> and
    /// <see cref="CargoBuildOptions.ThrowOnLicenseViolation"/> are honoured (the audit runs
    /// inside the container); <see cref="CargoBuildOptions.ExpectedCrateCount"/>, when not
    /// set, is estimated per target (when progress is reported or a policy is set); <see cref="CargoBuildOptions.TargetTriple"/> is replaced per
    /// target; <see cref="CargoBuildOptions.WorkingDirectory"/> is ignored (the build runs in
    /// the copied project). <c>null</c> uses the defaults.
    /// </summary>
    public CargoBuildOptions Options { get; set; }

    /// <summary>
    /// The targets to build, in order. Default: <see cref="CrossTarget.LinuxNative"/> only (the
    /// engine's own Linux architecture; the result is keyed by the resolved target, for example
    /// <see cref="CrossTarget.LinuxArm64"/> on Docker Desktop for Apple Silicon).
    /// </summary>
    public IList<CrossTarget> Targets { get; set; } = new List<CrossTarget> { CrossTarget.LinuxNative };

    /// <summary>
    /// The build image; <c>null</c> uses the base image. The layers the
    /// <see cref="Targets"/> need (Windows, Android, macOS, a Linux target of another
    /// architecture) are added automatically. Its <see cref="BuildImageSpec.Platform"/> builds
    /// the image - and, unless <see cref="ContainerOptions"/> names another, runs the container -
    /// on that platform; "another architecture" and <see cref="CrossTarget.LinuxNative"/> are
    /// then relative to the platform, not to the engine.
    /// </summary>
    public BuildImageSpec ImageSpec { get; set; }

    /// <summary>
    /// The build container's settings; <c>null</c> uses the defaults. Its
    /// <see cref="BuildContainerOptions.Platform"/>, when set, wins over the image spec's
    /// platform for the container.
    /// </summary>
    public BuildContainerOptions ContainerOptions { get; set; }

    /// <summary>
    /// What to copy out of the container after a successful target build, and where to; <c>null</c>
    /// (the default) retrieves nothing.
    /// </summary>
    public ArtifactRetrievalOptions Retrieval { get; set; }

    /// <summary>
    /// Whether the container is removed when the build ends (also after a failure).
    /// <c>null</c> (the default) removes it unless it is kept
    /// (<see cref="BuildContainerOptions.KeepOnDispose"/>, else
    /// <see cref="DockerRustWorkbenchOptions.KeepContainerByDefault"/>).
    /// </summary>
    public bool? DestroyContainerAfterBuild { get; set; }

    /// <summary>
    /// Whether the container's target volume (the compiled output and incremental state) is
    /// removed along with the container. Default <c>true</c>. Has no effect when the container
    /// is kept.
    /// </summary>
    public bool RemoveTargetVolumeAfterBuild { get; set; } = true;

    /// <summary>
    /// When <c>false</c> (the default) the first target that fails to build stops the build;
    /// when <c>true</c> every target is attempted.
    /// </summary>
    public bool ContinueOnTargetFailure { get; set; }

    /// <summary>
    /// A limit for the whole build (image, container, copy, audit, cargo, retrieval);
    /// <c>null</c> (the default) for none. Exceeding it throws
    /// <see cref="ContainerBuildException"/> with an inner <see cref="TimeoutException"/>.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>
    /// Exclusion patterns for the source copy, added to
    /// <see cref="DockerRustWorkbenchOptions.DefaultExcludePatterns"/>.
    /// </summary>
    public IList<string> ExtraExcludePatterns { get; set; } = new List<string>();

    /// <summary>
    /// A host folder to copy into <c>/work</c> instead of the project's Cargo workspace root:
    /// for a build whose path dependencies or other inputs live beside the workspace (for
    /// example a staging folder that holds the crate, vendored path crates and a generated
    /// crate). It must contain the workspace root (or the project folder when it is
    /// standalone); the project is built in its own folder below it
    /// (<see cref="ContainerBuildSource.ContainerDirectory"/>). Only with
    /// <see cref="ProjectDirectory"/>. <c>null</c> (the default) copies the workspace root.
    /// </summary>
    public string CopyRootDirectory { get; set; }

    /// <summary>
    /// The Android API level the Android targets are linked for (the container's
    /// <see cref="BuildContainer.AndroidApiLevel"/>). Default 24
    /// (<see cref="AndroidLinkProfile.DefaultApiLevel"/>); 21 or later.
    /// </summary>
    public int AndroidApiLevel { get; set; } = AndroidLinkProfile.DefaultApiLevel;

    /// <summary>
    /// Extra environment variables for ONE target's cargo run, keyed by a target of
    /// <see cref="Targets"/> (<see cref="CrossTarget.MacOSUniversal"/> applies to both slices,
    /// <see cref="CrossTarget.LinuxNative"/> to the engine's own Linux target). They are added to
    /// <see cref="CargoBuildOptions.Environment"/> for that target, replacing an entry of the
    /// same name. As with every cargo run in the container, a
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c> variable EXTENDS the rustflags the package
    /// sets for that target (Windows <c>+crt-static</c>, the macOS link profile's flags) instead
    /// of replacing them. Empty by default.
    /// </summary>
    public IDictionary<CrossTarget, IDictionary<string, string>> TargetEnvironment { get; set; } = new Dictionary<CrossTarget, IDictionary<string, string>>();

    /// <summary>Returns a shallow copy with its own lists (the option objects are
    /// shared).</summary>
    public ContainerBuildRequest Clone() => new ContainerBuildRequest
    {
        ProjectDirectory = ProjectDirectory,
        ArchivePath = ArchivePath,
        ExtractedSource = ExtractedSource,
        ArchiveOptions = ArchiveOptions,
        Options = Options,
        Targets = Targets == null ? new List<CrossTarget>() : Targets.ToList(),
        ImageSpec = ImageSpec,
        ContainerOptions = ContainerOptions,
        Retrieval = Retrieval,
        DestroyContainerAfterBuild = DestroyContainerAfterBuild,
        RemoveTargetVolumeAfterBuild = RemoveTargetVolumeAfterBuild,
        ContinueOnTargetFailure = ContinueOnTargetFailure,
        Timeout = Timeout,
        ExtraExcludePatterns = ExtraExcludePatterns == null ? new List<string>() : ExtraExcludePatterns.ToList(),
        CopyRootDirectory = CopyRootDirectory,
        AndroidApiLevel = AndroidApiLevel,
        TargetEnvironment = TargetEnvironment == null
            ? new Dictionary<CrossTarget, IDictionary<string, string>>()
            : TargetEnvironment.ToDictionary(p => p.Key, p => (IDictionary<string, string>)(p.Value == null ? new Dictionary<string, string>(StringComparer.Ordinal) : new Dictionary<string, string>(p.Value, StringComparer.Ordinal))),
    };

    /// <summary>
    /// Checks the request before anything contacts the engine.
    /// </summary>
    internal void Validate()
    {
        var sources = (string.IsNullOrWhiteSpace(ProjectDirectory) ? 0 : 1)
            + (string.IsNullOrWhiteSpace(ArchivePath) ? 0 : 1)
            + (ExtractedSource == null ? 0 : 1);
        if (sources != 1)
        {
            throw new ArgumentException("Set exactly one of ProjectDirectory, ArchivePath and ExtractedSource.", "request");
        }

        if (ExtractedSource != null && (ExtractedSource.IsDisposed || ExtractedSource.ProjectDirectory == null))
        {
            throw new ArgumentException("The ExtractedSource has been disposed or contains no Cargo project.", "request");
        }

        if (Targets == null || Targets.Count == 0)
        {
            throw new ArgumentException("Targets must name at least one target.", "request");
        }

        foreach (var target in Targets)
        {
            if (!CrossTargetInfo.Get(target).IsSupported)
            {
                throw new CrossTargetNotSupportedException(target);
            }
        }

        if (Targets.Distinct().Count() != Targets.Count)
        {
            throw new ArgumentException("Targets must not name a target twice.", "request");
        }

        if (AndroidApiLevel < 21)
        {
            throw new ArgumentException("AndroidApiLevel must be 21 or later (64-bit Android targets start there).", "request");
        }

        Engine.DockerPlatforms.Normalize(ImageSpec?.Platform);
        Engine.DockerPlatforms.Normalize(ContainerOptions?.Platform);

        if (!string.IsNullOrWhiteSpace(CopyRootDirectory) && string.IsNullOrWhiteSpace(ProjectDirectory))
        {
            throw new ArgumentException("CopyRootDirectory can only be used with ProjectDirectory.", "request");
        }

        foreach (var key in (TargetEnvironment ?? new Dictionary<CrossTarget, IDictionary<string, string>>()).Keys)
        {
            if (!Targets.Contains(key))
            {
                throw new ArgumentException($"TargetEnvironment names {key}, which is not one of the requested Targets.", "request");
            }
        }

        if (Retrieval != null)
        {
            ArtifactRetriever.Validate(Retrieval);
            if (Retrieval.Layout == ArtifactLayout.Flat && Targets.Count > 1)
            {
                throw new ArgumentException("The Flat artifact layout holds one target's files; use ByTarget or NuGetRuntimes for several targets.", "request");
            }
        }
    }
}
