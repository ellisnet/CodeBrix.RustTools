using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Finds the Android NDK on this machine, the way Android's own tools and cargo-ndk do: the
/// <c>ANDROID_NDK_HOME</c> or <c>ANDROID_NDK_ROOT</c> variable, else the newest
/// <c>ndk/&lt;version&gt;</c> folder (or the legacy <c>ndk-bundle</c>) under the SDK named by
/// <c>ANDROID_HOME</c> or <c>ANDROID_SDK_ROOT</c>. Never installs anything.
/// </summary>
public static class AndroidNdkLocator
{
    /// <summary>The variable that names an NDK root directly.</summary>
    public const string NdkHomeVariable = "ANDROID_NDK_HOME";

    /// <summary>The older variable that names an NDK root directly.</summary>
    public const string NdkRootVariable = "ANDROID_NDK_ROOT";

    /// <summary>The variable that names the SDK root (the NDKs live under <c>ndk/</c>).</summary>
    public const string SdkHomeVariable = "ANDROID_HOME";

    /// <summary>The older variable that names the SDK root.</summary>
    public const string SdkRootVariable = "ANDROID_SDK_ROOT";

    /// <summary>The host tag of this machine's NDK toolchain (<c>linux-x86_64</c>,
    /// <c>darwin-x86_64</c>, <c>windows-x86_64</c>), or <c>null</c> when Google ships no NDK
    /// host tools for this machine (see <see cref="HostSupportMessage"/>).</summary>
    public static string HostTag { get; } = HostTagFor(HostOperatingSystem.Detect(), RuntimeInformation.OSArchitecture);

    /// <summary>Why <see cref="HostTag"/> is <c>null</c>, or <c>null</c> when this machine can run
    /// the NDK.</summary>
    public static string HostSupportMessage { get; } = HostTag != null
        ? null
        : $"Google ships the Android NDK's host tools for Linux x86-64, macOS (Intel and Apple Silicon) and Windows x86-64 only; this machine is {HostOperatingSystem.Describe()} on {RuntimeInformation.OSArchitecture}.";

    /// <summary>
    /// The NDK host tag for an operating system and processor: Linux and Windows need x86-64
    /// (the NDK has no ARM64 host tools for them); macOS is served by the <c>darwin-x86_64</c>
    /// toolchain on Intel and on Apple Silicon, where its programs are universal binaries.
    /// </summary>
    /// <param name="operatingSystem">The operating-system family.</param>
    /// <param name="architecture">The processor architecture.</param>
    /// <returns>The host tag, or <c>null</c> when the NDK does not run there.</returns>
    public static string HostTagFor(OperatingSystemKind operatingSystem, Architecture architecture) => operatingSystem switch
    {
        OperatingSystemKind.MacOS when architecture is Architecture.X64 or Architecture.Arm64 => "darwin-x86_64",
        OperatingSystemKind.Windows when architecture == Architecture.X64 => "windows-x86_64",
        OperatingSystemKind.DebianLinux or OperatingSystemKind.OtherLinux when architecture == Architecture.X64 => "linux-x86_64",
        _ => null,
    };

    /// <summary>
    /// Finds the NDK for this machine.
    /// </summary>
    /// <param name="explicitPath">An NDK root to use instead of searching; when it is not an NDK
    /// for this host the result is <c>null</c> (no fallback).</param>
    /// <returns>The NDK, or <c>null</c> when none was found or this machine cannot run one.</returns>
    public static AndroidNdk Find(string explicitPath = null) => Find(explicitPath, Environment.GetEnvironmentVariable, HostTag);

    /// <summary>
    /// Finds the NDK with a given environment and host tag (for tests and for describing other
    /// machines).
    /// </summary>
    /// <param name="explicitPath">See <see cref="Find(string)"/>.</param>
    /// <param name="getEnvironmentVariable">Reads a variable; <c>null</c> reads the process
    /// environment.</param>
    /// <param name="hostTag">The host tag whose toolchain the NDK must hold; <c>null</c> means
    /// no NDK can be used.</param>
    public static AndroidNdk Find(string explicitPath, Func<string, string> getEnvironmentVariable, string hostTag)
    {
        if (string.IsNullOrWhiteSpace(hostTag))
        {
            return null;
        }

        getEnvironmentVariable ??= Environment.GetEnvironmentVariable;
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return AndroidNdk.TryLoad(explicitPath, hostTag, out var explicitNdk) ? explicitNdk : null;
        }

        foreach (var variable in new[] { NdkHomeVariable, NdkRootVariable })
        {
            var value = getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value) && AndroidNdk.TryLoad(value, hostTag, out var ndk))
            {
                return ndk;
            }
        }

        foreach (var variable in new[] { SdkHomeVariable, SdkRootVariable })
        {
            var value = getEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
            {
                var ndk = FindInSdk(value, hostTag);
                if (ndk != null)
                {
                    return ndk;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the newest NDK installed under an SDK root: the highest-versioned
    /// <c>ndk/&lt;version&gt;</c> folder that holds a toolchain for the host tag, else the legacy
    /// <c>ndk-bundle</c> folder.
    /// </summary>
    /// <param name="sdkDirectory">The SDK root (what <c>ANDROID_HOME</c> names).</param>
    /// <param name="hostTag">The host tag; <c>null</c> means this machine's.</param>
    /// <returns>The NDK, or <c>null</c>.</returns>
    public static AndroidNdk FindInSdk(string sdkDirectory, string hostTag = null)
    {
        hostTag ??= HostTag;
        if (string.IsNullOrWhiteSpace(sdkDirectory) || string.IsNullOrWhiteSpace(hostTag))
        {
            return null;
        }

        var root = Path.GetFullPath(sdkDirectory.Trim());
        foreach (var candidate in ListSideBySideNdks(root))
        {
            if (AndroidNdk.TryLoad(candidate, hostTag, out var ndk))
            {
                return ndk;
            }
        }

        var bundle = Path.Combine(root, "ndk-bundle");
        return AndroidNdk.TryLoad(bundle, hostTag, out var legacy) ? legacy : null;
    }

    /// <summary>
    /// The <c>ndk/&lt;version&gt;</c> folders under an SDK root, newest version first (versions
    /// compare numerically, part by part).
    /// </summary>
    /// <param name="sdkDirectory">The SDK root.</param>
    public static IReadOnlyList<string> ListSideBySideNdks(string sdkDirectory)
    {
        var ndkRoot = Path.Combine(sdkDirectory, "ndk");
        if (!Directory.Exists(ndkRoot))
        {
            return Array.Empty<string>();
        }

        return Directory.EnumerateDirectories(ndkRoot)
            .Select(d => (Path: d, Version: VersionParts(Path.GetFileName(d))))
            .OrderByDescending(e => e.Version, VersionComparer.Instance)
            .ThenByDescending(e => e.Path, StringComparer.Ordinal)
            .Select(e => e.Path)
            .ToList();
    }

    private static long[] VersionParts(string name)
        => name.Split('.', '-').Select(p => long.TryParse(p, out var n) ? n : -1L).ToArray();

    private sealed class VersionComparer : IComparer<long[]>
    {
        public static readonly VersionComparer Instance = new VersionComparer();

        public int Compare(long[] x, long[] y)
        {
            var length = Math.Max(x?.Length ?? 0, y?.Length ?? 0);
            for (var i = 0; i < length; i++)
            {
                var a = x != null && i < x.Length ? x[i] : -1L;
                var b = y != null && i < y.Length ? y[i] : -1L;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            return 0;
        }
    }
}
