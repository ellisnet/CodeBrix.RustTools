namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// One native file in a <see cref="TargetRecord"/>.
/// </summary>
public sealed class ArtifactRecord
{
    /// <summary>The file's path relative to the output folder, with forward slashes (for
    /// example <c>runtimes/linux-x64/native/libmy_crate.so</c>).</summary>
    public string Path { get; set; }

    /// <summary>The SHA-256 of the file, 64 lowercase hex digits.</summary>
    public string Sha256 { get; set; }

    /// <summary>The file's size in bytes.</summary>
    public long Length { get; set; }
}
