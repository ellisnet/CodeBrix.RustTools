using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// The parts of <c>rustup show</c> output the detector uses.
/// </summary>
internal sealed class RustupShowResult
{
    public string DefaultHost { get; set; }

    public string RustupHome { get; set; }

    public string ActiveToolchain { get; set; }

    public string ActiveReason { get; set; }

    public IReadOnlyList<string> InstalledTargets { get; set; } = Array.Empty<string>();
}
