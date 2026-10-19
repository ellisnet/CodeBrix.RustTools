using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Rendering;

/// <summary>
/// generated-files.json in the wrapper folder: every file the writer wrote, relative and with
/// <c>/</c>. A later write removes the listed files it no longer produces - and nothing else.
/// </summary>
internal sealed class GeneratedFilesManifest
{
    /// <summary>The manifest's file name.</summary>
    public const string FileName = "generated-files.json";

    /// <summary>The value of <see cref="Generator"/>.</summary>
    public const string GeneratorName = "CodeBrix.RustTools.CrateBuilder wrapper writer";

    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Who wrote the folder.</summary>
    public string Generator { get; set; } = GeneratorName;

    /// <summary>The generated files, ordinal-sorted.</summary>
    public List<string> Files { get; set; } = new List<string>();

    /// <summary>Reads the manifest of <paramref name="directory"/>, or <c>null</c> when there
    /// is none (or it is not one of ours).</summary>
    public static GeneratedFilesManifest Load(string directory)
    {
        var path = Path.Combine(directory, FileName);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var manifest = JsonSerializer.Deserialize<GeneratedFilesManifest>(File.ReadAllText(path), Options);
            return manifest != null && manifest.Generator == GeneratorName ? manifest : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The manifest as UTF-8 JSON with <c>\n</c> line endings and a final newline.</summary>
    public byte[] ToBytes()
        => new UTF8Encoding(false).GetBytes(JsonSerializer.Serialize(this, Options).Replace("\r\n", "\n") + "\n");

    /// <summary>Creates the manifest for a file set.</summary>
    public static GeneratedFilesManifest For(IEnumerable<string> relativePaths)
        => new GeneratedFilesManifest { Files = relativePaths.Where(p => p != FileName).OrderBy(p => p, StringComparer.Ordinal).ToList() };
}
