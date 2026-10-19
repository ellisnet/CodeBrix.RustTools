namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// What <see cref="DockerRustWorkbench.ProbePlatformAsync"/> found out about a platform.
/// </summary>
public enum PlatformProbeOutcome
{
    /// <summary>The platform is the engine's own (or none was given): it runs natively and
    /// nothing was started to find out.</summary>
    Native,

    /// <summary>A container of the platform ran and reported the platform's machine: the engine
    /// runs it (under emulation when it is foreign).</summary>
    Runs,

    /// <summary>The platform's program could not execute ("exec format error"): the engine
    /// host has no emulator for the architecture (on Linux, no qemu-user binfmt handler). See
    /// <see cref="PlatformProbeResult.Guidance"/>.</summary>
    NoEmulator,

    /// <summary>The engine or the registry refused the platform: the probe image has no variant
    /// for it, the pull failed, or the container could not be created or started for another
    /// reason (see <see cref="PlatformProbeResult.Message"/>).</summary>
    Refused,

    /// <summary>The container ran but failed, or reported another machine than the platform's
    /// (see <see cref="PlatformProbeResult.Message"/>).</summary>
    Failed,
}
