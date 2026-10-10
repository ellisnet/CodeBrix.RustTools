using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// The result of checking the Docker engine: whether it can run Linux build containers, what it
/// reported about itself, and what is wrong when it cannot.
/// </summary>
public sealed class DockerEngineInfo
{
    /// <summary>
    /// Whether the engine answered and runs Linux containers, so builds can proceed. An engine
    /// whose only issue is <see cref="DockerEngineIssue.DockerCliNotFound"/> still counts as
    /// available (only derived-image builds need the CLI).
    /// </summary>
    public bool IsAvailable { get; init; }

    /// <summary>The endpoint that was checked, as resolved by the Docker client (for example
    /// <c>unix:///var/run/docker.sock</c>); the configured text when it could not be
    /// resolved.</summary>
    public string Endpoint { get; init; }

    /// <summary>The engine's version (for example <c>29.8.2</c>), or <c>null</c> when it did not
    /// answer.</summary>
    public string ServerVersion { get; init; }

    /// <summary>The Engine API version the engine speaks, or <c>null</c>.</summary>
    public string ApiVersion { get; init; }

    /// <summary>The engine host's operating system as the engine describes it (for example
    /// <c>Debian GNU/Linux 13 (trixie)</c> or <c>Docker Desktop</c>), or <c>null</c>.</summary>
    public string OperatingSystem { get; init; }

    /// <summary>The kind of containers the engine runs: <c>linux</c> or <c>windows</c>; <c>null</c>
    /// when it did not answer.</summary>
    public string OsType { get; init; }

    /// <summary>The engine host's CPU architecture (for example <c>x86_64</c> or
    /// <c>aarch64</c>), or <c>null</c>. Build containers compile for this architecture's Linux
    /// target natively.</summary>
    public string Architecture { get; init; }

    /// <summary>Whether the engine runs Linux containers.</summary>
    public bool IsLinuxDaemon { get; init; }

    /// <summary>Whether the engine looks like Docker Desktop (Windows or macOS, or Docker Desktop
    /// for Linux).</summary>
    public bool IsDockerDesktop { get; init; }

    /// <summary>Whether the <c>docker</c> command-line tool was found on PATH (or at the
    /// configured path).</summary>
    public bool DockerCliFound { get; init; }

    /// <summary>The operating system of the machine this process runs on, used to pick
    /// guidance.</summary>
    public OperatingSystemKind HostOperatingSystem { get; init; }

    /// <summary>Everything found wrong; empty when the engine is fully usable.</summary>
    public IReadOnlyList<DockerEngineIssue> Issues { get; init; } = Array.Empty<DockerEngineIssue>();

    /// <summary>The Docker client's own explanation when the engine could not be used, or
    /// <c>null</c>.</summary>
    public string ErrorMessage { get; init; }

    /// <summary>The Rust target triple of the engine's own Linux platform, derived from
    /// <see cref="Architecture"/> (<c>x86_64-unknown-linux-gnu</c>, <c>aarch64-unknown-linux-gnu</c>
    /// or <c>riscv64gc-unknown-linux-gnu</c>); <c>null</c> when the architecture is unknown.</summary>
    public string NativeLinuxTriple => Architecture switch
    {
        "x86_64" or "amd64" => "x86_64-unknown-linux-gnu",
        "aarch64" or "arm64" => "aarch64-unknown-linux-gnu",
        "riscv64" => "riscv64gc-unknown-linux-gnu",
        _ => null,
    };

    /// <summary>The .NET runtime identifier of the engine's own Linux platform, derived from
    /// <see cref="Architecture"/> (<c>linux-x64</c> or <c>linux-arm64</c>); <c>null</c> when the
    /// architecture is unknown. Artifact retrieval names the <c>runtimes/&lt;rid&gt;/native</c>
    /// folder of the container's own build output after it.</summary>
    public string NativeLinuxRuntimeIdentifier => Architecture switch
    {
        "x86_64" or "amd64" => "linux-x64",
        "aarch64" or "arm64" => "linux-arm64",
        "riscv64" => "linux-riscv64",
        _ => null,
    };

    /// <summary>A one-line description, for logs.</summary>
    public override string ToString()
        => IsAvailable
            ? $"Docker {ServerVersion} ({OsType}/{Architecture}) at {Endpoint}"
            : $"Docker unavailable at {Endpoint}: {string.Join(", ", Issues)}{(string.IsNullOrEmpty(ErrorMessage) ? string.Empty : " - " + ErrorMessage)}";
}
