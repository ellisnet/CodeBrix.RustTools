using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// A project's toolchain override file: <c>rust-toolchain.toml</c>, or the legacy
/// <c>rust-toolchain</c> file (either TOML or a single channel name). rustup uses it to pick the
/// toolchain for every cargo/rustc command run in that folder tree.
/// </summary>
public sealed class RustToolchainFile
{
    /// <summary>The modern file name, <c>rust-toolchain.toml</c>.</summary>
    public const string FileName = "rust-toolchain.toml";

    /// <summary>The legacy file name, <c>rust-toolchain</c> (no extension).</summary>
    public const string LegacyFileName = "rust-toolchain";

    /// <summary>The full path of the file, or <c>null</c> when parsed from text.</summary>
    public string FilePath { get; init; }

    /// <summary>The requested channel (<c>stable</c>, <c>nightly-2026-01-01</c>,
    /// <c>1.94.1</c>, ...), or <c>null</c> when the file names a custom <see cref="ToolchainPath"/>
    /// instead.</summary>
    public string Channel { get; init; }

    /// <summary>The components the file asks for (<c>clippy</c>, <c>rustfmt</c>, ...).</summary>
    public IReadOnlyList<string> Components { get; init; } = Array.Empty<string>();

    /// <summary>The extra compilation targets the file asks for (target triples).</summary>
    public IReadOnlyList<string> Targets { get; init; } = Array.Empty<string>();

    /// <summary>The rustup profile (<c>minimal</c>, <c>default</c>, <c>complete</c>), or
    /// <c>null</c>.</summary>
    public string Profile { get; init; }

    /// <summary>The path of a custom toolchain (the <c>path</c> key), or <c>null</c>.</summary>
    public string ToolchainPath { get; init; }

    /// <summary>True when the file was the legacy one-line format containing only a channel.</summary>
    public bool IsLegacyFormat { get; init; }

    /// <summary>The release channel implied by <see cref="Channel"/>.</summary>
    public RustChannel ChannelKind => ChannelOf(Channel);

    /// <summary>
    /// Parses the text of a toolchain file. TOML text must contain a <c>[toolchain]</c> table;
    /// any other non-empty text is taken as a legacy single-line channel name.
    /// </summary>
    /// <param name="text">The file contents.</param>
    /// <param name="filePath">The file's path, recorded in <see cref="FilePath"/> and used in
    /// error messages; may be <c>null</c>.</param>
    /// <exception cref="RustToolsException">The text is empty or is invalid TOML.</exception>
    public static RustToolchainFile Parse(string text, string filePath = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new RustToolsException($"The toolchain file {filePath ?? "text"} is empty.");
        }

        var trimmed = text.Trim();
        var looksLikeToml = trimmed.Contains('[') || trimmed.Contains('=');
        if (!looksLikeToml)
        {
            var firstLine = trimmed.Split('\n')[0].Trim();
            return new RustToolchainFile { FilePath = filePath, Channel = firstLine, IsLegacyFormat = true };
        }

        var document = TomlHelpers.Parse(text, filePath);
        var toolchain = TomlHelpers.GetTable(document, "toolchain");
        if (toolchain == null)
        {
            throw new RustToolsException($"The toolchain file {filePath ?? "text"} has no [toolchain] table.");
        }

        return new RustToolchainFile
        {
            FilePath = filePath,
            Channel = TomlHelpers.GetString(toolchain, "channel"),
            Components = TomlHelpers.GetStringList(toolchain, "components"),
            Targets = TomlHelpers.GetStringList(toolchain, "targets"),
            Profile = TomlHelpers.GetString(toolchain, "profile"),
            ToolchainPath = TomlHelpers.GetString(toolchain, "path"),
        };
    }

    /// <summary>
    /// Reads and parses a toolchain file from disk.
    /// </summary>
    /// <param name="filePath">The path of a <c>rust-toolchain.toml</c> or <c>rust-toolchain</c>
    /// file.</param>
    /// <exception cref="RustToolsException">The file cannot be read or parsed.</exception>
    public static RustToolchainFile Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        string text;
        try
        {
            text = File.ReadAllText(filePath);
        }
        catch (IOException ex)
        {
            throw new RustToolsException($"The toolchain file {filePath} could not be read: {ex.Message}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new RustToolsException($"The toolchain file {filePath} could not be read: {ex.Message}", ex);
        }

        return Parse(text, Path.GetFullPath(filePath));
    }

    /// <summary>
    /// Finds the toolchain file that applies to <paramref name="directory"/> the way rustup
    /// does: the folder itself and then each parent, preferring <c>rust-toolchain.toml</c> over
    /// <c>rust-toolchain</c> in the same folder.
    /// </summary>
    /// <param name="directory">The project folder to start from.</param>
    /// <returns>The full path of the file, or <c>null</c> when none applies.</returns>
    public static string Find(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        for (var current = new DirectoryInfo(Path.GetFullPath(directory)); current != null; current = current.Parent)
        {
            foreach (var name in new[] { FileName, LegacyFileName })
            {
                var candidate = Path.Combine(current.FullName, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds and loads the toolchain file that applies to <paramref name="directory"/>.
    /// </summary>
    /// <param name="directory">The project folder to start from.</param>
    /// <returns>The parsed file, or <c>null</c> when none applies.</returns>
    public static RustToolchainFile FindAndLoad(string directory)
    {
        var path = Find(directory);
        return path == null ? null : Load(path);
    }

    /// <summary>
    /// Classifies a channel or toolchain name such as <c>stable</c>,
    /// <c>nightly-2026-01-01-x86_64-unknown-linux-gnu</c> or <c>1.94.1</c>.
    /// </summary>
    /// <param name="channelOrToolchainName">The name to classify; <c>null</c> gives
    /// <see cref="RustChannel.Unknown"/>.</param>
    public static RustChannel ChannelOf(string channelOrToolchainName)
    {
        if (string.IsNullOrWhiteSpace(channelOrToolchainName))
        {
            return RustChannel.Unknown;
        }

        var name = channelOrToolchainName.Trim();
        if (name.StartsWith("stable", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Stable;
        }

        if (name.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Beta;
        }

        if (name.StartsWith("nightly", StringComparison.OrdinalIgnoreCase))
        {
            return RustChannel.Nightly;
        }

        if (char.IsDigit(name[0]))
        {
            return name.Contains("beta", StringComparison.OrdinalIgnoreCase) ? RustChannel.Beta : RustChannel.Stable;
        }

        return RustChannel.Unknown;
    }
}
