using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Thrown when a built file is not what the target needs: the wrong architecture or format, missing exports, or a linked library it must not carry.
/// </summary>
public class ArtifactVerificationException : CrateBuilderException
{
    /// <summary>
    /// Creates an exception with the supplied message; its stage is
    /// <see cref="CrateBuildStage.Verifying"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public ArtifactVerificationException(string message)
        : base(message, CrateBuildStage.Verifying)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is
    /// <see cref="CrateBuildStage.Verifying"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public ArtifactVerificationException(string message, Exception innerException)
        : base(message, CrateBuildStage.Verifying, innerException)
    {
    }
}
