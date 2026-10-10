namespace CodeBrix.RustTools.Archives;

/// <summary>
/// Options for <see cref="SourceArchiveExtractor"/>.
/// </summary>
public sealed class SourceArchiveOptions
{
    /// <summary>
    /// The default for <see cref="MaxTotalBytes"/>: 2 GiB.
    /// </summary>
    public const long DefaultMaxTotalBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// The default for <see cref="MaxEntryCount"/>: 200,000 entries.
    /// </summary>
    public const int DefaultMaxEntryCount = 200_000;

    /// <summary>
    /// When true (the default) and every entry sits under one top-level folder (as in GitHub
    /// downloads: <c>repo-main/...</c>), that folder's contents are extracted directly into the
    /// destination and its name is reported in
    /// <see cref="ExtractedSource.StrippedRootFolderName"/>.
    /// </summary>
    public bool StripSingleRootFolder { get; set; } = true;

    /// <summary>
    /// Whether disposing the <see cref="ExtractedSource"/> deletes the extracted folder.
    /// <c>null</c> (the default) deletes it only when the extractor chose the folder (no
    /// destination was given).
    /// </summary>
    public bool? DeleteOnDispose { get; set; }

    /// <summary>
    /// The maximum number of bytes that may be extracted (a guard against archive bombs);
    /// defaults to <see cref="DefaultMaxTotalBytes"/>.
    /// </summary>
    public long MaxTotalBytes { get; set; } = DefaultMaxTotalBytes;

    /// <summary>
    /// The maximum number of entries the archive may contain; defaults to
    /// <see cref="DefaultMaxEntryCount"/>.
    /// </summary>
    public int MaxEntryCount { get; set; } = DefaultMaxEntryCount;

    /// <summary>
    /// When false (the default) extraction fails if a file already exists at an entry's
    /// destination; when true existing files are replaced.
    /// </summary>
    public bool Overwrite { get; set; }
}
