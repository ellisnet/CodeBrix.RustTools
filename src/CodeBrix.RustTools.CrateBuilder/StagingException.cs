using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Thrown when the crate cannot be copied into the scratch workspace: a path dependency outside the source tree, a workspace whose inheritance cannot be resolved, or a file that cannot be copied.
/// </summary>
public class StagingException : CrateBuilderException
{
    /// <summary>
    /// Creates an exception with the supplied message; its stage is
    /// <see cref="CrateBuildStage.Staging"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public StagingException(string message)
        : base(message, CrateBuildStage.Staging)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is
    /// <see cref="CrateBuildStage.Staging"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public StagingException(string message, Exception innerException)
        : base(message, CrateBuildStage.Staging, innerException)
    {
    }
}
