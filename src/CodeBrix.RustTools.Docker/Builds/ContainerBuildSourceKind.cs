namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// Where the source of a container build came from.
/// </summary>
public enum ContainerBuildSourceKind
{
    /// <summary>A project folder on the host.</summary>
    ProjectDirectory,

    /// <summary>A source archive, extracted on the host for the build.</summary>
    Archive,

    /// <summary>Source the caller had already extracted.</summary>
    ExtractedSource,
}
