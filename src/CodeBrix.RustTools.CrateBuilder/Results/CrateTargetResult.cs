using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The outcome of one requested target.
/// </summary>
public sealed class CrateTargetResult
{
    /// <summary>The target.</summary>
    public CrateTarget Target { get; set; }

    /// <summary>Its .NET runtime identifier.</summary>
    public string Rid { get; set; }

    /// <summary>Its Rust target triple.</summary>
    public string Triple { get; set; }

    /// <summary>What happened.</summary>
    public CrateTargetOutcome Outcome { get; set; }

    /// <summary>The absolute paths of the native files copied to the output folder.</summary>
    public IReadOnlyList<string> ArtifactPaths { get; set; } = Array.Empty<string>();

    /// <summary>Whether the files may be distributed (macOS files built without Apple's
    /// toolchain are development builds).</summary>
    public ArtifactProvenance Provenance { get; set; }

    /// <summary>What to install to make the target buildable
    /// (<see cref="CrateTargetOutcome.NotBuildableInThisMode"/>).</summary>
    public IReadOnlyList<InstallGuidance> Guidance { get; set; } = Array.Empty<InstallGuidance>();

    /// <summary>A short explanation of the outcome, or <c>null</c>.</summary>
    public string Message { get; set; }

    /// <summary>cargo's result (diagnostics, artifacts; in Docker mode its paths are container
    /// paths); <c>null</c> when cargo did not run.</summary>
    public CargoCommandResult CargoResult { get; set; }

    /// <summary>What the verification of the built files found (one line each).</summary>
    public IReadOnlyList<string> VerificationNotes { get; set; } = Array.Empty<string>();

    /// <summary>"rid: outcome" and the message when there is one.</summary>
    public override string ToString() => Rid + ": " + Outcome + (string.IsNullOrEmpty(Message) ? string.Empty : " - " + Message);
}
