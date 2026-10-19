using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.RustTools.Docker.Images;

namespace CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;

/// <summary>
/// The manylinux build images of <see cref="LinuxBaseline.ManyLinux"/>: the Containerfiles
/// embedded in this assembly, their pins, and the <see cref="BuildImageSpec"/> that builds one
/// through CodeBrix.RustTools.Docker (bring-your-own-Containerfile route).
/// </summary>
/// <remarks>
/// Only linux-x64 has an image. The aarch64 and riscv64 manylinux images run only under
/// qemu-user emulation on an x86-64 engine, and neither CodeBrix.RustTools.Docker
/// (<see cref="BuildImageSpec"/>, <c>BuildContainerOptions</c>) nor CodeBrix.Docker
/// (<c>ImageBuildSpec</c>, <c>ContainerSpec</c>) can ask the engine for another platform
/// (<c>--platform</c>), so those targets are reported as not buildable with this
/// baseline.
/// </remarks>
internal static class ManyLinuxImages
{
    /// <summary>The digest-pinned base image of linux-x64 (glibc 2.28).</summary>
    public const string X64BaseImage = "quay.io/pypa/manylinux_2_28_x86_64@sha256:a61875a2f84cab7df8de222ff12cabc08ff86eb4ad402ac90ba7bdaed9600cca";

    /// <summary>The dated tag the digest belongs to (for people; the digest is what is
    /// pinned).</summary>
    public const string X64BaseImageTag = "quay.io/pypa/manylinux_2_28_x86_64:2026.07.19-1";

    /// <summary>The glibc version of the linux-x64 image: the oldest glibc its libraries load
    /// on.</summary>
    public const string X64GlibcFloor = "2.28";

    /// <summary>The Rust toolchain rustup installs into the images.</summary>
    public const string RustVersion = "1.99.0";

    /// <summary>The rustup release whose rustup-init installs Rust.</summary>
    public const string RustupVersion = "1.29.1";

    /// <summary>The SHA-256 of that release's x86_64-unknown-linux-gnu rustup-init.</summary>
    public const string RustupInitSha256X64 = "dda7234360b7f578ca8b0ddcb80145646fa61a67c1720a5abc7051b35c9fcb71";

    /// <summary>The Containerfile file name of linux-x64 (also its embedded resource
    /// suffix).</summary>
    public const string X64ContainerfileName = "Containerfile.x86_64";

    /// <summary>The reason a Linux target other than linux-x64 is not buildable with the
    /// manylinux baseline.</summary>
    public const string ForeignArchitectureReason =
        "The ManyLinux baseline builds linux-x64 only: the aarch64 and riscv64 manylinux images would have to run as foreign-platform images under qemu-user, and CodeBrix.RustTools.Docker / CodeBrix.Docker offer no platform option (--platform) to build or run them. Use LinuxBaseline.RustImage, which cross-compiles linux-arm64 and linux-riscv64 with Debian's cross toolchains.";

    /// <summary>The reason a non-Linux target is not buildable with the manylinux
    /// baseline.</summary>
    public const string NonLinuxReason = "The ManyLinux baseline builds Linux targets only; use LinuxBaseline.RustImage for the others.";

    /// <summary>Whether CrateBuilder has a manylinux image for a target.</summary>
    public static bool HasImage(CrateTarget target) => target == CrateTarget.LinuxX64;

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

    /// <summary>The build arguments passed with the Containerfile (every pin).</summary>
    public static IDictionary<string, string> BuildArguments(CrateTarget target)
    {
        EnsureImage(target);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["BASE_IMAGE"] = X64BaseImage,
            ["RUST_VERSION"] = RustVersion,
            ["RUSTUP_VERSION"] = RustupVersion,
            ["RUSTUP_INIT_SHA256"] = RustupInitSha256X64,
            ["RUSTUP_HOST"] = "x86_64-unknown-linux-gnu",
        };
    }

    /// <summary>The glibc floor of a target's image.</summary>
    public static string GlibcFloor(CrateTarget target)
    {
        EnsureImage(target);
        return X64GlibcFloor;
    }

    /// <summary>
    /// Writes a target's Containerfile into <paramref name="directory"/> (its own build
    /// context, holding nothing else) and returns the spec that builds it. The image tag is a
    /// hash of the file, the arguments and the context, so an unchanged Containerfile is built
    /// once per engine and reused.
    /// </summary>
    public static BuildImageSpec CreateSpec(CrateTarget target, string directory)
    {
        EnsureImage(target);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, X64ContainerfileName);
        File.WriteAllText(path, ReadContainerfile(target), new UTF8Encoding(false));
        var spec = new BuildImageSpec
        {
            Containerfile = path,
            ContainerfileContext = directory,
        };
        foreach (var pair in BuildArguments(target))
        {
            spec.ContainerfileBuildArgs[pair.Key] = pair.Value;
        }

        return spec;
    }

    private static string ResourceName(CrateTarget target)
    {
        EnsureImage(target);
        return "CodeBrix.RustTools.CrateBuilder.Building.ManyLinux." + X64ContainerfileName;
    }

    private static void EnsureImage(CrateTarget target)
    {
        if (!HasImage(target))
        {
            throw new ArgumentException($"There is no manylinux image for {CrateTargets.Rid(target)}. " + (CrateTargets.IsLinux(target) ? ForeignArchitectureReason : NonLinuxReason), nameof(target));
        }
    }
}
