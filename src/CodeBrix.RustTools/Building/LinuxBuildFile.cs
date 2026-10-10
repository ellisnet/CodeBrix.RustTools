namespace CodeBrix.RustTools.Building;

/// <summary>
/// One file <see cref="RustWorkbench.BuildLinuxAsync"/> copied to the output folder.
/// </summary>
public sealed class LinuxBuildFile
{
    /// <summary>The file in the output folder.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The file name (<c>libmy_lib.so</c>).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The target it was built for.</summary>
    public string Triple { get; init; } = string.Empty;

    /// <summary>The .NET runtime identifier (<c>linux-arm64</c>, <c>linux-riscv64</c>,
    /// <c>linux-x64</c>).</summary>
    public string RuntimeIdentifier { get; init; } = string.Empty;

    /// <summary>The file it was copied from, in cargo's target folder.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Whether the file is a program rather than a shared library.</summary>
    public bool IsExecutable { get; init; }

    /// <summary>Returns the runtime identifier and path.</summary>
    public override string ToString() => $"{RuntimeIdentifier}: {Path}";
}
