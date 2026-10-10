using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Artifacts;

/// <summary>
/// One file copied out of a build container to the host.
/// </summary>
public sealed class RetrievedArtifact
{
    /// <summary>Where the file is on the host.</summary>
    public string HostPath { get; init; } = string.Empty;

    /// <summary>The file's path inside the build container (for example
    /// <c>/work/target/debug/my_app</c>).</summary>
    public string ContainerPath { get; init; } = string.Empty;

    /// <summary>The target the file was built for.</summary>
    public CrossTarget Target { get; init; }

    /// <summary>What the file is.</summary>
    public ArtifactKind Kind { get; init; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The SHA-256 of the file as written on the host, as 64 lowercase hexadecimal
    /// digits.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>Whether the file had an execute bit in the container. On Linux and macOS hosts
    /// the container's permission bits are applied to the host file as well; Windows hosts
    /// record them only.</summary>
    public bool IsExecutable { get; init; }

    /// <summary>The cargo package that produced the file.</summary>
    public string PackageName { get; init; } = string.Empty;

    /// <summary>The cargo target (bin or lib name) that produced the file.</summary>
    public string TargetName { get; init; } = string.Empty;

    /// <summary>Returns the kind, target and host path, for logs.</summary>
    public override string ToString() => $"{Kind} for {Target}: {HostPath} ({Size} bytes)";
}
