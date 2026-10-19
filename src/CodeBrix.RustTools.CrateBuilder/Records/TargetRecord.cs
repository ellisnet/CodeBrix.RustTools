using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// One requested target in a <see cref="BuildRecord"/>.
/// </summary>
public sealed class TargetRecord
{
    /// <summary>The .NET runtime identifier.</summary>
    public string Rid { get; set; }

    /// <summary>The Rust target triple.</summary>
    public string Triple { get; set; }

    /// <summary>What happened to the target.</summary>
    public CrateTargetOutcome Outcome { get; set; }

    /// <summary>Whether its files may be distributed.</summary>
    public ArtifactProvenance Provenance { get; set; }

    /// <summary>The files that were written for it.</summary>
    public IList<ArtifactRecord> Artifacts { get; set; } = new List<ArtifactRecord>();

    /// <summary>The explanation of the outcome, or <c>null</c>.</summary>
    public string Message { get; set; }
}
