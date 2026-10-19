using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Finds llvm-mingw (the clang / lld / mingw-w64 toolchain that links Rust's
/// <c>*-pc-windows-gnullvm</c> targets, see <see cref="WindowsGnullvmLinkProfile"/>) on this
/// machine: an explicit folder, else the folder named by <c>LLVM_MINGW_HOME</c>, else every PATH
/// folder. A folder qualifies when it (or its <c>bin</c> folder) holds the target's clang
/// wrapper, <c>&lt;machine&gt;-w64-mingw32-clang</c>. Never installs anything.
/// </summary>
public static class LlvmMingwLocator
{
    /// <summary>The variable that names llvm-mingw's installation folder (the one holding
    /// <c>bin</c>); the CodeBrix.RustTools.Docker images set the same name.</summary>
    public const string HomeVariable = "LLVM_MINGW_HOME";

    /// <summary>The page that lists llvm-mingw's releases.</summary>
    public const string ReleasesUrl = "https://github.com/mstorsjo/llvm-mingw/releases";

    /// <summary>
    /// Finds llvm-mingw's <c>bin</c> folder for a Windows gnullvm target with this process's
    /// environment and file system.
    /// </summary>
    /// <param name="triple">One of <see cref="WindowsGnullvmLinkProfile.Triples"/>.</param>
    /// <param name="explicitDirectory">llvm-mingw's folder or its <c>bin</c> folder to use
    /// instead of searching; when it does not hold the clang wrapper the result is <c>null</c>
    /// (no fallback).</param>
    /// <returns>The <c>bin</c> folder, or <c>null</c> when none was found.</returns>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Windows gnullvm
    /// target.</exception>
    public static string FindBinDirectory(string triple, string explicitDirectory = null)
        => FindBinDirectory(triple, explicitDirectory, Environment.GetEnvironmentVariable, File.Exists, OperatingSystem.IsWindows());

    /// <summary>
    /// Finds llvm-mingw's <c>bin</c> folder with a given environment and file system (for tests
    /// and for describing other machines). Search order: <paramref name="explicitDirectory"/>'s
    /// <c>bin</c> folder, then the folder itself (no fallback when it is set); otherwise
    /// <c>LLVM_MINGW_HOME</c>/<c>bin</c>, then each PATH folder.
    /// </summary>
    /// <param name="triple">One of <see cref="WindowsGnullvmLinkProfile.Triples"/>.</param>
    /// <param name="explicitDirectory">See <see cref="FindBinDirectory(string, string)"/>.</param>
    /// <param name="getEnvironmentVariable">Reads a variable; <c>null</c> reads the process
    /// environment.</param>
    /// <param name="fileExists">Tells whether a file exists; <c>null</c> uses
    /// <see cref="File.Exists"/>.</param>
    /// <param name="windowsFileNames">Whether the tools end in <c>.exe</c> (a Windows
    /// host).</param>
    /// <returns>The <c>bin</c> folder, or <c>null</c> when none was found.</returns>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Windows gnullvm
    /// target.</exception>
    public static string FindBinDirectory(string triple, string explicitDirectory, Func<string, string> getEnvironmentVariable, Func<string, bool> fileExists, bool windowsFileNames)
    {
        WindowsGnullvmLinkProfile.MachineOf(triple);
        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        fileExists ??= File.Exists;
        foreach (var candidate in Candidates(explicitDirectory, getEnvironmentVariable))
        {
            var profile = new WindowsGnullvmLinkProfile(candidate, windowsFileNames);
            if (fileExists(profile.ClangPath(triple)))
            {
                return profile.BinDirectory;
            }
        }

        return null;
    }

    /// <summary>
    /// The link profile on the llvm-mingw found for a target (see
    /// <see cref="FindBinDirectory(string, string)"/>), or <c>null</c> when none was found.
    /// </summary>
    /// <param name="triple">One of <see cref="WindowsGnullvmLinkProfile.Triples"/>.</param>
    /// <param name="explicitDirectory">llvm-mingw's folder or its <c>bin</c> folder, or
    /// <c>null</c> to search.</param>
    /// <exception cref="ArgumentException"><paramref name="triple"/> is not a Windows gnullvm
    /// target.</exception>
    public static WindowsGnullvmLinkProfile FindProfile(string triple, string explicitDirectory = null)
    {
        var bin = FindBinDirectory(triple, explicitDirectory);
        return bin == null ? null : new WindowsGnullvmLinkProfile(bin, OperatingSystem.IsWindows());
    }

    private static IEnumerable<string> Candidates(string explicitDirectory, Func<string, string> getEnvironmentVariable)
    {
        if (!string.IsNullOrWhiteSpace(explicitDirectory))
        {
            yield return BinFolderOf(explicitDirectory.Trim());
            yield return explicitDirectory.Trim();
            yield break;
        }

        var home = getEnvironmentVariable(HomeVariable);
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return BinFolderOf(home.Trim());
        }

        var path = getEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(path))
        {
            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return folder;
            }
        }
    }

    // Joins with the folder's own separator, as the link profiles join their tool paths, so a
    // folder of another machine (/opt/llvm-mingw described on a Windows host) keeps its style.
    private static string BinFolderOf(string directory)
    {
        var separator = directory.Contains('\\') && !directory.Contains('/') ? "\\" : "/";
        return directory.TrimEnd('/', '\\') + separator + "bin";
    }
}
