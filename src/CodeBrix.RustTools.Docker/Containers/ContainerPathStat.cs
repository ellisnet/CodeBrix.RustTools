namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// What the engine reports about one path inside a build container.
/// </summary>
internal sealed class ContainerPathStat
{
    /// <summary>Whether the path exists.</summary>
    public bool Exists { get; init; }

    /// <summary>The final path component.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The size in bytes (0 for a folder).</summary>
    public long Size { get; init; }

    /// <summary>The Unix permission bits.</summary>
    public int UnixMode { get; init; }

    /// <summary>Whether the path is a folder.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>The link target when the path is a symbolic link; otherwise <c>null</c>.</summary>
    public string LinkTarget { get; init; }
}
