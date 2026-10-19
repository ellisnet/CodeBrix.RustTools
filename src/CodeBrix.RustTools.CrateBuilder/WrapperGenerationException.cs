using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Thrown when the .NET wrapper project cannot be written.
/// </summary>
public class WrapperGenerationException : CrateBuilderException
{
    /// <summary>
    /// Creates an exception with the supplied message; its stage is
    /// <see cref="CrateBuildStage.Wrapping"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public WrapperGenerationException(string message)
        : base(message, CrateBuildStage.Wrapping)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is
    /// <see cref="CrateBuildStage.Wrapping"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public WrapperGenerationException(string message, Exception innerException)
        : base(message, CrateBuildStage.Wrapping, innerException)
    {
    }
}
