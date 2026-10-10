namespace CodeBrix.RustTools.Building;

/// <summary>
/// One file <see cref="RustWorkbench.BuildAndroidAsync"/> copied to the output folder.
/// </summary>
public sealed class AndroidBuildFile
{
    /// <summary>The file in the output folder.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>The file name (<c>libmy_lib.so</c>).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>The target it was built for.</summary>
    public string Triple { get; init; } = string.Empty;

    /// <summary>The Android ABI (<c>arm64-v8a</c>, <c>x86_64</c>).</summary>
    public string Abi { get; init; } = string.Empty;

    /// <summary>The .NET runtime identifier (<c>android-arm64</c>, <c>android-x64</c>).</summary>
    public string RuntimeIdentifier { get; init; } = string.Empty;

    /// <summary>The file it was copied from, in cargo's target folder.</summary>
    public string SourcePath { get; init; } = string.Empty;

    /// <summary>Whether the file is a program rather than a shared library.</summary>
    public bool IsExecutable { get; init; }

    /// <summary>Returns the ABI and path.</summary>
    public override string ToString() => $"{Abi}: {Path}";
}
