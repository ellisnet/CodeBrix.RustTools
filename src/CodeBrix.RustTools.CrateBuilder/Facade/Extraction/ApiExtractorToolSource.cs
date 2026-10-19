using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Extraction;

/// <summary>
/// The source of the codebrix-api-extractor tool, embedded in this assembly
/// (Facade/Extraction/Tool/), and its materialisation into a content-addressed scratch folder.
/// </summary>
internal static class ApiExtractorToolSource
{
    /// <summary>The prefix of the tool's manifest resource names.</summary>
    public const string ResourcePrefix = "ApiExtractorTool/";

    /// <summary>The tool's crate and executable name.</summary>
    public const string ToolName = "codebrix-api-extractor";

    /// <summary>The folder below the scratch root that holds one sub-folder per tool
    /// version.</summary>
    public const string ScratchFolderName = "api-extractor";

    /// <summary>The marker file written last; a folder without it is incomplete.</summary>
    public const string CompleteMarkerFileName = ".materialised";

    /// <summary>How many hex digits of the content hash name the folder (kept short for
    /// Windows path lengths below target/).</summary>
    public const int FolderHashLength = 16;

    private static readonly Lazy<IReadOnlyList<ToolSourceFile>> EmbeddedFiles = new Lazy<IReadOnlyList<ToolSourceFile>>(ReadEmbeddedFiles);

    /// <summary>The tool's files (relative path with '/', content), sorted by path.</summary>
    public static IReadOnlyList<ToolSourceFile> Files => EmbeddedFiles.Value;

    /// <summary>The SHA-256 (64 lowercase hex digits) of <see cref="Files"/>.</summary>
    public static string ContentHash => ComputeHash(Files);

    /// <summary>The SHA-256 over files: for each, ordered by path, the UTF-8 path, a 0 byte,
    /// the content length (8 bytes, little endian) and the content.</summary>
    public static string ComputeHash(IEnumerable<ToolSourceFile> files)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(f => f.RelativePath, StringComparer.Ordinal))
        {
            sha.AppendData(Encoding.UTF8.GetBytes(file.RelativePath));
            sha.AppendData(new byte[] { 0 });
            sha.AppendData(BitConverter.GetBytes((long)file.Content.Length));
            sha.AppendData(file.Content);
        }

        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>The folder the tool is materialised into below
    /// <paramref name="scratchRootDirectory"/>.</summary>
    public static string GetToolDirectory(string scratchRootDirectory)
        => Path.Combine(scratchRootDirectory, ScratchFolderName, ContentHash.Substring(0, FolderHashLength));

    /// <summary>
    /// Writes the tool's files to <c>&lt;scratch&gt;/api-extractor/&lt;hash&gt;/</c> unless a
    /// complete copy is already there, and returns that folder. Safe to call concurrently: the
    /// files are written to a temporary sibling folder that is then moved into place.
    /// </summary>
    public static string Materialise(string scratchRootDirectory)
    {
        if (string.IsNullOrWhiteSpace(scratchRootDirectory))
        {
            throw new ArgumentException("A scratch folder is required.", nameof(scratchRootDirectory));
        }

        var files = Files;
        if (files.Count == 0)
        {
            throw new InvalidOperationException("The API extractor source is missing from the CodeBrix.RustTools.CrateBuilder assembly.");
        }

        var target = GetToolDirectory(scratchRootDirectory);
        if (IsComplete(target))
        {
            return target;
        }

        var parent = Path.GetDirectoryName(target);
        Directory.CreateDirectory(parent);
        var temporary = target + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        try
        {
            foreach (var file in files)
            {
                var path = Path.Combine(temporary, file.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, file.Content);
            }

            File.WriteAllText(Path.Combine(temporary, CompleteMarkerFileName), ContentHash + "\n");
            if (Directory.Exists(target))
            {
                // An incomplete earlier attempt (no marker) is replaced.
                if (IsComplete(target))
                {
                    return target;
                }

                Directory.Delete(target, recursive: true);
            }

            Directory.Move(temporary, target);
        }
        catch (IOException) when (IsComplete(target))
        {
            // Another caller won the race.
        }
        finally
        {
            if (Directory.Exists(temporary))
            {
                try
                {
                    Directory.Delete(temporary, recursive: true);
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
        }

        return target;
    }

    /// <summary>Whether <paramref name="directory"/> holds a complete copy of this tool
    /// version.</summary>
    public static bool IsComplete(string directory)
    {
        var marker = Path.Combine(directory, CompleteMarkerFileName);
        return File.Exists(marker) && File.ReadAllText(marker).Trim() == ContentHash;
    }

    private static IReadOnlyList<ToolSourceFile> ReadEmbeddedFiles()
    {
        var assembly = typeof(ApiExtractorToolSource).Assembly;
        var result = new List<ToolSourceFile>();
        foreach (var name in assembly.GetManifestResourceNames())
        {
            var normalized = name.Replace('\\', '/');
            if (!normalized.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(name);
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            result.Add(new ToolSourceFile(normalized.Substring(ResourcePrefix.Length), buffer.ToArray()));
        }

        return result.OrderBy(f => f.RelativePath, StringComparer.Ordinal).ToList();
    }
}
