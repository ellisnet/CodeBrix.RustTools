using System;
using System.Text.RegularExpressions;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// Docker platform names (<c>os/architecture[/variant]</c>, for example <c>linux/arm64</c>) as
/// <see cref="BuildImageSpec.Platform"/> and <see cref="Containers.BuildContainerOptions.Platform"/>
/// take them, and how they relate to an engine's own architecture and to the Linux targets.
/// </summary>
/// <remarks>
/// A foreign platform (one that is not the engine's own architecture) runs under emulation:
/// on a Linux engine host that needs a qemu-user binfmt handler for the architecture
/// (<c>/proc/sys/fs/binfmt_misc/qemu-aarch64</c>, <c>qemu-riscv64</c>, ...); Docker Desktop
/// brings its own. <see cref="DockerRustWorkbench.ProbePlatformAsync"/> finds out whether it
/// works, and <see cref="DockerGuidanceTopic.EnableForeignPlatforms"/> says how to set it up.
/// </remarks>
public static partial class DockerPlatforms
{
    /// <summary>64-bit x86 Linux: <c>linux/amd64</c>.</summary>
    public const string LinuxAmd64 = "linux/amd64";

    /// <summary>64-bit ARM Linux: <c>linux/arm64</c>.</summary>
    public const string LinuxArm64 = "linux/arm64";

    /// <summary>64-bit RISC-V Linux: <c>linux/riscv64</c>.</summary>
    public const string LinuxRiscV64 = "linux/riscv64";

    /// <summary>
    /// Trims and lower-cases a platform name; <c>null</c> or blank gives <c>null</c> (the
    /// engine's own platform).
    /// </summary>
    /// <param name="platform">A platform such as <c>linux/arm64</c>.</param>
    /// <exception cref="ArgumentException">The value is a comma-separated list (one platform
    /// only: a build loads one single-platform image), or is not of the form
    /// <c>os/architecture[/variant]</c>.</exception>
    public static string Normalize(string platform)
    {
        if (string.IsNullOrWhiteSpace(platform))
        {
            return null;
        }

        var value = platform.Trim().ToLowerInvariant();
        if (value.Contains(','))
        {
            throw new ArgumentException($"'{platform}' names more than one platform; give exactly one, for example {LinuxArm64}.", nameof(platform));
        }

        if (!PlatformPattern().IsMatch(value))
        {
            throw new ArgumentException($"'{platform}' is not a platform of the form os/architecture[/variant], for example {LinuxArm64}.", nameof(platform));
        }

        return value;
    }

    /// <summary>
    /// The architecture part of a platform as <c>uname -m</c> names it inside a container of
    /// that platform (<c>linux/amd64</c> -> <c>x86_64</c>, <c>linux/arm64</c> -> <c>aarch64</c>,
    /// <c>linux/riscv64</c> -> <c>riscv64</c>; any other architecture as it is written), or
    /// <c>null</c> for <c>null</c>.
    /// </summary>
    /// <param name="platform">A platform such as <c>linux/arm64</c>.</param>
    /// <exception cref="ArgumentException">The platform is not valid (see
    /// <see cref="Normalize"/>).</exception>
    public static string MachineOf(string platform)
    {
        var normalized = Normalize(platform);
        if (normalized == null)
        {
            return null;
        }

        var architecture = normalized.Split('/')[1];
        return architecture switch
        {
            "amd64" or "x86_64" => "x86_64",
            "arm64" or "aarch64" => "aarch64",
            "riscv64" => "riscv64",
            _ => architecture,
        };
    }

    /// <summary>
    /// The Linux platform of an engine architecture as the engine reports it
    /// (<see cref="DockerEngineInfo.Architecture"/>: <c>x86_64</c> -> <c>linux/amd64</c>,
    /// <c>aarch64</c> -> <c>linux/arm64</c>, <c>riscv64</c> -> <c>linux/riscv64</c>), or
    /// <c>null</c> for any other.
    /// </summary>
    /// <param name="architecture">The architecture, for example <c>x86_64</c>.</param>
    public static string ForArchitecture(string architecture) => architecture?.Trim().ToLowerInvariant() switch
    {
        "x86_64" or "amd64" => LinuxAmd64,
        "aarch64" or "arm64" => LinuxArm64,
        "riscv64" => LinuxRiscV64,
        _ => null,
    };

    /// <summary>
    /// The platform whose containers build a Linux target natively (<see cref="CrossTarget.LinuxX64"/>
    /// -> <c>linux/amd64</c>, <see cref="CrossTarget.LinuxArm64"/> -> <c>linux/arm64</c>,
    /// <see cref="CrossTarget.LinuxRiscV64"/> -> <c>linux/riscv64</c>), or <c>null</c> for every
    /// other target.
    /// </summary>
    /// <param name="target">A concrete target.</param>
    public static string ForLinuxTarget(CrossTarget target) => target switch
    {
        CrossTarget.LinuxX64 => LinuxAmd64,
        CrossTarget.LinuxArm64 => LinuxArm64,
        CrossTarget.LinuxRiscV64 => LinuxRiscV64,
        _ => null,
    };

    /// <summary>
    /// Whether two platforms name the same operating system and architecture (the variant is
    /// ignored, so <c>linux/arm64/v8</c> matches <c>linux/arm64</c>). <c>null</c> matches only
    /// <c>null</c>.
    /// </summary>
    /// <param name="first">A platform, or <c>null</c>.</param>
    /// <param name="second">A platform, or <c>null</c>.</param>
    /// <exception cref="ArgumentException">A platform is not valid (see
    /// <see cref="Normalize"/>).</exception>
    public static bool AreSame(string first, string second)
    {
        var a = Normalize(first);
        var b = Normalize(second);
        if (a == null || b == null)
        {
            return a == null && b == null;
        }

        return string.Equals(a.Split('/')[0], b.Split('/')[0], StringComparison.Ordinal)
               && string.Equals(MachineOf(a), MachineOf(b), StringComparison.Ordinal);
    }

    [GeneratedRegex("^[a-z0-9_.-]+/[a-z0-9_.-]+(/[a-z0-9_.-]+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex PlatformPattern();
}
