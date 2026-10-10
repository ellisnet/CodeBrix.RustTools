namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// One progress report while project files are copied into a build container: reported once
/// per file.
/// </summary>
public sealed class SourceCopyProgress
{
    /// <summary>The number of files sent so far, including this one.</summary>
    public long FilesCopied { get; init; }

    /// <summary>The number of bytes sent so far.</summary>
    public long BytesCopied { get; init; }

    /// <summary>The path of the file just sent, relative to the project folder, with forward
    /// slashes.</summary>
    public string CurrentPath { get; init; } = string.Empty;

    /// <summary>Returns the counts and the current file, for logs.</summary>
    public override string ToString() => $"{FilesCopied} file(s), {BytesCopied} byte(s): {CurrentPath}";
}
