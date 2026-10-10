namespace CodeBrix.RustTools.Archives;

/// <summary>
/// The archive formats <see cref="SourceArchiveExtractor"/> understands.
/// </summary>
public enum ArchiveKind
{
    /// <summary>The format could not be recognised.</summary>
    Unknown,

    /// <summary>A zip file (<c>.zip</c>), as GitHub's "Download ZIP" produces.</summary>
    Zip,

    /// <summary>An uncompressed tar file (<c>.tar</c>).</summary>
    Tar,

    /// <summary>A gzip-compressed tar file (<c>.tar.gz</c> / <c>.tgz</c>), as GitHub's tarball
    /// download and crates.io's <c>.crate</c> files use.</summary>
    TarGzip,
}
