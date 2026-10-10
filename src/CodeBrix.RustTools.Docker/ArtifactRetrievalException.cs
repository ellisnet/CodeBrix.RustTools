using System;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// Thrown when files built inside a container cannot be copied out to the host: the path does
/// not exist in the container, the destination folder cannot be written, or the transfer
/// fails.
/// </summary>
public class ArtifactRetrievalException : DockerRustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    public ArtifactRetrievalException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that names the container path and wraps the exception which caused
    /// it.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    /// <param name="containerPath">The path inside the container that could not be
    /// retrieved.</param>
    /// <param name="innerException">The exception that caused this one, or <c>null</c>.</param>
    public ArtifactRetrievalException(string message, string containerPath, Exception innerException = null)
        : base(message, innerException)
    {
        ContainerPath = containerPath;
    }

    /// <summary>The path inside the container that could not be retrieved, or
    /// <c>null</c>.</summary>
    public string ContainerPath { get; }
}
