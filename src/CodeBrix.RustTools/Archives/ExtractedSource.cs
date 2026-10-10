using System;
using System.Collections.Generic;
using System.IO;

namespace CodeBrix.RustTools.Archives;

/// <summary>
/// The folder a source archive was extracted into, with the Cargo project found in it.
/// Dispose it to delete the folder (when <see cref="DeleteOnDispose"/> is true).
/// </summary>
public sealed class ExtractedSource : IDisposable
{
    internal ExtractedSource(
        string archivePath,
        ArchiveKind archiveKind,
        string rootDirectory,
        string manifestPath,
        string strippedRootFolderName,
        int entryCount,
        long totalBytes,
        IReadOnlyList<string> skippedEntries,
        bool deleteOnDispose)
    {
        ArchivePath = archivePath;
        ArchiveKind = archiveKind;
        RootDirectory = rootDirectory;
        ManifestPath = manifestPath;
        ProjectDirectory = manifestPath == null ? null : Path.GetDirectoryName(manifestPath);
        StrippedRootFolderName = strippedRootFolderName;
        EntryCount = entryCount;
        TotalBytes = totalBytes;
        SkippedEntries = skippedEntries ?? Array.Empty<string>();
        DeleteOnDispose = deleteOnDispose;
    }

    /// <summary>
    /// The archive that was extracted.
    /// </summary>
    public string ArchivePath { get; }

    /// <summary>
    /// The detected archive format.
    /// </summary>
    public ArchiveKind ArchiveKind { get; }

    /// <summary>
    /// The folder the archive was extracted into (after stripping a single root folder).
    /// </summary>
    public string RootDirectory { get; }

    /// <summary>
    /// The folder of the Cargo project found in the extracted files: the workspace root when
    /// the archive holds a workspace, otherwise the shallowest package (found with
    /// <see cref="Projects.CargoProjectLocator"/>; a workspace root wins a tie at the same
    /// depth; <c>target</c>, <c>node_modules</c> and hidden folders are ignored). <c>null</c>
    /// when the archive contains no Cargo.toml.
    /// </summary>
    public string ProjectDirectory { get; }

    /// <summary>
    /// The full path of the Cargo.toml in <see cref="ProjectDirectory"/>, or <c>null</c>.
    /// </summary>
    public string ManifestPath { get; }

    /// <summary>
    /// The name of the single top-level folder that was stripped (for example
    /// <c>my-crate-main</c>), or <c>null</c> when nothing was stripped.
    /// </summary>
    public string StrippedRootFolderName { get; }

    /// <summary>
    /// The number of files and folders extracted.
    /// </summary>
    public int EntryCount { get; }

    /// <summary>
    /// The number of bytes written.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Entries that were not extracted, with the reason (symbolic links, hard links and special
    /// files are never extracted).
    /// </summary>
    public IReadOnlyList<string> SkippedEntries { get; }

    /// <summary>
    /// Whether <see cref="Dispose"/> deletes <see cref="RootDirectory"/>. Initialised from
    /// <see cref="SourceArchiveOptions.DeleteOnDispose"/>; set it to false to keep the files.
    /// </summary>
    public bool DeleteOnDispose { get; set; }

    /// <summary>
    /// True after <see cref="Dispose"/> has run.
    /// </summary>
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Deletes <see cref="RootDirectory"/> and everything in it when
    /// <see cref="DeleteOnDispose"/> is true (best effort: files still in use are left behind).
    /// Safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        if (!DeleteOnDispose || !Directory.Exists(RootDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(RootDirectory, recursive: true);
        }
        catch (IOException)
        {
            // Best effort: something still holds a file open.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: a read-only file or folder.
        }
    }
}
