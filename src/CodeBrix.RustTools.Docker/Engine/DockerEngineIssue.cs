namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// A reason the Docker engine cannot be used for Rust builds, as found by
/// <see cref="DockerAvailability"/>.
/// </summary>
public enum DockerEngineIssue
{
    /// <summary>The endpoint is malformed or uses a scheme the Docker client does not support
    /// (for example <c>https://</c>).</summary>
    EndpointNotSupported,

    /// <summary>Nothing answered at the endpoint: Docker is not installed, not running, or the
    /// endpoint names the wrong place.</summary>
    NotReachable,

    /// <summary>The engine is running but this user may not use it (on Linux, typically not a
    /// member of the <c>docker</c> group).</summary>
    PermissionDenied,

    /// <summary>The engine answered but is running Windows containers; builds need Linux
    /// containers.</summary>
    NotLinuxDaemon,

    /// <summary>The <c>docker</c> command-line tool was not found on PATH. The engine itself may
    /// be fine, but building derived images uses the CLI (BuildKit).</summary>
    DockerCliNotFound,
}
