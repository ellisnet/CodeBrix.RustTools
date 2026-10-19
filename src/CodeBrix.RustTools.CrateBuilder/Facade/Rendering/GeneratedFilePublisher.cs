using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// Publishes a set of generated files into a folder: everything is written to a fresh temporary
/// sibling folder first; a missing target folder is then created by one directory move, an
/// existing one gets only the files whose bytes changed (each by an atomic file move), and the
/// files the PREVIOUS generation listed in <c>generated-files.json</c> but this one does not are
/// removed. Nothing else in the folder is touched (cargo's target/ and its lock-file edits stay).
/// </summary>
internal static class GeneratedFilePublisher
{
    /// <summary>The generator name written into the manifest.</summary>
    public const string GeneratorName = "CodeBrix.RustTools.CrateBuilder FacadeRenderer";

    /// <summary>What a publish did.</summary>
    internal sealed class Outcome
    {
        /// <summary>New or changed files (relative paths, ordinal order).</summary>
        public List<string> Changed { get; } = new List<string>();

        /// <summary>Files removed because the previous generation listed them (relative paths).</summary>
        public List<string> Removed { get; } = new List<string>();
    }

    /// <summary>Publishes <paramref name="files"/> (relative '/' paths -> bytes) into
    /// <paramref name="targetDirectory"/> together with the manifest.</summary>
    public static Outcome Publish(string targetDirectory, IReadOnlyDictionary<string, byte[]> files)
    {
        var outcome = new Outcome();
        var all = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in files)
        {
            CheckRelative(pair.Key);
            all[pair.Key] = pair.Value;
        }

        all[FacadeConventions.GeneratedFilesManifestName] = Manifest(files);
        var parent = Path.GetDirectoryName(Path.GetFullPath(targetDirectory));
        Directory.CreateDirectory(parent);
        var temp = Path.Combine(parent, ".render-" + Guid.NewGuid().ToString("N"));
        try
        {
            foreach (var pair in all)
            {
                var path = Path.Combine(temp, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, pair.Value);
            }

            if (!Directory.Exists(targetDirectory))
            {
                Directory.Move(temp, targetDirectory);
                outcome.Changed.AddRange(all.Keys);
                return outcome;
            }

            var previous = PreviousFiles(targetDirectory);
            foreach (var pair in all)
            {
                var destination = Path.Combine(targetDirectory, pair.Key);
                if (File.Exists(destination) && SameBytes(destination, pair.Value))
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Move(Path.Combine(temp, pair.Key), destination, overwrite: true);
                outcome.Changed.Add(pair.Key);
            }

            foreach (var stale in previous.Where(p => !all.ContainsKey(p)).OrderBy(p => p, StringComparer.Ordinal))
            {
                var path = Path.Combine(targetDirectory, stale);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    outcome.Removed.Add(stale);
                    RemoveEmptyParents(Path.GetDirectoryName(path), targetDirectory);
                }
            }

            return outcome;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new FacadeGenerationException($"The facade files could not be written to {targetDirectory}: {e.Message}", e);
        }
        finally
        {
            if (Directory.Exists(temp))
            {
                try
                {
                    Directory.Delete(temp, recursive: true);
                }
                catch (IOException)
                {
                    // Best effort: a leftover .render-* folder holds only a copy of the output.
                }
                catch (UnauthorizedAccessException)
                {
                    // Best effort.
                }
            }
        }
    }

    /// <summary>The manifest bytes for <paramref name="files"/> (path + SHA-256, ordinal order,
    /// indented, "\n", UTF-8 without BOM).</summary>
    public static byte[] Manifest(IReadOnlyDictionary<string, byte[]> files)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteString("generator", GeneratorName);
            writer.WriteStartArray("files");
            foreach (var pair in files.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WriteStartObject();
                writer.WriteString("path", pair.Key);
                writer.WriteString("sha256", Convert.ToHexStringLower(SHA256.HashData(pair.Value)));
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    /// <summary>The relative paths the manifest in <paramref name="directory"/> lists (empty when
    /// there is none or it cannot be read; unsafe paths are ignored).</summary>
    public static IReadOnlyList<string> PreviousFiles(string directory)
    {
        var path = Path.Combine(directory, FacadeConventions.GeneratedFilesManifestName);
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (!document.RootElement.TryGetProperty("files", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            return list.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.Object && e.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null)
                .Where(IsSafeRelative)
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool IsSafeRelative(string path)
        => !string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path) && !path.Contains('\\')
            && path.Split('/').All(s => s.Length > 0 && s != "." && s != "..");

    private static void CheckRelative(string path)
    {
        if (!IsSafeRelative(path))
        {
            throw new FacadeGenerationException($"'{path}' is not a relative path inside the facade folder.");
        }
    }

    private static bool SameBytes(string path, byte[] bytes)
    {
        var info = new FileInfo(path);
        return info.Length == bytes.Length && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes);
    }

    private static void RemoveEmptyParents(string directory, string root)
    {
        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        for (var d = Path.GetFullPath(directory); d.Length > full.Length && d.StartsWith(full, StringComparison.Ordinal); d = Path.GetDirectoryName(d))
        {
            if (Directory.EnumerateFileSystemEntries(d).Any())
            {
                return;
            }

            Directory.Delete(d);
        }
    }

    /// <summary>UTF-8 (no BOM) bytes of generated text.</summary>
    public static byte[] Utf8(string text) => new UTF8Encoding(false).GetBytes(text);
}
