using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Thrown when the crate's source cannot be found or fetched: a missing Cargo.toml, a clone or fetch that fails, a commit, branch or tag that does not exist, or a clone folder that holds something other than a clone of the requested remote.
/// </summary>
public class SourceResolutionException : CrateBuilderException
{
    /// <summary>
    /// Creates an exception with the supplied message; its stage is
    /// <see cref="CrateBuildStage.ResolvingSource"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public SourceResolutionException(string message)
        : base(message, CrateBuildStage.ResolvingSource)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is
    /// <see cref="CrateBuildStage.ResolvingSource"/>.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public SourceResolutionException(string message, Exception innerException)
        : base(message, CrateBuildStage.ResolvingSource, innerException)
    {
    }
}
