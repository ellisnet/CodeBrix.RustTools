using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Thrown when the crate's public API cannot be read, planned or turned into the C facade crate.
/// </summary>
public class FacadeGenerationException : CrateBuilderException
{
    /// <summary>
    /// Creates an exception with the supplied message; its stage is
    /// <see cref="CrateBuildStage.RenderingFacade"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public FacadeGenerationException(string message)
        : base(message, CrateBuildStage.RenderingFacade)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is
    /// <see cref="CrateBuildStage.RenderingFacade"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public FacadeGenerationException(string message, Exception innerException)
        : base(message, CrateBuildStage.RenderingFacade, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for one of the facade stages
    /// (<see cref="CrateBuildStage.ExtractingApi"/>, <see cref="CrateBuildStage.Planning"/>
    /// or <see cref="CrateBuildStage.RenderingFacade"/>).
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="stage">The facade stage that failed.</param>
    /// <param name="innerException">The exception that caused this one, or <c>null</c>.</param>
    public FacadeGenerationException(string message, CrateBuildStage stage, Exception innerException = null)
        : base(message, stage, innerException)
    {
    }
}
