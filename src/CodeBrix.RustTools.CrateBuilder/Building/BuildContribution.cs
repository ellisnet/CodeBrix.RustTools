using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// What an <see cref="IBuildContribution"/> adds to one target's build. Every value may hold
/// <see cref="WorkspaceToken"/>.
/// </summary>
internal sealed class BuildContribution
{
    /// <summary>The token replaced by the staged workspace root in the mode of the build: the
    /// absolute host folder in host mode, <c>/work</c> (the container folder) in Docker
    /// mode.</summary>
    public const string WorkspaceToken = "{workspace}";

    /// <summary>Environment variables for cargo (a later contribution's value for the same
    /// name wins).</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Rustflags appended, space-separated, to the target's
    /// <c>CARGO_TARGET_&lt;TRIPLE&gt;_RUSTFLAGS</c> (after the link profile's own
    /// flags).</summary>
    public IList<string> RustFlags { get; } = new List<string>();

    /// <summary>Lines for the target's verification notes and the record.</summary>
    public IList<string> Notes { get; } = new List<string>();
}
