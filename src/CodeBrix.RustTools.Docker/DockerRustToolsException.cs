using System;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// The base type of every exception CodeBrix.RustTools.Docker throws on purpose. It derives from
/// <see cref="RustToolsException"/>, so one catch clause covers both the local-toolchain library
/// and the container builds.
/// </summary>
public class DockerRustToolsException : RustToolsException
{
    /// <summary>
    /// Creates an exception with a generic message.
    /// </summary>
    public DockerRustToolsException()
        : base("A CodeBrix.RustTools.Docker operation failed.")
    {
    }

    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public DockerRustToolsException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one, typically a
    /// <c>CodeBrix.Docker.DockerException</c>.</param>
    public DockerRustToolsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
