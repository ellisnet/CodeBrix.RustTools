using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Archives;

/// <summary>
/// Extracts source archives (zip, tar, tar.gz / tgz) safely: every entry is checked so nothing
/// can be written outside the destination folder (absolute paths, drive letters and <c>..</c>
/// segments are rejected), links and special files are skipped, size and entry-count limits
/// guard against archive bombs, and GitHub's single top-level folder is stripped by default.
/// The Cargo project in the extracted files is located for you.
/// </summary>
public sealed class SourceArchiveExtractor
{
    private const int MagicLength = 512;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;
    private const int UnixPermissionMask = 0x1FF;
    private const int ManifestSearchDepth = 8;

    /// <summary>
    /// Creates an extractor.
    /// </summary>
    public SourceArchiveExtractor()
    {
    }

    /// <summary>
    /// The folder under which archives are extracted when no destination is given:
    /// <c>&lt;temp&gt;/CodeBrix.RustTools</c> (each extraction gets its own sub-folder).
    /// </summary>
    public static string DefaultExtractionRoot => Path.Combine(Path.GetTempPath(), "CodeBrix.RustTools");

    /// <summary>
    /// Detects the archive format from the file's first bytes (zip <c>PK\x03\x04</c>, gzip
    /// <c>\x1f\x8b</c>, tar <c>ustar</c> at offset 257), falling back to the file extension
    /// (<c>.zip</c>, <c>.tar</c>, <c>.tar.gz</c>, <c>.tgz</c>, <c>.crate</c>).
    /// </summary>
    /// <param name="path">The archive file.</param>
    /// <returns>The format, or <see cref="ArchiveKind.Unknown"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    public ArchiveKind DetectKind(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("An archive path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"The archive '{path}' does not exist.", path);
        }

        var header = new byte[MagicLength];
        int read;
        using (var stream = File.OpenRead(path))
        {
            read = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        }

        var byMagic = DetectKind(header.AsSpan(0, read));
        return byMagic != ArchiveKind.Unknown ? byMagic : DetectKindFromExtension(path);
    }

    /// <summary>
    /// Extracts an archive.
    /// </summary>
    /// <param name="archivePath">The zip, tar, tar.gz or tgz file.</param>
    /// <param name="destinationDirectory">The folder to extract into (created when missing);
    /// <c>null</c> extracts into a new folder under <see cref="DefaultExtractionRoot"/>.</param>
    /// <param name="options">Extraction options; <c>null</c> uses the defaults.</param>
    /// <returns>The extracted folder and the project found in it. Dispose it to delete the
    /// folder when <see cref="ExtractedSource.DeleteOnDispose"/> is true.</returns>
    /// <exception cref="ArgumentException"><paramref name="archivePath"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The archive does not exist.</exception>
    /// <exception cref="SourceArchiveException">The format is not recognised, the archive is
    /// corrupt, an entry would be written outside the destination, a limit was exceeded, or a
    /// file already exists and <see cref="SourceArchiveOptions.Overwrite"/> is false. A folder
    /// the extractor created is deleted again.</exception>
    public ExtractedSource Extract(string archivePath, string destinationDirectory = null, SourceArchiveOptions options = null)
        => ExtractCore(archivePath, destinationDirectory, options, CancellationToken.None);

    /// <summary>
    /// Extracts an archive on a background thread. See <see cref="Extract"/>.
    /// </summary>
    /// <param name="archivePath">The zip, tar, tar.gz or tgz file.</param>
    /// <param name="destinationDirectory">The folder to extract into; <c>null</c> for a new
    /// temporary folder.</param>
    /// <param name="options">Extraction options; <c>null</c> uses the defaults.</param>
    /// <param name="cancellationToken">Stops extraction between entries; a folder the extractor
    /// created is deleted again.</param>
    public Task<ExtractedSource> ExtractAsync(string archivePath, string destinationDirectory = null, SourceArchiveOptions options = null, CancellationToken cancellationToken = default)
        => Task.Run(() => ExtractCore(archivePath, destinationDirectory, options, cancellationToken), cancellationToken);

    internal static ArchiveKind DetectKind(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 4 && header[0] == (byte)'P' && header[1] == (byte)'K'
            && ((header[2] == 3 && header[3] == 4) || (header[2] == 5 && header[3] == 6)))
        {
            return ArchiveKind.Zip;
        }

        if (header.Length >= 2 && header[0] == 0x1F && header[1] == 0x8B)
        {
            return ArchiveKind.TarGzip;
        }

        if (header.Length >= 262 && header[257] == (byte)'u' && header[258] == (byte)'s' && header[259] == (byte)'t'
            && header[260] == (byte)'a' && header[261] == (byte)'r')
        {
            return ArchiveKind.Tar;
        }

        return ArchiveKind.Unknown;
    }

    internal static ArchiveKind DetectKindFromExtension(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            return ArchiveKind.Zip;
        }

        if (name.EndsWith(".tar.gz", StringComparison.Ordinal) || name.EndsWith(".tgz", StringComparison.Ordinal) || name.EndsWith(".crate", StringComparison.Ordinal))
        {
            return ArchiveKind.TarGzip;
        }

        return name.EndsWith(".tar", StringComparison.Ordinal) ? ArchiveKind.Tar : ArchiveKind.Unknown;
    }

    /// <summary>
    /// Validates an entry name and returns it as a relative '/'-separated path ("" for the
    /// archive root). Throws for absolute paths, drive letters and <c>..</c> segments.
    /// </summary>
    internal static string NormalizeEntryName(string name, string archivePath)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new SourceArchiveException("The archive contains an entry with an empty name.", archivePath, null);
        }

        var normalized = name.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            throw new SourceArchiveException($"The archive entry '{name}' has an absolute path; refusing to extract it.", archivePath, null);
        }

        if (normalized.Length >= 2 && char.IsAsciiLetter(normalized[0]) && normalized[1] == ':')
        {
            throw new SourceArchiveException($"The archive entry '{name}' has a drive-letter path; refusing to extract it.", archivePath, null);
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToList();
        if (segments.Any(s => s == ".."))
        {
            throw new SourceArchiveException($"The archive entry '{name}' contains a '..' segment that would escape the destination folder; refusing to extract it.", archivePath, null);
        }

        if (segments.Any(s => s.IndexOf('\0') >= 0))
        {
            throw new SourceArchiveException($"The archive entry '{name}' contains a NUL character.", archivePath, null);
        }

        return string.Join("/", segments);
    }

    /// <summary>
    /// Returns the single top-level folder shared by every file and folder entry, or
    /// <c>null</c> when the entries do not all live under one folder.
    /// </summary>
    internal static string FindSingleRootFolder(IEnumerable<(string Path, bool IsDirectory)> entries)
    {
        string root = null;
        var hasNestedEntry = false;
        foreach (var (path, isDirectory) in entries)
        {
            if (path.Length == 0)
            {
                continue;
            }

            var slash = path.IndexOf('/');
            var first = slash < 0 ? path : path.Substring(0, slash);
            if (slash < 0 && !isDirectory)
            {
                return null;
            }

            if (slash >= 0)
            {
                hasNestedEntry = true;
            }

            if (root == null)
            {
                root = first;
            }
            else if (!string.Equals(root, first, StringComparison.Ordinal))
            {
                return null;
            }
        }

        return hasNestedEntry ? root : null;
    }

    /// <summary>
    /// Chooses the Cargo.toml that represents the project in an extracted folder, using
    /// <see cref="CargoProjectLocator.FindProjectRoots"/> (which skips <c>target</c>,
    /// <c>node_modules</c> and hidden folders, and never returns a manifest nested inside another
    /// one): the shallowest project root wins, and a workspace root wins a tie at the same
    /// depth. Returns <c>null</c> when the folder holds no Cargo.toml.
    /// </summary>
    internal static string FindProjectManifest(string rootDirectory, int maxDepth = ManifestSearchDepth)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory) || !Directory.Exists(rootDirectory))
        {
            return null;
        }

        var root = Path.GetFullPath(rootDirectory);
        return CargoProjectLocator.FindProjectRoots(root, maxDepth)
            .Select(path => (Path: path, Depth: Depth(root, path), IsWorkspace: CargoProjectLocator.TryLoad(path)?.IsWorkspaceRoot == true))
            .OrderBy(candidate => candidate.Depth)
            .ThenBy(candidate => candidate.IsWorkspace ? 0 : 1)
            .ThenBy(candidate => candidate.Path, StringComparer.Ordinal)
            .Select(candidate => candidate.Path)
            .FirstOrDefault();
    }

    private static int Depth(string root, string manifestPath)
        => Path.GetRelativePath(root, Path.GetDirectoryName(manifestPath))
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Count(segment => segment != ".");

    private static ExtractedSource ExtractCore(string archivePath, string destinationDirectory, SourceArchiveOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(archivePath))
        {
            throw new ArgumentException("An archive path is required.", nameof(archivePath));
        }

        options ??= new SourceArchiveOptions();
        var fullArchivePath = Path.GetFullPath(archivePath);
        var kind = new SourceArchiveExtractor().DetectKind(fullArchivePath);
        if (kind == ArchiveKind.Unknown)
        {
            throw new SourceArchiveException($"'{fullArchivePath}' is not a zip, tar or gzip-compressed tar archive.", fullArchivePath, null);
        }

        var chosenByUs = string.IsNullOrWhiteSpace(destinationDirectory);
        var destination = chosenByUs
            ? Path.Combine(DefaultExtractionRoot, Guid.NewGuid().ToString("N"))
            : Path.GetFullPath(destinationDirectory);
        var createdByUs = !Directory.Exists(destination);

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = kind == ArchiveKind.Zip ? ScanZip(fullArchivePath) : ScanTar(fullArchivePath, kind);
            ValidateEntries(entries, fullArchivePath, options);

            var root = options.StripSingleRootFolder
                ? FindSingleRootFolder(entries.Where(e => e.Type is EntryType.File or EntryType.Directory).Select(e => (e.Path, e.Type == EntryType.Directory)))
                : null;

            Directory.CreateDirectory(destination);
            var writer = new EntryWriter(fullArchivePath, destination, root, options, cancellationToken);
            if (kind == ArchiveKind.Zip)
            {
                ExtractZip(fullArchivePath, writer);
            }
            else
            {
                ExtractTar(fullArchivePath, kind, writer);
            }

            var manifest = FindProjectManifest(destination);
            return new ExtractedSource(
                fullArchivePath,
                kind,
                destination,
                manifest,
                root,
                writer.EntryCount,
                writer.TotalBytes,
                writer.Skipped,
                options.DeleteOnDispose ?? chosenByUs);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && ex is not SourceArchiveException && IsArchiveReadFailure(ex))
        {
            DeleteIfCreated(destination, createdByUs);
            throw new SourceArchiveException($"The archive '{fullArchivePath}' could not be extracted: {ex.Message}", fullArchivePath, ex);
        }
        catch
        {
            DeleteIfCreated(destination, createdByUs);
            throw;
        }
    }

    private static bool IsArchiveReadFailure(Exception ex)
        => ex is InvalidDataException or IOException or FormatException or UnauthorizedAccessException or NotSupportedException;

    private static void DeleteIfCreated(string destination, bool createdByUs)
    {
        if (!createdByUs)
        {
            return;
        }

        try
        {
            if (Directory.Exists(destination))
            {
                Directory.Delete(destination, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static void ValidateEntries(List<ScannedEntry> entries, string archivePath, SourceArchiveOptions options)
    {
        if (entries.Count > options.MaxEntryCount)
        {
            throw new SourceArchiveException($"The archive has {entries.Count} entries, more than the limit of {options.MaxEntryCount} (SourceArchiveOptions.MaxEntryCount).", archivePath, null);
        }

        var declared = entries.Where(e => e.Type == EntryType.File).Sum(e => Math.Max(0, e.Length));
        if (declared > options.MaxTotalBytes)
        {
            throw new SourceArchiveException($"The archive would extract {declared} bytes, more than the limit of {options.MaxTotalBytes} (SourceArchiveOptions.MaxTotalBytes).", archivePath, null);
        }
    }

    private static List<ScannedEntry> ScanZip(string archivePath)
    {
        var result = new List<ScannedEntry>();
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var path = NormalizeEntryName(entry.FullName, archivePath);
            result.Add(new ScannedEntry(path, ZipEntryType(entry), entry.Length));
        }

        return result;
    }

    private static List<ScannedEntry> ScanTar(string archivePath, ArchiveKind kind)
    {
        var result = new List<ScannedEntry>();
        using var stream = OpenTarStream(archivePath, kind);
        using var reader = new TarReader(stream);
        TarEntry entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            var type = TarEntryKind(entry.EntryType);
            if (type == EntryType.Metadata)
            {
                continue;
            }

            result.Add(new ScannedEntry(NormalizeEntryName(entry.Name, archivePath), type, entry.Length));
        }

        return result;
    }

    private static void ExtractZip(string archivePath, EntryWriter writer)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var type = ZipEntryType(entry);
            var mode = (entry.ExternalAttributes >> 16) & UnixPermissionMask;
            switch (type)
            {
                case EntryType.Directory:
                    writer.WriteDirectory(entry.FullName);
                    break;
                case EntryType.File:
                    using (var data = entry.Open())
                    {
                        writer.WriteFile(entry.FullName, data, mode);
                    }

                    break;
                default:
                    writer.Skip(entry.FullName, "symbolic link");
                    break;
            }
        }
    }

    private static void ExtractTar(string archivePath, ArchiveKind kind, EntryWriter writer)
    {
        using var stream = OpenTarStream(archivePath, kind);
        using var reader = new TarReader(stream);
        TarEntry entry;
        while ((entry = reader.GetNextEntry()) != null)
        {
            switch (TarEntryKind(entry.EntryType))
            {
                case EntryType.Metadata:
                    break;
                case EntryType.Directory:
                    writer.WriteDirectory(entry.Name);
                    break;
                case EntryType.File:
                    if (entry.DataStream == null)
                    {
                        writer.WriteFile(entry.Name, Stream.Null, (int)entry.Mode & UnixPermissionMask);
                    }
                    else
                    {
                        writer.WriteFile(entry.Name, entry.DataStream, (int)entry.Mode & UnixPermissionMask);
                    }

                    break;
                case EntryType.Link:
                    writer.Skip(entry.Name, (entry.EntryType == TarEntryType.HardLink ? "hard link to " : "symbolic link to ") + entry.LinkName);
                    break;
                default:
                    writer.Skip(entry.Name, "special file (" + entry.EntryType + ")");
                    break;
            }
        }
    }

    private static Stream OpenTarStream(string archivePath, ArchiveKind kind)
    {
        Stream file = File.OpenRead(archivePath);
        return kind == ArchiveKind.TarGzip ? new GZipStream(file, CompressionMode.Decompress, leaveOpen: false) : file;
    }

    private static EntryType ZipEntryType(ZipArchiveEntry entry)
    {
        var unixType = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        if (unixType == UnixSymbolicLink)
        {
            return EntryType.Link;
        }

        var name = entry.FullName;
        return name.EndsWith('/') || name.EndsWith('\\') ? EntryType.Directory : EntryType.File;
    }

    private static EntryType TarEntryKind(TarEntryType type) => type switch
    {
        TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile => EntryType.File,
        TarEntryType.Directory => EntryType.Directory,
        TarEntryType.SymbolicLink or TarEntryType.HardLink => EntryType.Link,
        TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes
            or TarEntryType.LongLink or TarEntryType.LongPath => EntryType.Metadata,
        _ => EntryType.Special,
    };

    private enum EntryType
    {
        File,
        Directory,
        Link,
        Special,
        Metadata,
    }

    private sealed record ScannedEntry(string Path, EntryType Type, long Length);

    /// <summary>
    /// Writes validated entries below the destination and enforces the limits on what is
    /// actually written.
    /// </summary>
    private sealed class EntryWriter
    {
        private readonly string _archivePath;
        private readonly string _destination;
        private readonly string _destinationPrefix;
        private readonly string _strippedRoot;
        private readonly SourceArchiveOptions _options;
        private readonly CancellationToken _cancellationToken;
        private readonly byte[] _buffer = new byte[81920];

        public EntryWriter(string archivePath, string destination, string strippedRoot, SourceArchiveOptions options, CancellationToken cancellationToken)
        {
            _archivePath = archivePath;
            _destination = Path.GetFullPath(destination);
            _destinationPrefix = Path.EndsInDirectorySeparator(_destination) ? _destination : _destination + Path.DirectorySeparatorChar;
            _strippedRoot = strippedRoot;
            _options = options;
            _cancellationToken = cancellationToken;
        }

        public int EntryCount { get; private set; }

        public long TotalBytes { get; private set; }

        public List<string> Skipped { get; } = new List<string>();

        public void Skip(string name, string reason)
        {
            NormalizeEntryName(name, _archivePath);
            Skipped.Add($"{name} ({reason}, not extracted)");
        }

        public void WriteDirectory(string name)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var target = Resolve(name);
            if (target == null)
            {
                return;
            }

            Directory.CreateDirectory(target);
            EntryCount++;
        }

        public void WriteFile(string name, Stream data, int mode)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var target = Resolve(name);
            if (target == null)
            {
                return;
            }

            if (File.Exists(target) && !_options.Overwrite)
            {
                throw new SourceArchiveException($"The file '{target}' already exists (archive entry '{name}'); set SourceArchiveOptions.Overwrite to replace existing files.", _archivePath, null);
            }

            if (Directory.Exists(target))
            {
                throw new SourceArchiveException($"The archive entry '{name}' is a file but '{target}' is a folder.", _archivePath, null);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target));
            using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                int read;
                while ((read = data.Read(_buffer, 0, _buffer.Length)) > 0)
                {
                    TotalBytes += read;
                    if (TotalBytes > _options.MaxTotalBytes)
                    {
                        throw new SourceArchiveException($"Extraction stopped: more than {_options.MaxTotalBytes} bytes (SourceArchiveOptions.MaxTotalBytes).", _archivePath, null);
                    }

                    output.Write(_buffer, 0, read);
                }
            }

            ApplyMode(target, mode);
            EntryCount++;
        }

        private string Resolve(string name)
        {
            var relative = NormalizeEntryName(name, _archivePath);
            if (_strippedRoot != null)
            {
                if (relative == _strippedRoot)
                {
                    return null;
                }

                relative = relative.Substring(_strippedRoot.Length + 1);
            }

            if (relative.Length == 0)
            {
                return null;
            }

            var target = Path.GetFullPath(Path.Combine(_destination, relative.Replace('/', Path.DirectorySeparatorChar)));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!target.StartsWith(_destinationPrefix, comparison))
            {
                throw new SourceArchiveException($"The archive entry '{name}' resolves outside the destination folder; refusing to extract it.", _archivePath, null);
            }

            return target;
        }

        private static void ApplyMode(string path, int mode)
        {
            if (OperatingSystem.IsWindows() || mode == 0)
            {
                return;
            }

            var unixMode = (UnixFileMode)mode | UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(path, unixMode);
        }
    }
}
