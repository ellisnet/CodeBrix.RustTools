using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Facts about every <see cref="CrateTarget"/>: the groups, the .NET runtime identifier, the
/// Rust target triple and the native library file name.
/// </summary>
/// <remarks>
/// The triples are taken from CodeBrix.RustTools.Docker's <see cref="CrossTargetInfo"/> table
/// (see <see cref="ToCrossTarget"/>), which is also what CodeBrix.RustTools' link profiles
/// build: Linux targets are the glibc <c>*-unknown-linux-gnu</c> triples
/// (<c>riscv64gc-</c> for RISC-V), Windows targets are Rust's <c>*-pc-windows-gnullvm</c> triples
/// (built with llvm-mingw against the Universal C Runtime, never MSVC), macOS targets are
/// <c>*-apple-darwin</c> and Android targets are <c>*-linux-android</c>. The same triple is
/// used in both build modes, so a host build and a container build of one target produce
/// files of the same kind.
/// </remarks>
public static class CrateTargets
{
    /// <summary>Every target, in <see cref="CrateTarget"/> order.</summary>
    public static IReadOnlyList<CrateTarget> All { get; } = Array.AsReadOnly(Enum.GetValues<CrateTarget>());

    /// <summary>The two macOS targets (<c>osx-x64</c>, <c>osx-arm64</c>).</summary>
    public static IReadOnlyList<CrateTarget> MacOS { get; } = Array.AsReadOnly(new[] { CrateTarget.OsxX64, CrateTarget.OsxArm64 });

    /// <summary>The two Windows targets (<c>win-x64</c>, <c>win-arm64</c>).</summary>
    public static IReadOnlyList<CrateTarget> Windows { get; } = Array.AsReadOnly(new[] { CrateTarget.WinX64, CrateTarget.WinArm64 });

    /// <summary>The three Linux targets (<c>linux-x64</c>, <c>linux-arm64</c>,
    /// <c>linux-riscv64</c>).</summary>
    public static IReadOnlyList<CrateTarget> Linux { get; } = Array.AsReadOnly(new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64 });

    /// <summary>The two Android targets (<c>android-x64</c>, <c>android-arm64</c>).</summary>
    public static IReadOnlyList<CrateTarget> Android { get; } = Array.AsReadOnly(new[] { CrateTarget.AndroidX64, CrateTarget.AndroidArm64 });

    /// <summary>
    /// The CodeBrix.RustTools.Docker target that builds the same triple.
    /// </summary>
    /// <param name="target">The target.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="target"/> is not a defined
    /// value.</exception>
    public static CrossTarget ToCrossTarget(CrateTarget target) => target switch
    {
        CrateTarget.WinX64 => CrossTarget.WindowsX64Gnullvm,
        CrateTarget.WinArm64 => CrossTarget.WindowsArm64Gnullvm,
        CrateTarget.OsxX64 => CrossTarget.MacOSX64,
        CrateTarget.OsxArm64 => CrossTarget.MacOSArm64,
        CrateTarget.LinuxX64 => CrossTarget.LinuxX64,
        CrateTarget.LinuxArm64 => CrossTarget.LinuxArm64,
        CrateTarget.LinuxRiscV64 => CrossTarget.LinuxRiscV64,
        CrateTarget.AndroidX64 => CrossTarget.AndroidX64,
        CrateTarget.AndroidArm64 => CrossTarget.AndroidArm64,
        _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown crate target."),
    };

    /// <summary>
    /// The .NET runtime identifier of a target, for example <c>win-x64</c> or
    /// <c>linux-riscv64</c>; the folder name under <c>runtimes/</c> in a NuGet package.
    /// </summary>
    /// <param name="target">The target.</param>
    public static string Rid(CrateTarget target) => Info(target).RuntimeIdentifier;

    /// <summary>
    /// The Rust target triple a target is built for, for example
    /// <c>x86_64-pc-windows-gnullvm</c> or <c>aarch64-linux-android</c> (see the remarks on
    /// <see cref="CrateTargets"/> for why these triples).
    /// </summary>
    /// <param name="target">The target.</param>
    public static string Triple(CrateTarget target) => Info(target).Triple;

    /// <summary>
    /// The file name of a crate's dynamic library on a target, as cargo writes it: the crate
    /// name with every '-' turned into '_', as <c>lib&lt;name&gt;.so</c> (Linux, Android),
    /// <c>lib&lt;name&gt;.dylib</c> (macOS) or <c>&lt;name&gt;.dll</c> (Windows).
    /// </summary>
    /// <param name="target">The target.</param>
    /// <param name="crateName">The crate (library target) name, for example
    /// <c>my-crate</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="crateName"/> is blank.</exception>
    public static string NativeFileName(CrateTarget target, string crateName)
    {
        if (string.IsNullOrWhiteSpace(crateName))
        {
            throw new ArgumentException("A crate name is required.", nameof(crateName));
        }

        return Info(target).SharedLibraryFileName(crateName.Trim());
    }

    /// <summary>
    /// The target of a .NET runtime identifier (case-insensitive), for example
    /// <c>osx-arm64</c>.
    /// </summary>
    /// <param name="rid">One of the nine runtime identifiers returned by <see cref="Rid"/>.</param>
    /// <exception cref="ArgumentException"><paramref name="rid"/> is blank or not one of
    /// them.</exception>
    public static CrateTarget FromRid(string rid)
    {
        var trimmed = (rid ?? string.Empty).Trim();
        foreach (var target in All)
        {
            if (string.Equals(Rid(target), trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return target;
            }
        }

        throw new ArgumentException(
            $"'{rid}' is not a runtime identifier CrateBuilder builds for; use one of: {string.Join(", ", All.Select(Rid))}.",
            nameof(rid));
    }

    /// <summary>Whether a target is one of <see cref="MacOS"/>.</summary>
    /// <param name="target">The target.</param>
    public static bool IsMacOS(CrateTarget target) => target is CrateTarget.OsxX64 or CrateTarget.OsxArm64;

    /// <summary>Whether a target is one of <see cref="Windows"/>.</summary>
    /// <param name="target">The target.</param>
    public static bool IsWindows(CrateTarget target) => target is CrateTarget.WinX64 or CrateTarget.WinArm64;

    /// <summary>Whether a target is one of <see cref="Linux"/>.</summary>
    /// <param name="target">The target.</param>
    public static bool IsLinux(CrateTarget target) => target is CrateTarget.LinuxX64 or CrateTarget.LinuxArm64 or CrateTarget.LinuxRiscV64;

    /// <summary>Whether a target is one of <see cref="Android"/>.</summary>
    /// <param name="target">The target.</param>
    public static bool IsAndroid(CrateTarget target) => target is CrateTarget.AndroidX64 or CrateTarget.AndroidArm64;

    private static CrossTargetInfo Info(CrateTarget target) => CrossTargetInfo.Get(ToCrossTarget(target));
}
