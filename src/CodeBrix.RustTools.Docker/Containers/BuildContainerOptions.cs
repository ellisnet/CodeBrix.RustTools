using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Docker;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Settings for one build container started by
/// <see cref="DockerRustWorkbench.StartContainerAsync"/>.
/// </summary>
public sealed class BuildContainerOptions
{
    /// <summary>
    /// The container name. <c>null</c> (the default) generates one from the workbench's
    /// <see cref="DockerRustWorkbenchOptions.ContainerNamePrefix"/>: a random suffix normally,
    /// or the reuse key when <see cref="ReuseExisting"/> is set.
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// Whether to reuse a container this library created earlier with the same image and the
    /// same settings (matched through a label holding a hash of both) instead of creating a new
    /// one. A matching stopped container is started again. Default <c>false</c>.
    /// </summary>
    public bool ReuseExisting { get; set; }

    /// <summary>
    /// Whether the container is left running when the <see cref="BuildContainer"/> is disposed.
    /// <c>null</c> (the default) uses
    /// <see cref="DockerRustWorkbenchOptions.KeepContainerByDefault"/>.
    /// </summary>
    public bool? KeepOnDispose { get; set; }

    /// <summary>
    /// Extra environment variables for the container (and every command run in it). The
    /// container always gets <c>CARGO_TERM_COLOR=never</c> and <c>RUSTUP_AUTO_INSTALL=0</c>
    /// unless these override them.
    /// </summary>
    public IDictionary<string, string> Env { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// CPU, memory and process limits for the container; <c>null</c> (the default) leaves the
    /// engine's defaults.
    /// </summary>
    public ResourceLimits ResourceLimits { get; set; }

    /// <summary>
    /// Extra mounts, added after the cargo cache volumes and the target volume.
    /// </summary>
    public IList<MountSpec> Mounts { get; set; } = new List<MountSpec>();

    /// <summary>
    /// The named volume mounted at <c>/work/target</c> (cargo's output folder). <c>null</c> (the
    /// default) uses <c>&lt;container name&gt;-target</c>. The volume is removed with the
    /// container unless the container is kept.
    /// </summary>
    public string TargetVolumeName { get; set; }

    /// <summary>
    /// Returns a copy of these options (collections are copied; mount and limit objects are
    /// shared).
    /// </summary>
    public BuildContainerOptions Clone() => new BuildContainerOptions
    {
        Name = Name,
        ReuseExisting = ReuseExisting,
        KeepOnDispose = KeepOnDispose,
        Env = Env == null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(Env, StringComparer.Ordinal),
        ResourceLimits = ResourceLimits,
        Mounts = Mounts == null ? new List<MountSpec>() : Mounts.ToList(),
        TargetVolumeName = TargetVolumeName,
    };
}
