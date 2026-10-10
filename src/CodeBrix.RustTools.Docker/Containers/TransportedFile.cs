namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// One file copied out of a build container.
/// </summary>
internal sealed class TransportedFile
{
    /// <summary>The file's path inside the container.</summary>
    public string ContainerPath { get; init; } = string.Empty;

    /// <summary>Where the file was written on the host.</summary>
    public string HostPath { get; init; } = string.Empty;

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>The file's Unix permission bits in the container (for example 0755).</summary>
    public int Mode { get; init; }

    /// <summary>Whether any execute bit is set in <see cref="Mode"/>.</summary>
    public bool IsExecutable => (Mode & 0b001_001_001) != 0;
}
