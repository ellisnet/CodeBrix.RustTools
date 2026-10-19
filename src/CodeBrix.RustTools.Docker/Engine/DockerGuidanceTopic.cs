namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// What a piece of <see cref="DockerGuidance"/> resolves.
/// </summary>
public enum DockerGuidanceTopic
{
    /// <summary>Install Docker (Docker Desktop on Windows and macOS, Docker Engine on
    /// Linux).</summary>
    InstallDocker,

    /// <summary>Start Docker (or Docker Desktop) and wait until it is running.</summary>
    StartDocker,

    /// <summary>Let the current user use the Docker engine (Linux: the <c>docker</c>
    /// group).</summary>
    GrantDockerAccess,

    /// <summary>Install or update WSL 2, which Docker Desktop on Windows uses to run Linux
    /// containers.</summary>
    EnableWsl2Backend,

    /// <summary>Switch Docker Desktop from Windows containers to Linux containers.</summary>
    SwitchToLinuxContainers,

    /// <summary>Correct the configured Docker endpoint.</summary>
    CheckEndpoint,

    /// <summary>Make the <c>docker</c> command-line tool available on PATH.</summary>
    InstallDockerCli,

    /// <summary>Let the engine run images of another architecture (for example
    /// <c>linux/arm64</c> or <c>linux/riscv64</c> on an x86-64 engine): register qemu-user binfmt
    /// handlers on a Linux engine host (Docker Desktop brings its own emulation). Returned by
    /// <see cref="DockerRustWorkbench.ProbePlatformAsync"/> when a foreign platform's program
    /// cannot execute.</summary>
    EnableForeignPlatforms,
}
