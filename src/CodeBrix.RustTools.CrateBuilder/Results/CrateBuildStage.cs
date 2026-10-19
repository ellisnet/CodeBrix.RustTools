namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The stages of <see cref="CrateBuilder.BuildAsync"/>, in the order they run.
/// </summary>
public enum CrateBuildStage
{
    /// <summary>Checking that this machine can build the request (toolchain or Docker
    /// engine, per target), before anything else runs.</summary>
    Readiness,

    /// <summary>Finding the local Cargo.toml, or cloning / fetching the Git repository and
    /// checking out the requested commit, branch or tag.</summary>
    ResolvingSource,

    /// <summary>Copying the crate (and its path dependencies) into a scratch workspace that
    /// the build may write to.</summary>
    Staging,

    /// <summary>Reading the crate's public API.</summary>
    ExtractingApi,

    /// <summary>Deciding how each public item maps to the C ABI and to C#.</summary>
    Planning,

    /// <summary>Writing the C facade crate for a plain Rust library.</summary>
    RenderingFacade,

    /// <summary>Running cargo for each target.</summary>
    Building,

    /// <summary>Checking the built files (architecture, exports, linked libraries).</summary>
    Verifying,

    /// <summary>Writing the .NET wrapper class library.</summary>
    Wrapping,

    /// <summary>Writing the JSON build record beside the artifacts.</summary>
    Recording,

    /// <summary>Removing the scratch workspace and any clone the request asked to remove.</summary>
    CleaningUp,
}
