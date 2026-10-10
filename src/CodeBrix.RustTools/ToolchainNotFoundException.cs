using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools;

/// <summary>
/// Thrown when an operation needs a Rust (or C) tool that is not installed, not runnable or too
/// old on this machine. The exception carries the readiness evaluation and the install/update
/// guidance the consumer can show to the user; the library never installs anything itself.
/// </summary>
public class ToolchainNotFoundException : RustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message and no readiness details.
    /// </summary>
    /// <param name="message">A description of which tool is missing.</param>
    public ToolchainNotFoundException(string message)
        : this(message, null, null)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of which tool is missing.</param>
    /// <param name="innerException">The exception raised while trying to run the tool.</param>
    public ToolchainNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
        Guidance = Array.Empty<InstallGuidance>();
    }

    /// <summary>
    /// Creates an exception that carries the readiness evaluation and the matching guidance.
    /// </summary>
    /// <param name="message">A description of which tool is missing.</param>
    /// <param name="readiness">The evaluation that found the problem, or <c>null</c>.</param>
    /// <param name="guidance">Install/update guidance for the consumer to display, or
    /// <c>null</c> for none.</param>
    public ToolchainNotFoundException(string message, ToolchainReadiness readiness, IReadOnlyList<InstallGuidance> guidance)
        : base(message)
    {
        Readiness = readiness;
        Guidance = guidance ?? Array.Empty<InstallGuidance>();
    }

    /// <summary>
    /// The readiness evaluation that detected the missing or outdated tool; <c>null</c> when the
    /// exception was raised without one.
    /// </summary>
    public ToolchainReadiness Readiness { get; }

    /// <summary>
    /// Operating-system-specific instructions that resolve the problem. Never <c>null</c>; may be
    /// empty. The library never executes these commands.
    /// </summary>
    public IReadOnlyList<InstallGuidance> Guidance { get; }
}
