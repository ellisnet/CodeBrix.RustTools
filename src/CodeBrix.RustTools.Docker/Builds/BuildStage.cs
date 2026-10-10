namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// The stages of a container build, in the order they happen, as reported in
/// <see cref="ContainerBuildProgress.Stage"/>.
/// </summary>
public enum BuildStage
{
    /// <summary>Checking that the Docker engine is reachable and runs Linux containers.</summary>
    CheckingEngine,

    /// <summary>Pulling the base image and building the derived build image when
    /// needed.</summary>
    EnsuringImage,

    /// <summary>Creating or reusing and starting the build container.</summary>
    StartingContainer,

    /// <summary>Copying the project source into the container.</summary>
    CopyingSource,

    /// <summary>Checking dependency licenses against the policy, inside the container.</summary>
    AuditingLicenses,

    /// <summary>Running cargo inside the container.</summary>
    Building,

    /// <summary>Copying the built files out of the container.</summary>
    RetrievingArtifacts,

    /// <summary>Stopping and removing what is not being kept.</summary>
    CleaningUp,

    /// <summary>The build finished (successfully or with compiler errors).</summary>
    Completed,

    /// <summary>The build could not run to completion.</summary>
    Failed,
}
