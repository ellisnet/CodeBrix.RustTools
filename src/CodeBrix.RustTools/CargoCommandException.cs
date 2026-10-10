using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools;

/// <summary>
/// Thrown when a cargo command cannot be run or fails in a way that is not reported through a
/// normal command result - for example when a cargo subcommand such as clippy or fmt is not
/// installed for the active toolchain.
/// </summary>
public class CargoCommandException : RustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public CargoCommandException(string message)
        : base(message)
    {
        Guidance = Array.Empty<InstallGuidance>();
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception raised while running cargo.</param>
    public CargoCommandException(string message, Exception innerException)
        : base(message, innerException)
    {
        Guidance = Array.Empty<InstallGuidance>();
    }

    /// <summary>
    /// Creates an exception that records how cargo exited and how to fix the problem.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="exitCode">The cargo process exit code, or <c>null</c> when cargo did not
    /// run to completion.</param>
    /// <param name="standardError">The text cargo wrote to standard error, or <c>null</c>.</param>
    /// <param name="guidance">Install/update guidance for the consumer to display, or
    /// <c>null</c> for none.</param>
    public CargoCommandException(string message, int? exitCode, string standardError, IReadOnlyList<InstallGuidance> guidance)
        : base(message)
    {
        ExitCode = exitCode;
        StandardError = standardError;
        Guidance = guidance ?? Array.Empty<InstallGuidance>();
    }

    /// <summary>
    /// The cargo process exit code; <c>null</c> when cargo did not run to completion.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// The text cargo wrote to standard error, or <c>null</c> when it was not captured.
    /// </summary>
    public string StandardError { get; }

    /// <summary>
    /// Operating-system-specific instructions that resolve the problem (for example
    /// <c>rustup component add clippy</c>). Never <c>null</c>; may be empty.
    /// </summary>
    public IReadOnlyList<InstallGuidance> Guidance { get; }
}
