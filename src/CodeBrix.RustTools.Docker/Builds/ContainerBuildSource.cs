using CodeBrix.RustTools.Docker.Containers;

namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// A summary of the source a container build used: where it came from on the host and where
/// it went in the container.
/// </summary>
public sealed class ContainerBuildSource
{
    /// <summary>Where the source came from.</summary>
    public ContainerBuildSourceKind Kind { get; init; }

    /// <summary>The archive, for <see cref="ContainerBuildSourceKind.Archive"/> and
    /// <see cref="ContainerBuildSourceKind.ExtractedSource"/>; otherwise <c>null</c>.</summary>
    public string ArchivePath { get; init; }

    /// <summary>The host folder that was copied into the container (the workspace root when
    /// the project is a workspace member).</summary>
    public string HostDirectory { get; init; } = string.Empty;

    /// <summary>The project's Cargo.toml on the host.</summary>
    public string HostManifestPath { get; init; } = string.Empty;

    /// <summary>The container folder cargo ran in: <c>/work</c>, or
    /// <c>/work/&lt;member&gt;</c> for a workspace member.</summary>
    public string ContainerDirectory { get; init; } = string.Empty;

    /// <summary>The package name, or <c>null</c> for a virtual workspace manifest.</summary>
    public string PackageName { get; init; }

    /// <summary>What the copy into the container transferred; <c>null</c> when the build
    /// stopped before the copy.</summary>
    public SourceCopyResult Copy { get; init; }

    /// <summary>Returns the host folder and container folder, for logs.</summary>
    public override string ToString() => $"{Kind}: {HostDirectory} -> {ContainerDirectory}";
}
