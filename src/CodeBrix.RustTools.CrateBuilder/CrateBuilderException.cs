using System;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The base type of every exception CodeBrix.RustTools.CrateBuilder raises on purpose. It
/// derives from <see cref="RustToolsException"/>, so one catch clause covers the whole
/// CodeBrix.RustTools family. <see cref="CrateBuilder.BuildAsync"/> does not throw these for a
/// failed stage: it reports them in <see cref="CrateBuildResult.FailedStage"/> and
/// <see cref="CrateBuildResult.FailureMessage"/>.
/// </summary>
public class CrateBuilderException : RustToolsException
{
    /// <summary>
    /// Creates an exception whose stage is filled in by the pipeline (the stage that was
    /// running when it was raised).
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public CrateBuilderException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the exception which caused it; its stage is filled in
    /// by the pipeline.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception that caused this one.</param>
    public CrateBuilderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception for a known stage.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="stage">The stage that failed.</param>
    /// <param name="innerException">The exception that caused this one, or <c>null</c>.</param>
    public CrateBuilderException(string message, CrateBuildStage stage, Exception innerException = null)
        : base(message, innerException)
    {
        Stage = stage;
        HasStage = true;
    }

    /// <summary>The stage that failed. For an exception created without one it is the stage
    /// the pipeline was running when the exception reached it (and
    /// <see cref="CrateBuildStage.Readiness"/> until then).</summary>
    public CrateBuildStage Stage { get; private set; }

    /// <summary>Whether <see cref="Stage"/> was given when the exception was created (or has
    /// since been filled in).</summary>
    internal bool HasStage { get; private set; }

    /// <summary>Fills in the stage when none was given.</summary>
    internal void AssignStageIfUnknown(CrateBuildStage stage)
    {
        if (!HasStage)
        {
            Stage = stage;
            HasStage = true;
        }
    }
}
