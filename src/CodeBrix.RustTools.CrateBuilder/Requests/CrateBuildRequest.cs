using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Licensing;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Everything one <see cref="CrateBuilder.BuildAsync"/> call needs: the crate, the build mode,
/// the targets, where the files go, and optionally the wrapper to write.
/// </summary>
public sealed class CrateBuildRequest
{
    /// <summary>The crate to build (REQUIRED): a <see cref="LocalManifestSource"/> or a
    /// <see cref="GitRepositorySource"/>.</summary>
    public CrateSource Source { get; set; }

    /// <summary>Where cargo runs. Default <see cref="BuildMode.Host"/>.</summary>
    public BuildMode Mode { get; set; } = BuildMode.Host;

    /// <summary>The targets to build (REQUIRED: at least one, none twice). Targets the
    /// readiness check finds unbuildable in <see cref="Mode"/> are reported as
    /// <see cref="CrateTargetOutcome.NotBuildableInThisMode"/> while the others are
    /// built.</summary>
    public IList<CrateTarget> Targets { get; set; } = new List<CrateTarget>();

    /// <summary>The base image for the Linux targets in Docker mode. Default
    /// <see cref="LinuxBaseline.RustImage"/>; <see cref="LinuxBaseline.ManyLinux"/> is
    /// rejected in host mode.</summary>
    public LinuxBaseline LinuxBaseline { get; set; } = LinuxBaseline.RustImage;

    /// <summary>Link the crate against the CodeBrix WebGPU engine (the
    /// <c>codebrix_wgpu</c> library of CodeBrix.WebGpu.Native). Default false.</summary>
    public bool LinkToCodeBrixWebGpu { get; set; }

    /// <summary>Where the native files and the build record go (REQUIRED).</summary>
    public string OutputDirectory { get; set; }

    /// <summary>How the native files are laid out under <see cref="OutputDirectory"/>.
    /// Default <see cref="ArtifactLayout.NuGetRuntimes"/>
    /// (<c>runtimes/&lt;rid&gt;/native/</c>); <see cref="ArtifactLayout.Flat"/> is allowed for
    /// one target only; <see cref="ArtifactLayout.ByTarget"/> is rejected.</summary>
    public ArtifactLayout Layout { get; set; } = ArtifactLayout.NuGetRuntimes;

    /// <summary>The cargo profile. Default <see cref="CrateBuildProfile.Release"/>.</summary>
    public CrateBuildProfile Profile { get; set; } = CrateBuildProfile.Release;

    /// <summary>Cargo features to enable (<c>--features</c>).</summary>
    public IList<string> Features { get; set; } = new List<string>();

    /// <summary>Pass <c>--no-default-features</c>.</summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>Pass <c>--all-features</c>.</summary>
    public bool AllFeatures { get; set; }

    /// <summary>Pass <c>--offline</c>: cargo may not use the network (every dependency must
    /// already be in the cargo cache).</summary>
    public bool Offline { get; set; }

    /// <summary>Pass <c>--locked</c>: the crate's Cargo.lock must be up to date and is used
    /// as it is.</summary>
    public bool Locked { get; set; }

    /// <summary>The licence policy every crate in the dependency graph must satisfy before
    /// anything compiles; <c>null</c> = no licence gate.</summary>
    public LicensePolicy LicensePolicy { get; set; }

    /// <summary>A limit for the whole build after the readiness check; <c>null</c> = none.
    /// When it expires the build stops and the result names the stage that was running.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>The wrapper project to write; <c>null</c> = native files and the build
    /// record only.</summary>
    public WrapperOptions Wrapper { get; set; }

    /// <summary>Whether the facade stages run (<see cref="CodeBrix.RustTools.CrateBuilder.FacadeMode"/>): Auto (the
    /// default) decides from the crate and the wrapper request and falls back to a facade when
    /// a cdylib built as it is exports nothing; Never builds the crate as it is; Always
    /// generates a facade even over a cdylib with C exports.</summary>
    public FacadeMode FacadeMode { get; set; } = FacadeMode.Auto;

    /// <summary>
    /// Checks the request before anything runs.
    /// </summary>
    /// <exception cref="ArgumentException">A rule is broken: no source (or an invalid one),
    /// no targets, a target named twice, no output folder, the
    /// <see cref="ArtifactLayout.ByTarget"/> layout, the <see cref="ArtifactLayout.Flat"/>
    /// layout with more than one target, <see cref="LinuxBaseline.ManyLinux"/> in host mode,
    /// a blank feature name, a timeout that is not positive, an undefined facade mode,
    /// <see cref="FacadeMode.Never"/> with a wrapper, or invalid wrapper settings.</exception>
    public void Validate()
    {
        if (Source == null)
        {
            throw new ArgumentException("CrateBuildRequest.Source is required: a LocalManifestSource or a GitRepositorySource.");
        }

        Source.Validate();

        if (!Enum.IsDefined(Mode))
        {
            throw new ArgumentException($"CrateBuildRequest.Mode {Mode} is not a defined build mode.");
        }

        if (Targets == null || Targets.Count == 0)
        {
            throw new ArgumentException("CrateBuildRequest.Targets must name at least one target.");
        }

        foreach (var target in Targets)
        {
            if (!Enum.IsDefined(target))
            {
                throw new ArgumentException($"CrateBuildRequest.Targets holds {target}, which is not a defined target.");
            }
        }

        var repeated = Targets.GroupBy(t => t).FirstOrDefault(g => g.Count() > 1);
        if (repeated != null)
        {
            throw new ArgumentException($"CrateBuildRequest.Targets names {repeated.Key} twice; name each target once.");
        }

        if (string.IsNullOrWhiteSpace(OutputDirectory))
        {
            throw new ArgumentException("CrateBuildRequest.OutputDirectory is required: the folder that receives the native files and the build record.");
        }

        if (Layout == ArtifactLayout.ByTarget)
        {
            throw new ArgumentException("CrateBuildRequest.Layout ByTarget is not supported; use NuGetRuntimes (runtimes/<rid>/native/) or, for one target, Flat.");
        }

        if (!Enum.IsDefined(Layout))
        {
            throw new ArgumentException($"CrateBuildRequest.Layout {Layout} is not a defined layout.");
        }

        if (Layout == ArtifactLayout.Flat && Targets.Count > 1)
        {
            throw new ArgumentException("CrateBuildRequest.Layout Flat allows one target only (the targets' files would share names); use NuGetRuntimes.");
        }

        if (Mode == BuildMode.Host && LinuxBaseline == LinuxBaseline.ManyLinux)
        {
            throw new ArgumentException("CrateBuildRequest.LinuxBaseline ManyLinux applies to Docker mode only; host builds use the host's own toolchain.");
        }

        if (Features != null && Features.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("CrateBuildRequest.Features must not hold blank names.");
        }

        if (Timeout.HasValue && Timeout.Value <= TimeSpan.Zero)
        {
            throw new ArgumentException("CrateBuildRequest.Timeout must be positive when it is set.");
        }

        if (!Enum.IsDefined(FacadeMode))
        {
            throw new ArgumentException($"CrateBuildRequest.FacadeMode {FacadeMode} is not a defined facade mode.");
        }

        if (FacadeMode == FacadeMode.Never && Wrapper != null)
        {
            throw new ArgumentException("CrateBuildRequest.FacadeMode Never cannot be combined with a Wrapper: the wrapper's declarations come from the facade stages (use Auto, which passes a cdylib's own C exports through).");
        }

        Wrapper?.Validate();
    }

    /// <summary>
    /// A deep copy of the request: the source, lists, licence policy and wrapper settings are
    /// copied (a Git credentials callback is shared).
    /// </summary>
    public CrateBuildRequest Clone() => new CrateBuildRequest
    {
        Source = Source?.Clone(),
        Mode = Mode,
        Targets = new List<CrateTarget>(Targets ?? Enumerable.Empty<CrateTarget>()),
        LinuxBaseline = LinuxBaseline,
        LinkToCodeBrixWebGpu = LinkToCodeBrixWebGpu,
        OutputDirectory = OutputDirectory,
        Layout = Layout,
        Profile = Profile,
        Features = new List<string>(Features ?? Enumerable.Empty<string>()),
        NoDefaultFeatures = NoDefaultFeatures,
        AllFeatures = AllFeatures,
        Offline = Offline,
        Locked = Locked,
        LicensePolicy = LicensePolicy?.Clone(),
        Timeout = Timeout,
        Wrapper = Wrapper?.Clone(),
        FacadeMode = FacadeMode,
    };
}
