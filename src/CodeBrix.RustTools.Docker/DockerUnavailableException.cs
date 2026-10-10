using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Engine;

namespace CodeBrix.RustTools.Docker;

/// <summary>
/// Thrown when an operation needs the Docker engine and the engine cannot be used: it is not
/// reachable, access to it was refused, or it is running Windows containers instead of Linux
/// containers. The exception carries the engine check and operating-system-specific guidance
/// for the consumer to show; the library never installs or starts anything itself.
/// </summary>
public class DockerUnavailableException : DockerRustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message and no engine details.
    /// </summary>
    /// <param name="message">A description of why the engine cannot be used.</param>
    public DockerUnavailableException(string message)
        : this(message, null, null, null)
    {
    }

    /// <summary>
    /// Creates an exception that carries the engine check and the matching guidance.
    /// </summary>
    /// <param name="message">A description of why the engine cannot be used.</param>
    /// <param name="engine">The engine check that found the problem, or <c>null</c>.</param>
    /// <param name="guidance">Guidance for the consumer to display, or <c>null</c> for
    /// none.</param>
    /// <param name="innerException">The Docker client exception that revealed the problem, or
    /// <c>null</c>.</param>
    public DockerUnavailableException(string message, DockerEngineInfo engine, IReadOnlyList<DockerGuidance> guidance, Exception innerException = null)
        : base(message, innerException)
    {
        Engine = engine;
        Guidance = guidance ?? Array.Empty<DockerGuidance>();
    }

    /// <summary>
    /// The engine check that detected the problem; <c>null</c> when the exception was raised
    /// without one.
    /// </summary>
    public DockerEngineInfo Engine { get; }

    /// <summary>
    /// Operating-system-specific instructions that resolve the problem (install Docker, start
    /// it, grant access to it, switch it to Linux containers). Never <c>null</c>; may be empty.
    /// The library never executes these commands.
    /// </summary>
    public IReadOnlyList<DockerGuidance> Guidance { get; }
}
