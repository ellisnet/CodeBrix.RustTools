using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// The file-system rules of staging, shared by the source resolver and the workspace stager:
/// what a staged copy leaves out, path comparison, relative paths with forward slashes, and
/// folder deletion that also removes read-only files (Git object files are read-only on
/// Windows).
/// </summary>
internal static class StagingFileSystem
{
    /// <summary>The name of cargo's build output folder.</summary>
    public const string TargetFolderName = "target";

    /// <summary>The name of Git's folder (or, in a worktree or submodule, file).</summary>
    public const string GitFolderName = ".git";

    /// <summary>The file cargo writes into every target folder it creates.</summary>
    public const string CacheDirectoryTagFileName = "CACHEDIR.TAG";

    /// <summary>Path comparison of this operating system (case-insensitive on Windows and
    /// macOS, as cargo's own file systems are by default).</summary>
    public static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static StringComparison PathComparison => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <summary>Whether two full paths name the same folder or file (trailing separators
    /// ignored).</summary>
    public static bool PathEquals(string a, string b)
        => PathComparer.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)));

    /// <summary>Whether <paramref name="path"/> is <paramref name="directory"/> itself or lies
    /// beneath it.</summary>
    public static bool IsSameOrInside(string path, string directory)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return string.Equals(full, root, PathComparison)
            || full.StartsWith(root + Path.DirectorySeparatorChar, PathComparison)
            || (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar && full.StartsWith(root + Path.AltDirectorySeparatorChar, PathComparison));
    }

    /// <summary>The path of <paramref name="path"/> relative to <paramref name="baseDirectory"/>,
    /// with forward slashes ("." for the folder itself).</summary>
    public static string RelativePath(string baseDirectory, string path)
        => Path.GetRelativePath(baseDirectory, path).Replace('\\', '/');

    /// <summary>The name prefix of CrateBuilder's own folders (<c>.codebrix-vendor</c>,
    /// <c>.codebrix-facade</c>, <c>.codebrix-wgpu</c>, ...); such a folder is never copied into a
    /// staged tree, at any depth.</summary>
    public const string CrateBuilderFolderPrefix = ".codebrix-";

    /// <summary>cargo's configuration folder (staged like every hidden folder that is not in
    /// <see cref="ExcludedRootHiddenFolders"/>).</summary>
    public const string CargoConfigFolderName = ".cargo";

    /// <summary>The hidden folders directly below a copied root that are left out of a staged
    /// copy: version control, CI, editor and tool-environment folders that no cargo build
    /// reads (case-insensitive).</summary>
    public static readonly IReadOnlySet<string> ExcludedRootHiddenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".github", ".gitlab", ".hg", ".svn", ".jj", ".vscode", ".idea", ".vs", ".fleet", ".zed",
        ".devcontainer", ".circleci", ".buildkite", ".husky", ".claude", ".cursor", ".direnv", ".venv",
        ".tox", ".mypy_cache", ".pytest_cache", ".ruff_cache",
    };

    /// <summary>
    /// Whether a sub-folder is left out of a staged copy: <c>.git</c> anywhere; a CrateBuilder
    /// folder (<see cref="CrateBuilderFolderPrefix"/>) anywhere; a hidden folder (name starting
    /// with '.') directly below the copy's root that belongs to version control, CI, an editor
    /// or a tool environment (<see cref="ExcludedRootHiddenFolders"/>: <c>.github</c>,
    /// <c>.vscode</c>, <c>.idea</c> ...) - every OTHER hidden folder is copied, because builds
    /// read some of them (<c>.cargo</c> configuration, the <c>.sqlx</c> offline query data,
    /// assets in a hidden folder); and a <c>target</c> folder that is cargo's output - it sits beside a Cargo.toml or
    /// holds cargo's CACHEDIR.TAG - so that a source module folder named <c>target</c> is still
    /// copied.
    /// </summary>
    /// <param name="directory">The full path of the sub-folder.</param>
    /// <param name="depth">1 for a folder directly below the copy's root, 2 below that, and
    /// so on.</param>
    public static bool IsExcludedDirectory(string directory, int depth)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.Equals(name, GitFolderName, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((depth == 1 && name.StartsWith('.') && ExcludedRootHiddenFolders.Contains(name)) || name.StartsWith(CrateBuilderFolderPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(name, TargetFolderName, StringComparison.Ordinal))
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(directory));
            return File.Exists(Path.Combine(parent, "Cargo.toml")) || File.Exists(Path.Combine(directory, CacheDirectoryTagFileName));
        }

        return false;
    }

    /// <summary>Whether a file is left out of a staged copy: only a <c>.git</c> file (the
    /// link of a worktree or submodule).</summary>
    public static bool IsExcludedFile(string file)
        => string.Equals(Path.GetFileName(file), GitFolderName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Copies <paramref name="source"/> to <paramref name="destination"/> (created), leaving out
    /// what <see cref="IsExcludedDirectory"/> and <see cref="IsExcludedFile"/> name. Symbolic
    /// links are followed: a linked file is copied as a regular file, a linked folder is copied
    /// as a folder unless it leads back to a folder already being copied (a loop), in which
    /// case it is skipped; a broken link is skipped. The folders in
    /// <paramref name="excludedDirectories"/> (CrateBuilder's scratch root and run folder, when a
    /// consumer placed them inside the crate) and the destination itself are never copied, so a
    /// copy cannot copy its own output.
    /// </summary>
    /// <returns>The number of files copied.</returns>
    public static int CopyTree(string source, string destination, CancellationToken cancellationToken, IEnumerable<string> excludedDirectories = null)
    {
        var active = new HashSet<string>(PathComparer);
        var excluded = new HashSet<string>(PathComparer) { Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) };
        foreach (var directory in excludedDirectories ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(directory))
            {
                excluded.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)));
            }
        }

        return CopyTree(Path.GetFullPath(source), Path.GetFullPath(destination), 0, active, excluded, cancellationToken);
    }

    /// <summary>
    /// Deletes a folder and everything in it, clearing read-only attributes first; nothing
    /// happens when it does not exist.
    /// </summary>
    public static void DeleteDirectory(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0, IgnoreInaccessible = true }))
        {
            try
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (IOException)
            {
                // The delete below reports what really cannot be removed.
            }
        }

        Directory.Delete(directory, recursive: true);
    }

    /// <summary>Deletes everything inside a folder but keeps the folder itself.</summary>
    public static void DeleteContents(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var child in Directory.GetDirectories(directory))
        {
            DeleteDirectory(child);
        }

        foreach (var file in Directory.GetFiles(directory))
        {
            File.SetAttributes(file, FileAttributes.Normal);
            File.Delete(file);
        }
    }

    /// <summary>Whether a folder exists and holds no files or folders at all.</summary>
    public static bool IsEmptyDirectory(string directory)
        => Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any();

    /// <summary>Replaces every character that is not a letter, digit, '-', '_' or '.' with
    /// '_'; <paramref name="fallback"/> when nothing is left.</summary>
    public static string SanitizeFolderName(string name, string fallback)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return fallback;
        }

        var characters = name.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_' || c == '.' ? c : '_').ToArray();
        var result = new string(characters).Trim('.');
        return result.Length == 0 ? fallback : result;
    }

    private static int CopyTree(string source, string destination, int depth, HashSet<string> active, HashSet<string> excluded, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var real = RealPath(source);
        if (real == null || !active.Add(real))
        {
            return 0;
        }

        try
        {
            Directory.CreateDirectory(destination);
            var count = 0;
            foreach (var file in Directory.GetFiles(source).OrderBy(f => f, StringComparer.Ordinal))
            {
                if (IsExcludedFile(file))
                {
                    continue;
                }

                if (new FileInfo(file).LinkTarget != null && !File.Exists(file))
                {
                    continue;
                }

                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
                count++;
            }

            foreach (var directory in Directory.GetDirectories(source).OrderBy(d => d, StringComparer.Ordinal))
            {
                if (IsExcludedDirectory(directory, depth + 1) || excluded.Contains(Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory))))
                {
                    continue;
                }

                count += CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)), depth + 1, active, excluded, cancellationToken);
            }

            return count;
        }
        finally
        {
            active.Remove(real);
        }
    }

    private static string RealPath(string directory)
    {
        var info = new DirectoryInfo(directory);
        if (info.LinkTarget == null)
        {
            return info.FullName;
        }

        var target = info.ResolveLinkTarget(returnFinalTarget: true);
        return target != null && target.Exists ? target.FullName : null;
    }
}
