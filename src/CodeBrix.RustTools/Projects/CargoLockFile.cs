using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Internal;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// A parsed Cargo.lock file: the exact versions and sources cargo resolved.
/// </summary>
public sealed class CargoLockFile
{
    /// <summary>The file name cargo writes, <c>Cargo.lock</c>.</summary>
    public const string FileName = "Cargo.lock";

    /// <summary>The full path of the file, or <c>null</c> when parsed from text.</summary>
    public string FilePath { get; init; }

    /// <summary>The lock-file format <c>version</c> (3, 4, ...), or <c>null</c> for the oldest
    /// format.</summary>
    public int? FormatVersion { get; init; }

    /// <summary>Every locked package, in file order.</summary>
    public IReadOnlyList<CargoLockPackage> Packages { get; init; } = Array.Empty<CargoLockPackage>();

    /// <summary>
    /// Returns every locked version of the named package (more than one when the graph holds
    /// several semver-incompatible versions).
    /// </summary>
    /// <param name="name">The package name.</param>
    public IReadOnlyList<CargoLockPackage> Find(string name)
        => Packages.Where(p => string.Equals(p.Name, name, StringComparison.Ordinal)).ToList();

    /// <summary>
    /// Reads and parses a Cargo.lock file.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="RustToolsException">The file is not valid TOML.</exception>
    public static CargoLockFile Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The Cargo.lock file was not found.", fullPath);
        }

        return Parse(File.ReadAllText(fullPath), fullPath);
    }

    /// <summary>
    /// Parses the text of a Cargo.lock file.
    /// </summary>
    /// <param name="text">The TOML text.</param>
    /// <param name="filePath">The file's path, recorded in <see cref="FilePath"/>; may be
    /// <c>null</c>.</param>
    /// <exception cref="RustToolsException">The text is not valid TOML.</exception>
    public static CargoLockFile Parse(string text, string filePath = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        var document = TomlHelpers.Parse(text, filePath ?? FileName);
        var packages = new List<CargoLockPackage>();
        if (TomlHelpers.GetTableArray(document, "package") is { } array)
        {
            foreach (var table in array)
            {
                packages.Add(new CargoLockPackage
                {
                    Name = TomlHelpers.GetString(table, "name") ?? string.Empty,
                    Version = TomlHelpers.GetString(table, "version") ?? string.Empty,
                    Source = TomlHelpers.GetString(table, "source"),
                    Checksum = TomlHelpers.GetString(table, "checksum"),
                    Dependencies = TomlHelpers.GetStringList(table, "dependencies"),
                });
            }
        }

        var version = TomlHelpers.GetInteger(document, "version");
        return new CargoLockFile
        {
            FilePath = filePath == null ? null : Path.GetFullPath(filePath),
            FormatVersion = version.HasValue ? (int)version.Value : null,
            Packages = packages,
        };
    }
}
