using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The JSON record written beside the artifacts of every build that got past the readiness
/// check: what was built from which source, how, with which toolchain, and what came out. It
/// is indented JSON with camelCase names, enums as strings and null values left out.
/// </summary>
public sealed class BuildRecord
{
    /// <summary>The file name the record is written under in the output folder.</summary>
    public const string DefaultFileName = "crate-build-record.json";

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>The version of CodeBrix.RustTools.CrateBuilder that wrote the record.</summary>
    public string CrateBuilderVersion { get; set; }

    /// <summary>When the build started (UTC).</summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>When the build finished (UTC).</summary>
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>Which source was built; <c>null</c> when it could not be resolved.</summary>
    public CrateSourceIdentity Source { get; set; }

    /// <summary>The build mode.</summary>
    public BuildMode Mode { get; set; }

    /// <summary>The cargo profile.</summary>
    public CrateBuildProfile Profile { get; set; }

    /// <summary>The cargo features that were enabled.</summary>
    public IList<string> Features { get; set; } = new List<string>();

    /// <summary>Whether <c>--no-default-features</c> was passed.</summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>Whether <c>--offline</c> was passed.</summary>
    public bool Offline { get; set; }

    /// <summary>Whether <c>--locked</c> was passed.</summary>
    public bool Locked { get; set; }

    /// <summary>The toolchain: in host mode the rustc and cargo versions and the host triple;
    /// in Docker mode the engine and, when known, the build image reference and id.</summary>
    public string ToolchainDescription { get; set; }

    /// <summary>One entry per requested target, in request order.</summary>
    public IList<TargetRecord> Targets { get; set; } = new List<TargetRecord>();

    /// <summary>The CodeBrix.WebGpu.Native version the crate was linked against; <c>null</c>
    /// when it was not linked.</summary>
    public string WebGpuNativeVersion { get; set; }

    /// <summary>The C interface of the built library (generated facade or the crate's own C
    /// exports); <c>null</c> when the facade stages did not run.</summary>
    public FacadeRecord Facade { get; set; }

    /// <summary>The file name of the wrap report written beside the record
    /// (<c>WRAP-REPORT.txt</c>); <c>null</c> when none was written.</summary>
    public string WrapReportFile { get; set; }

    /// <summary>The wrapper project; <c>null</c> when none was written.</summary>
    public WrapperRecord Wrapper { get; set; }

    /// <summary>The build's warnings, and the failure when a stage failed.</summary>
    public IList<string> Warnings { get; set; } = new List<string>();

    /// <summary>
    /// Reads a record written by <see cref="Save"/>.
    /// </summary>
    /// <param name="path">The record file.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="CrateBuilderException">The file is not a valid build record.</exception>
    public static BuildRecord Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A record path is required.", nameof(path));
        }

        var text = File.ReadAllText(path, Encoding.UTF8);
        try
        {
            return JsonSerializer.Deserialize<BuildRecord>(text, JsonOptions)
                ?? throw new CrateBuilderException($"The build record '{path}' is empty.", CrateBuildStage.Recording);
        }
        catch (JsonException ex)
        {
            throw new CrateBuilderException($"The build record '{path}' is not valid JSON: {ex.Message}", CrateBuildStage.Recording, ex);
        }
    }

    /// <summary>
    /// Writes the record as indented UTF-8 JSON (no byte-order mark, Unix line endings, a
    /// final newline), creating the folder when needed and replacing an existing file.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public void Save(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A record path is required.", nameof(path));
        }

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, ToJson(), new UTF8Encoding(false));
    }

    /// <summary>The JSON text <see cref="Save"/> writes.</summary>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NewLine = "\n",
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
