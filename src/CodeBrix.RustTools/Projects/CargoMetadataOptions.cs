using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// Options for running <c>cargo metadata</c> (<see cref="CargoProject.GetMetadataAsync"/>).
/// </summary>
public sealed class CargoMetadataOptions
{
    /// <summary>Passes <c>--offline</c>: use only the local registry cache, never the network.</summary>
    public bool Offline { get; set; }

    /// <summary>Passes <c>--locked</c>: fail instead of updating Cargo.lock.</summary>
    public bool Locked { get; set; }

    /// <summary>Passes <c>--frozen</c> (<c>--locked</c> and <c>--offline</c> together).</summary>
    public bool Frozen { get; set; }

    /// <summary>Passes <c>--no-deps</c>: report only the workspace members, with no resolve
    /// graph (fast, and needs no network).</summary>
    public bool NoDependencies { get; set; }

    /// <summary>Passes <c>--filter-platform TRIPLE</c>: keep only dependencies that apply to
    /// that target triple. <c>null</c> keeps every platform's dependencies.</summary>
    public string FilterPlatform { get; set; }

    /// <summary>Passes <c>--all-features</c>.</summary>
    public bool AllFeatures { get; set; }

    /// <summary>Passes <c>--no-default-features</c>.</summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>Features to enable (<c>--features a,b</c>).</summary>
    public IList<string> Features { get; } = new List<string>();

    /// <summary>The maximum run time; <c>null</c> uses 5 minutes. cargo may download the
    /// registry index on the first run.</summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Extra arguments appended as-is.</summary>
    public IList<string> ExtraArguments { get; } = new List<string>();
}
