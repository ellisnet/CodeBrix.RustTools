using System;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// The time limits <see cref="DockerRustWorkbench"/> applies to Docker operations. Every value
/// must be positive; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> disables a limit.
/// </summary>
public sealed class DockerRustTimeouts
{
    /// <summary>
    /// The limit for each ordinary (non-streaming) Docker Engine API call, such as inspecting a
    /// container or creating a volume. Handed to the Docker client as its default timeout when
    /// <see cref="DockerRustWorkbenchOptions.ClientOptions"/> is not supplied. Default: 100
    /// seconds.
    /// </summary>
    public TimeSpan ApiCall { get; set; } = TimeSpan.FromSeconds(100);

    /// <summary>
    /// The limit for preparing a build image: pulling the base image and building the derived
    /// image from the generated Dockerfile. The first pull of the official <c>rust</c> image is
    /// well over a gigabyte, and the Android layer downloads the NDK, so this is generous.
    /// Default: 60 minutes.
    /// </summary>
    public TimeSpan ImagePreparation { get; set; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// The limit for one command run inside a build container through
    /// <see cref="DockerRustWorkbench.ExecAsync"/>. Default: 30 minutes.
    /// </summary>
    public TimeSpan Command { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Returns a copy of these timeouts.
    /// </summary>
    public DockerRustTimeouts Clone() => new DockerRustTimeouts
    {
        ApiCall = ApiCall,
        ImagePreparation = ImagePreparation,
        Command = Command,
    };
}
