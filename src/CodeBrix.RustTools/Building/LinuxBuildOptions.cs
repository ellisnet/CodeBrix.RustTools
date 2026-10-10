using System.Collections.Generic;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// What <see cref="RustWorkbench.BuildLinuxAsync"/> builds and how it lays the result out.
/// </summary>
public sealed class LinuxBuildOptions
{
    /// <summary>The link profile to apply; <c>null</c> (the default) uses
    /// <see cref="LinuxLinkProfile.ForTriple"/> for each target, with the tools on PATH or in
    /// <see cref="ToolDirectory"/>. A profile serves only the targets of its
    /// <see cref="LinuxLinkProfile.ToolPrefix"/>; with an empty <see cref="Triples"/> it selects
    /// them.</summary>
    public LinuxLinkProfile Profile { get; set; }

    /// <summary>The folder that holds the cross tools when <see cref="Profile"/> is <c>null</c>,
    /// or <c>null</c> (the default) when they are on PATH.</summary>
    public string ToolDirectory { get; set; }

    /// <summary>The targets to build, in order. Empty (the default) builds every Linux target
    /// (<see cref="LinuxLinkProfile.Triples"/>) other than the host's own triple - on an x86-64
    /// Linux host <c>aarch64-unknown-linux-gnu</c> and <c>riscv64gc-unknown-linux-gnu</c>. The
    /// host's own triple is never accepted: build it natively with
    /// <see cref="RustWorkbench.BuildAsync"/>.</summary>
    public IList<string> Triples { get; } = new List<string>();

    /// <summary>The folder layout of the output; <see cref="LinuxOutputLayout.NuGetRuntimes"/>
    /// by default.</summary>
    public LinuxOutputLayout Layout { get; set; } = LinuxOutputLayout.NuGetRuntimes;

    /// <summary>Whether programs (binaries) are copied as well as shared libraries. Default
    /// false: a NuGet package carries libraries only.</summary>
    public bool IncludeExecutables { get; set; }
}
