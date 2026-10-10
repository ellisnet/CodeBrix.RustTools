using System;

namespace CodeBrix.RustTools;

/// <summary>
/// Thrown when a source archive (zip, tar, tar.gz or tgz) cannot be read or contains an entry
/// that would be written outside the extraction folder.
/// </summary>
public class SourceArchiveException : RustToolsException
{
    /// <summary>
    /// Creates an exception with the supplied message.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    public SourceArchiveException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Creates an exception that wraps the lower-level exception which caused it.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="innerException">The exception raised while reading the archive.</param>
    public SourceArchiveException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception that names the archive which caused it.
    /// </summary>
    /// <param name="message">A description of what went wrong.</param>
    /// <param name="archivePath">The path of the archive being extracted.</param>
    /// <param name="innerException">The exception raised while reading the archive, or
    /// <c>null</c>.</param>
    public SourceArchiveException(string message, string archivePath, Exception innerException)
        : base(message, innerException)
    {
        ArchivePath = archivePath;
    }

    /// <summary>
    /// The path of the archive that could not be extracted, or <c>null</c> when not recorded.
    /// </summary>
    public string ArchivePath { get; }
}
