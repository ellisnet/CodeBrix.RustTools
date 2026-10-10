using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// An Android NDK installation on this machine: where it is, which release it is, and the
/// LLVM toolchain inside it that Rust needs (the per-API-level <c>clang</c> wrappers that act
/// as cargo's linker and the <c>cc</c> crate's compiler, and <c>llvm-ar</c>). Found by
/// <see cref="AndroidNdkLocator"/>; used by <c>AndroidLinkProfile</c>.
/// </summary>
public sealed partial class AndroidNdk
{
    /// <summary>The file at the NDK root that names its release (<c>Pkg.Revision</c>).</summary>
    public const string SourcePropertiesFileName = "source.properties";

    private AndroidNdk(string path, string version, string hostTag, IReadOnlyList<int> apiLevels)
    {
        Path = path;
        Version = version;
        HostTag = hostTag;
        ApiLevels = apiLevels;
    }

    /// <summary>The NDK root folder (the one that holds <c>source.properties</c>).</summary>
    public string Path { get; }

    /// <summary>The release, from <c>source.properties</c> (for example <c>30.0.16248370</c>),
    /// or <c>null</c> when the file could not be read.</summary>
    public string Version { get; }

    /// <summary>The host tag of the toolchain in use (<c>linux-x86_64</c>, <c>darwin-x86_64</c>
    /// or <c>windows-x86_64</c>; see <see cref="AndroidNdkLocator.HostTagFor"/>).</summary>
    public string HostTag { get; }

    /// <summary>The API levels the toolchain has <c>clang</c> wrappers for, ascending.</summary>
    public IReadOnlyList<int> ApiLevels { get; }

    /// <summary>Whether the tools carry Windows file names (<c>.cmd</c> wrappers, <c>.exe</c>
    /// programs).</summary>
    public bool WindowsFileNames => HostTag.StartsWith("windows", StringComparison.OrdinalIgnoreCase);

    /// <summary>The LLVM toolchain folder: <c>toolchains/llvm/prebuilt/&lt;host tag&gt;</c>.</summary>
    public string ToolchainDirectory => System.IO.Path.Combine(Path, "toolchains", "llvm", "prebuilt", HostTag);

    /// <summary>The toolchain's <c>bin</c> folder, which holds the wrappers and
    /// <c>llvm-ar</c>.</summary>
    public string ToolchainBinDirectory => System.IO.Path.Combine(ToolchainDirectory, "bin");

    /// <summary>The toolchain's sysroot (Android's C headers and libraries).</summary>
    public string SysrootDirectory => System.IO.Path.Combine(ToolchainDirectory, "sysroot");

    /// <summary>The archiver, <c>llvm-ar</c>.</summary>
    public string ArchiverPath => System.IO.Path.Combine(ToolchainBinDirectory, WindowsFileNames ? "llvm-ar.exe" : "llvm-ar");

    /// <summary>The <c>llvm-ranlib</c> tool.</summary>
    public string RanlibPath => System.IO.Path.Combine(ToolchainBinDirectory, WindowsFileNames ? "llvm-ranlib.exe" : "llvm-ranlib");

    /// <summary>
    /// The <c>clang</c> wrapper that targets <paramref name="triple"/> at <paramref name="apiLevel"/>
    /// (for example <c>aarch64-linux-android24-clang</c>, <c>.cmd</c> on Windows).
    /// </summary>
    /// <param name="triple">A Rust Android target triple.</param>
    /// <param name="apiLevel">The Android API level to link for.</param>
    public string ClangWrapperPath(string triple, int apiLevel)
        => System.IO.Path.Combine(ToolchainBinDirectory, WrapperFileName(triple, apiLevel, "clang", WindowsFileNames));

    /// <summary>The matching <c>clang++</c> wrapper.</summary>
    /// <param name="triple">A Rust Android target triple.</param>
    /// <param name="apiLevel">The Android API level to link for.</param>
    public string ClangPlusPlusWrapperPath(string triple, int apiLevel)
        => System.IO.Path.Combine(ToolchainBinDirectory, WrapperFileName(triple, apiLevel, "clang++", WindowsFileNames));

    /// <summary>Whether the toolchain has wrappers for an API level.</summary>
    /// <param name="apiLevel">The Android API level.</param>
    public bool SupportsApiLevel(int apiLevel) => ApiLevels.Contains(apiLevel);

    /// <summary>
    /// Loads the NDK at a folder.
    /// </summary>
    /// <param name="directory">The NDK root.</param>
    /// <param name="hostTag">The host tag to use; <c>null</c> means this machine's
    /// (<see cref="AndroidNdkLocator.HostTag"/>).</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is blank.</exception>
    /// <exception cref="DirectoryNotFoundException">The folder is not an NDK with a toolchain for
    /// the host tag, or no host tag applies to this machine.</exception>
    public static AndroidNdk Load(string directory, string hostTag = null)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The NDK folder is required.", nameof(directory));
        }

        hostTag ??= AndroidNdkLocator.HostTag;
        if (hostTag == null)
        {
            throw new DirectoryNotFoundException("No Android NDK host tag applies to this machine: " + AndroidNdkLocator.HostSupportMessage);
        }

        return TryLoad(directory, hostTag, out var ndk)
            ? ndk
            : throw new DirectoryNotFoundException($"'{directory}' is not an Android NDK with a toolchain for {hostTag} (expected toolchains/llvm/prebuilt/{hostTag}/bin).");
    }

    /// <summary>
    /// Loads the NDK at a folder when it holds a toolchain for the host tag.
    /// </summary>
    /// <param name="directory">The NDK root.</param>
    /// <param name="hostTag">The host tag whose toolchain must exist.</param>
    /// <param name="ndk">The NDK, or <c>null</c>.</param>
    /// <returns><c>true</c> when the folder is such an NDK.</returns>
    public static bool TryLoad(string directory, string hostTag, out AndroidNdk ndk)
    {
        ndk = null;
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(hostTag))
        {
            return false;
        }

        var root = System.IO.Path.GetFullPath(directory.Trim());
        var bin = System.IO.Path.Combine(root, "toolchains", "llvm", "prebuilt", hostTag.Trim(), "bin");
        if (!Directory.Exists(bin))
        {
            return false;
        }

        var windows = hostTag.StartsWith("windows", StringComparison.OrdinalIgnoreCase);
        var levels = new SortedSet<int>();
        foreach (var file in Directory.EnumerateFiles(bin))
        {
            var match = WrapperPattern().Match(System.IO.Path.GetFileName(file));
            if (match.Success && (windows == match.Groups["cmd"].Success) && int.TryParse(match.Groups["api"].Value, out var level))
            {
                levels.Add(level);
            }
        }

        ndk = new AndroidNdk(root, ReadVersion(root), hostTag.Trim(), levels.ToList());
        return true;
    }

    /// <summary>Returns the version, path and host tag.</summary>
    public override string ToString() => $"Android NDK {Version ?? "(unknown version)"} at {Path} ({HostTag})";

    internal static string WrapperFileName(string triple, int apiLevel, string tool, bool windowsFileNames)
    {
        if (string.IsNullOrWhiteSpace(triple))
        {
            throw new ArgumentException("The target triple is required.", nameof(triple));
        }

        return triple.Trim() + apiLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + tool + (windowsFileNames ? ".cmd" : string.Empty);
    }

    private static string ReadVersion(string root)
    {
        var file = System.IO.Path.Combine(root, SourcePropertiesFileName);
        try
        {
            if (!File.Exists(file))
            {
                return null;
            }

            foreach (var line in File.ReadLines(file))
            {
                var separator = line.IndexOf('=');
                if (separator > 0 && line.Substring(0, separator).Trim() == "Pkg.Revision")
                {
                    var value = line.Substring(separator + 1).Trim();
                    return value.Length == 0 ? null : value;
                }
            }
        }
        catch (IOException)
        {
            // An unreadable file leaves the version unknown.
        }
        catch (UnauthorizedAccessException)
        {
            // Same.
        }

        return null;
    }

    [GeneratedRegex(@"^(?<triple>[a-z0-9_]+-linux-android(eabi)?)(?<api>[0-9]+)-clang(?<cmd>\.cmd)?$")]
    private static partial Regex WrapperPattern();
}
