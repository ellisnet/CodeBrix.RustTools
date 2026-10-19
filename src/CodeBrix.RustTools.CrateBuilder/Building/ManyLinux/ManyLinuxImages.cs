using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;

/// <summary>
/// The manylinux build images of <see cref="LinuxBaseline.ManyLinux"/>: the Containerfiles
/// embedded in this assembly (one per Linux architecture), their pins, their glibc floors, and
/// the <see cref="BuildImageSpec"/> that builds one through CodeBrix.RustTools.Docker
/// (bring-your-own-Containerfile route).
/// </summary>
/// <remarks>
/// linux-x64 builds natively on an x86-64 engine. The aarch64 and riscv64 images are
/// linux/arm64 and linux/riscv64 images: their specs carry <see cref="BuildImageSpec.Platform"/>,
/// so on an x86-64 engine they are built and run under qemu-user emulation (the engine's host
/// needs a qemu-user binfmt handler for the architecture; the Docker build driver probes for it
/// first). The linux-x64 spec carries no platform, so its image identity is the one it always
/// had.
/// </remarks>
internal static class ManyLinuxImages
{
    /// <summary>The digest-pinned base image of linux-x64 (glibc 2.28).</summary>
    public const string X64BaseImage = "quay.io/pypa/manylinux_2_28_x86_64@sha256:a61875a2f84cab7df8de222ff12cabc08ff86eb4ad402ac90ba7bdaed9600cca";

    /// <summary>The dated tag the linux-x64 digest belongs to (for people; the digest is what
    /// is pinned).</summary>
    public const string X64BaseImageTag = "quay.io/pypa/manylinux_2_28_x86_64:2026.07.19-1";

    /// <summary>The glibc version of the linux-x64 image: the oldest glibc its libraries load
    /// on.</summary>
    public const string X64GlibcFloor = "2.28";

    /// <summary>The digest-pinned base image of linux-arm64 (glibc 2.28).</summary>
    public const string Arm64BaseImage = "quay.io/pypa/manylinux_2_28_aarch64@sha256:162c81dfd3efc710732a571717d3c916a6945ebf279e879ddee3243af96fe46f";

    /// <summary>The dated tag the linux-arm64 digest belongs to.</summary>
    public const string Arm64BaseImageTag = "quay.io/pypa/manylinux_2_28_aarch64:2026.07.19-1";

    /// <summary>The glibc version of the linux-arm64 image.</summary>
    public const string Arm64GlibcFloor = "2.28";

    /// <summary>The digest-pinned base image of linux-riscv64 (glibc 2.39: quay.io/pypa
    /// publishes no older manylinux baseline for riscv64).</summary>
    public const string RiscV64BaseImage = "quay.io/pypa/manylinux_2_39_riscv64@sha256:04b3acc69a6cebb10ac1252587d202acce6445d91c783d3f92229bb2b2483069";

    /// <summary>The dated tag the linux-riscv64 digest belongs to.</summary>
    public const string RiscV64BaseImageTag = "quay.io/pypa/manylinux_2_39_riscv64:2026.07.19-1";

    /// <summary>The glibc version of the linux-riscv64 image.</summary>
    public const string RiscV64GlibcFloor = "2.39";

    /// <summary>The Rust toolchain rustup installs into the images.</summary>
    public const string RustVersion = "1.99.0";

    /// <summary>The rustup release whose rustup-init installs Rust.</summary>
    public const string RustupVersion = "1.29.1";

    /// <summary>The SHA-256 of that release's x86_64-unknown-linux-gnu rustup-init.</summary>
    public const string RustupInitSha256X64 = "dda7234360b7f578ca8b0ddcb80145646fa61a67c1720a5abc7051b35c9fcb71";

    /// <summary>The SHA-256 of that release's aarch64-unknown-linux-gnu rustup-init.</summary>
    public const string RustupInitSha256Arm64 = "15f6e4ce9f583b929c996c91562bad6d4454f3281de858b02cdfdef615fac433";

    /// <summary>The SHA-256 of that release's riscv64gc-unknown-linux-gnu rustup-init.</summary>
    public const string RustupInitSha256RiscV64 = "e94134ed99fed1a3a1f13a3bf0a05d616fc1c2496ec0b557aaee4856cd34b2f2";

    /// <summary>The Containerfile file name of linux-x64 (also its embedded resource
    /// suffix).</summary>
    public const string X64ContainerfileName = "Containerfile.x86_64";

    /// <summary>The Containerfile file name of linux-arm64.</summary>
    public const string Arm64ContainerfileName = "Containerfile.aarch64";

    /// <summary>The Containerfile file name of linux-riscv64.</summary>
    public const string RiscV64ContainerfileName = "Containerfile.riscv64";

    /// <summary>The reason a non-Linux target is not buildable with the manylinux
    /// baseline.</summary>
    public const string NonLinuxReason = "The ManyLinux baseline builds Linux targets only; use LinuxBaseline.RustImage for the others.";

    /// <summary>Whether CrateBuilder has a manylinux image for a target: the three Linux
    /// targets.</summary>
    public static bool HasImage(CrateTarget target) => CrateTargets.IsLinux(target);

    /// <summary>The architecture name of a target's image (<c>x86_64</c>, <c>aarch64</c> or
    /// <c>riscv64</c>), as in its Containerfile name and its scratch folder.</summary>
    public static string ArchitectureName(CrateTarget target) => Pick(target, "x86_64", "aarch64", "riscv64");

    /// <summary>The Docker platform a target's image is for: <c>linux/amd64</c>,
    /// <c>linux/arm64</c> or <c>linux/riscv64</c>. A platform that is not the engine's own runs
    /// under emulation.</summary>
    public static string PlatformOf(CrateTarget target) => Pick(target, DockerPlatforms.LinuxAmd64, DockerPlatforms.LinuxArm64, DockerPlatforms.LinuxRiscV64);

    /// <summary>The digest-pinned base image of a target's image.</summary>
    public static string BaseImage(CrateTarget target) => Pick(target, X64BaseImage, Arm64BaseImage, RiscV64BaseImage);

    /// <summary>The Containerfile file name of a target's image.</summary>
    public static string ContainerfileName(CrateTarget target) => Pick(target, X64ContainerfileName, Arm64ContainerfileName, RiscV64ContainerfileName);

    /// <summary>The Containerfile text of a target's image.</summary>
    /// <exception cref="ArgumentException">There is no image for the target.</exception>
    public static string ReadContainerfile(CrateTarget target)
    {
        var name = ResourceName(target);
        using var stream = typeof(ManyLinuxImages).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The embedded resource '{name}' is missing from {typeof(ManyLinuxImages).Assembly.GetName().Name}.");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd().Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    /// <summary>The build arguments passed with a target's Containerfile (every pin).</summary>
    /// <exception cref="ArgumentException">There is no image for the target.</exception>
    public static IDictionary<string, string> BuildArguments(CrateTarget target)
    {
        EnsureImage(target);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASE_IMAGE"] = BaseImage(target),
            ["RUST_VERSION"] = RustVersion,
            ["RUSTUP_VERSION"] = RustupVersion,
            ["RUSTUP_INIT_SHA256"] = Pick(target, RustupInitSha256X64, RustupInitSha256Arm64, RustupInitSha256RiscV64),
            ["RUSTUP_HOST"] = CrateTargets.Triple(target),
        };
    }

    /// <summary>The glibc floor of a target's image: the oldest glibc the libraries built in it
    /// load on (2.28 for linux-x64 and linux-arm64, 2.39 for linux-riscv64).</summary>
    /// <exception cref="ArgumentException">There is no image for the target.</exception>
    public static string GlibcFloor(CrateTarget target) => Pick(target, X64GlibcFloor, Arm64GlibcFloor, RiscV64GlibcFloor);

    /// <summary>
    /// Writes a target's Containerfile into <paramref name="directory"/> (its own build
    /// context, holding nothing else) and returns the spec that builds it. The image tag is a
    /// hash of the file, the arguments, the platform and the context, so an unchanged
    /// Containerfile is built once per engine and reused. linux-arm64 and linux-riscv64 set
    /// <see cref="BuildImageSpec.Platform"/> (<see cref="PlatformOf"/>); linux-x64 leaves it
    /// unset (the engine's own platform on an x86-64 engine, and the image identity it always
    /// had).
    /// </summary>
    /// <exception cref="ArgumentException">There is no image for the target.</exception>
    public static BuildImageSpec CreateSpec(CrateTarget target, string directory)
    {
        EnsureImage(target);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, ContainerfileName(target));
        File.WriteAllText(path, ReadContainerfile(target), new UTF8Encoding(false));
        var spec = new BuildImageSpec
        {
            Containerfile = path,
            ContainerfileContext = directory,
            Platform = target == CrateTarget.LinuxX64 ? null : PlatformOf(target),
        };
        foreach (var pair in BuildArguments(target))
        {
            spec.ContainerfileBuildArgs[pair.Key] = pair.Value;
        }

        return spec;
    }

    /// <summary>
    /// Why a target's manylinux image cannot be used on this engine after the platform probe
    /// said its platform does not run: the image is a foreign-platform image that needs
    /// qemu-user emulation, what the probe found, and the guidance that sets the emulation up.
    /// </summary>
    public static string EmulationUnavailableReason(CrateTarget target, DockerEngineInfo engine, PlatformProbeResult probe)
    {
        var guidance = probe?.Guidance ?? DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.EnableForeignPlatforms, engine?.HostOperatingSystem ?? CodeBrix.RustTools.Toolchain.HostOperatingSystem.Detect());
        var machine = DockerPlatforms.MachineOf(PlatformOf(target));
        return $"The ManyLinux image of {CrateTargets.Rid(target)} is a {PlatformOf(target)} image and this Docker engine is {engine?.Architecture ?? "of unknown architecture"}, "
               + $"so it can only run under qemu-user emulation, which needs a qemu-user binfmt handler for {machine} registered on the engine's host (/proc/sys/fs/binfmt_misc/qemu-{machine}). "
               + $"The platform probe found: {probe?.Message ?? "no answer"} "
               + "To set the emulation up: " + guidance.ToString().Replace(Environment.NewLine, " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal)
               + " (or use LinuxBaseline.RustImage, which cross-compiles linux-arm64 and linux-riscv64 with Debian's cross toolchains).";
    }

    private static string ResourceName(CrateTarget target) => "CodeBrix.RustTools.CrateBuilder.Building.ManyLinux." + ContainerfileName(target);

    private static string Pick(CrateTarget target, string x64, string arm64, string riscv64) => target switch
    {
        CrateTarget.LinuxX64 => x64,
        CrateTarget.LinuxArm64 => arm64,
        CrateTarget.LinuxRiscV64 => riscv64,
        _ => throw new ArgumentException($"There is no manylinux image for {CrateTargets.Rid(target)}. {NonLinuxReason}", nameof(target)),
    };

    private static void EnsureImage(CrateTarget target)
    {
        if (!HasImage(target))
        {
            throw new ArgumentException($"There is no manylinux image for {CrateTargets.Rid(target)}. {NonLinuxReason}", nameof(target));
        }
    }
}
