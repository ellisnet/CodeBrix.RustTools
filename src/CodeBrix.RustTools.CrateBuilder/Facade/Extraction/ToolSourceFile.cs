namespace CodeBrix.RustTools.CrateBuilder.Facade.Extraction;

/// <summary>
/// One file of the embedded API extractor source.
/// </summary>
internal sealed class ToolSourceFile
{
    /// <summary>Creates a file entry.</summary>
    /// <param name="relativePath">The path below the tool folder, with '/'.</param>
    /// <param name="content">The file's bytes.</param>
    public ToolSourceFile(string relativePath, byte[] content)
    {
        RelativePath = relativePath;
        Content = content ?? new byte[0];
    }

    /// <summary>The path below the tool folder, with '/' (<c>src/main.rs</c>).</summary>
    public string RelativePath { get; }

    /// <summary>The file's bytes.</summary>
    public byte[] Content { get; }
}
