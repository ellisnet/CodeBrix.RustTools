using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// Thrown when work inside Docker fails: a base image cannot be pulled, a derived build image
/// fails to build, a build container cannot be created or started, or a command run inside a
/// container exits with a non-zero code.
/// </summary>
public class ContainerBuildException : DockerRustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    public ContainerBuildException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    /// <param name="innerException">The exception that caused this one, typically a
    /// <c>CodeBrix.Docker.DockerException</c>.</param>
    public ContainerBuildException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for a command that ran inside a container and failed.
    /// </summary>
    /// <param name="message">A description of what failed.</param>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="exitCode">The command's exit code.</param>
    /// <param name="standardOutput">What the command wrote to standard output.</param>
    /// <param name="standardError">What the command wrote to standard error.</param>
    public ContainerBuildException(string message, IReadOnlyList<string> command, long exitCode, string standardOutput, string standardError)
        : base(message)
    {
        Command = command;
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? string.Empty;
        StandardError = standardError ?? string.Empty;
    }

    /// <summary>The command that failed, or <c>null</c> when the failure was not a
    /// command.</summary>
    public IReadOnlyList<string> Command { get; }

    /// <summary>The failed command's exit code, or <c>null</c> when the failure was not a
    /// command.</summary>
    public long? ExitCode { get; }

    /// <summary>What the failed command wrote to standard output; empty when the failure was not
    /// a command.</summary>
    public string StandardOutput { get; } = string.Empty;

    /// <summary>What the failed command wrote to standard error; empty when the failure was not
    /// a command.</summary>
    public string StandardError { get; } = string.Empty;
}
