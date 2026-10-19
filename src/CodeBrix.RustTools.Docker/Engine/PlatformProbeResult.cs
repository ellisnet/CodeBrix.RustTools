namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// The answer of <see cref="DockerRustWorkbench.ProbePlatformAsync"/>: whether containers of a
/// platform run on the engine, and why not when they do not.
/// </summary>
public sealed class PlatformProbeResult
{
    /// <summary>The probed platform, normalized (for example <c>linux/arm64</c>); <c>null</c>
    /// when the engine's own platform was asked for with <c>null</c>.</summary>
    public string Platform { get; init; }

    /// <summary>What the probe found.</summary>
    public PlatformProbeOutcome Outcome { get; init; }

    /// <summary>Whether containers of the platform run: <see cref="PlatformProbeOutcome.Native"/>
    /// or <see cref="PlatformProbeOutcome.Runs"/>.</summary>
    public bool CanRun => Outcome == PlatformProbeOutcome.Native || Outcome == PlatformProbeOutcome.Runs;

    /// <summary>Whether the platform is not the engine's own, so its containers run under
    /// emulation.</summary>
    public bool IsEmulated { get; init; }

    /// <summary>The machine <c>uname -m</c> reported inside the probe container (for example
    /// <c>aarch64</c>), or <c>null</c> when nothing ran.</summary>
    public string ReportedMachine { get; init; }

    /// <summary>The image the probe container ran (<see cref="DockerRustWorkbench.PlatformProbeImage"/>),
    /// or <c>null</c> when nothing was started.</summary>
    public string ProbeImage { get; init; }

    /// <summary>A one-sentence explanation, including the engine's own words on failure.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>What to do about a <see cref="PlatformProbeOutcome.NoEmulator"/> result (the
    /// <see cref="DockerGuidanceTopic.EnableForeignPlatforms"/> guidance for the host's operating
    /// system); <c>null</c> otherwise. CodeBrix.RustTools.Docker never runs it.</summary>
    public DockerGuidance Guidance { get; init; }

    /// <summary>The platform, the outcome and the message, for logs.</summary>
    public override string ToString() => $"{Platform ?? "(engine platform)"}: {Outcome} - {Message}";
}
