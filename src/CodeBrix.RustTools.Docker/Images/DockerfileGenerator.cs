using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// Turns a <see cref="BuildImageSpec"/> into Dockerfile text. The output is deterministic: the
/// same spec always gives byte-for-byte the same text (Unix line endings, apt packages sorted and
/// de-duplicated, layers in a fixed order), which is what lets the image tag be a hash of the
/// text and identical specs share one image.
/// </summary>
/// <remarks>
/// Layer order: <c>FROM</c>; the <c>rustup default</c> layer; the apt layer; the clippy/rustfmt
/// layer; the Windows layer (llvm-mingw, both Windows targets); the Linux cross layers (x86-64, ARM64, RISC-V 64, one per
/// Linux target in the spec - the workbench removes the engine's own); the Android layer; the
/// macOS layer; the extra lines; then <c>WORKDIR /work</c> and
/// <c>ENV CARGO_TERM_COLOR=never RUSTUP_AUTO_INSTALL=0</c>.
/// </remarks>
public static partial class DockerfileGenerator
{
    /// <summary>
    /// The Android NDK release the Android layer downloads: r30, which developer.android.com
    /// listed as the latest LTS release when this library was written (checked 2026-10-08).
    /// </summary>
    public const string AndroidNdkVersion = "r30";

    /// <summary>The URL the Android layer downloads the Linux NDK from.</summary>
    public const string AndroidNdkUrl = "https://dl.google.com/android/repository/android-ndk-" + AndroidNdkVersion + "-linux.zip";

    /// <summary>The SHA-1 checksum developer.android.com publishes for <see cref="AndroidNdkUrl"/>;
    /// the Android layer verifies the download against it.</summary>
    public const string AndroidNdkSha1 = "5107f898313790e449e87eee2183d9a20602dee9";

    /// <summary>Where the Android layer installs the NDK inside the image (also the value of
    /// <c>ANDROID_NDK_HOME</c>).</summary>
    public const string AndroidNdkHome = "/opt/android-ndk";

    /// <summary>
    /// The llvm-mingw release the Windows layer downloads from
    /// https://github.com/mstorsjo/llvm-mingw/releases (the UCRT build for Ubuntu 22.04 hosts,
    /// which runs on the Debian-based <c>rust</c> image), checked 2026-10-09.
    /// </summary>
    public const string LlvmMingwVersion = "20261006";

    /// <summary>The SHA-256 of the x86-64 Linux host build of <see cref="LlvmMingwVersion"/>.</summary>
    public const string LlvmMingwSha256X64 = "5f9c6ed95b2d4bdb2869a488c5fd5857fbdabcf288a0aa3eb1da43f6a08d8ab4";

    /// <summary>The SHA-256 of the ARM64 Linux host build of <see cref="LlvmMingwVersion"/>.</summary>
    public const string LlvmMingwSha256Arm64 = "e77c4acfa44184f6b0c52b814d724ad63b2ecebb3c3f6f842c0846e717cbd23c";

    /// <summary>Where the Windows layer unpacks llvm-mingw inside the image (also the value of
    /// <c>LLVM_MINGW_HOME</c>); its <c>bin</c> folder holds the clang wrappers and llvm-ar.</summary>
    public const string LlvmMingwHome = "/opt/llvm-mingw";

    /// <summary>
    /// The download URL of the llvm-mingw build for a Linux host architecture
    /// (<c>x86_64</c> or <c>aarch64</c>).
    /// </summary>
    /// <param name="hostArchitecture">The engine's architecture as <c>uname -m</c> prints it.</param>
    public static string LlvmMingwUrl(string hostArchitecture)
        => "https://github.com/mstorsjo/llvm-mingw/releases/download/" + LlvmMingwVersion + "/llvm-mingw-" + LlvmMingwVersion + "-ucrt-ubuntu-22.04-" + hostArchitecture + ".tar.xz";

    /// <summary>The working directory of the image and of every build container.</summary>
    public const string WorkDirectory = "/work";

    /// <summary>Where the macOS layer writes the sysroot it derives: <c>usr/lib/libSystem.tbd</c>
    /// (the libSystem symbol stub: what Rust's std and <see cref="MacOSProbeProgram"/> import plus
    /// <see cref="MacOSStubSupplement"/>) with its <c>libc</c>, <c>libm</c>, <c>libpthread</c> and
    /// <c>libdl</c> aliases, the <c>symbols-&lt;triple&gt;.txt</c> lists it was made from, and a
    /// <c>README.txt</c>. Passed to rustc as <c>SDKROOT</c> for macOS targets; copied to the host
    /// by <see cref="DockerRustWorkbench.ExportMacOSSysrootAsync"/>.</summary>
    public const string MacOSSysrootDirectory = "/opt/codebrix/macos-sysroot";

    /// <summary>A fixed path to the image toolchain's <c>rust-lld</c>, created by the macOS layer
    /// (a symbolic link into the rustup toolchain), used as the linker of macOS targets.</summary>
    public const string MacOSRustLldPath = "/opt/codebrix/bin/rust-lld";

    /// <summary>
    /// Symbol names the macOS layer adds to the derived libSystem stub on top of what Rust's
    /// standard library and the probe program import: the ISO C99 <c>&lt;math.h&gt;</c> functions
    /// (double and float), the POSIX <c>lgamma_r</c> variants Rust's std declares, and the
    /// Darwin-specific library calls LLVM emits for float and memory operations
    /// (<c>__sincos_stret</c>, <c>__exp10</c>, <c>memset_pattern16</c> and relatives). Float methods
    /// are inlined into the crate being compiled and lowered to these calls there, so the std
    /// rlibs never show them and the set-difference derivation alone cannot find them; without
    /// them a crate that uses <c>f64::powf</c> or <c>ln</c> fails to link. Names only, taken from
    /// the C standard and from LLVM; nothing from Apple. Sorted (ordinal), as a C symbol with its
    /// leading underscore.
    /// </summary>
    public static IReadOnlyList<string> MacOSStubSupplement { get; } = Array.AsReadOnly(new[]
    {
        "___exp10", "___exp10f", "___sincos_stret", "___sincosf_stret", "_acos", "_acosf",
        "_acosh", "_acoshf", "_asin", "_asinf", "_asinh", "_asinhf",
        "_atan", "_atan2", "_atan2f", "_atanf", "_atanh", "_atanhf",
        "_cbrt", "_cbrtf", "_ceil", "_ceilf", "_copysign", "_copysignf",
        "_cos", "_cosf", "_cosh", "_coshf", "_erf", "_erfc",
        "_erfcf", "_erff", "_exp", "_exp2", "_exp2f", "_expf",
        "_expm1", "_expm1f", "_fabs", "_fabsf", "_fdim", "_fdimf",
        "_floor", "_floorf", "_fma", "_fmaf", "_fmax", "_fmaxf",
        "_fmin", "_fminf", "_fmod", "_fmodf", "_frexp", "_frexpf",
        "_hypot", "_hypotf", "_ilogb", "_ilogbf", "_ldexp", "_ldexpf",
        "_lgamma", "_lgamma_r", "_lgammaf", "_lgammaf_r", "_llrint", "_llrintf",
        "_llround", "_llroundf", "_log", "_log10", "_log10f", "_log1p",
        "_log1pf", "_log2", "_log2f", "_logb", "_logbf", "_logf",
        "_lrint", "_lrintf", "_lround", "_lroundf", "_memset_pattern16", "_memset_pattern4",
        "_memset_pattern8", "_modf", "_modff", "_nan", "_nanf", "_nearbyint",
        "_nearbyintf", "_nextafter", "_nextafterf", "_pow", "_powf", "_remainder",
        "_remainderf", "_remquo", "_remquof", "_rint", "_rintf", "_round",
        "_roundf", "_scalbn", "_scalbnf", "_sin", "_sinf", "_sinh",
        "_sinhf", "_sqrt", "_sqrtf", "_tan", "_tanf", "_tanh",
        "_tanhf", "_tgamma", "_tgammaf", "_trunc", "_truncf",
    });

    /// <summary>
    /// The Rust program the macOS layer compiles (never runs) while deriving the stub: every
    /// float and memory operation LLVM may lower to a C library call. Its undefined symbols join
    /// the stub, and it is the program the closed-loop link at the end of the derivation builds.
    /// One entry per source line, written into the Dockerfile as a heredoc.
    /// </summary>
    public static IReadOnlyList<string> MacOSProbeProgram { get; } = Array.AsReadOnly(new[]
    {
        "// Probe compiled by CodeBrix.RustTools.Docker while deriving the macOS libSystem stub: every float and memory",
        "// operation that rustc inlines into the compiled crate and LLVM then lowers to a C library call. The symbols this",
        "// object leaves undefined join the stub, and the probe is what the closed-loop link at the end builds.",
        "use std::hint::black_box;",
        "fn main() {",
        "    let x: f64 = black_box(1.5);",
        "    let y: f64 = black_box(2.5);",
        "    let n: i32 = black_box(3);",
        "    let d = [x.powf(y), x.powi(n), x.exp(), x.exp2(), x.ln(), x.log2(), x.log10(), x.log(y), x.sin(), x.cos(), x.tan(),",
        "        x.asin(), x.acos(), x.atan(), x.atan2(y), x.sinh(), x.cosh(), x.tanh(), x.asinh(), x.acosh(), x.atanh(),",
        "        x.cbrt(), x.hypot(y), x.exp_m1(), x.ln_1p(), x.mul_add(y, x), x.floor(), x.ceil(), x.round(), x.round_ties_even(),",
        "        x.trunc(), x.fract(), x % y, x.rem_euclid(y), x.div_euclid(y), x.sqrt(), x.abs(), x.signum(), x.copysign(y),",
        "        x.min(y), x.max(y), x.recip(), 10f64.powf(x), 2f64.powf(x), x.sin_cos().0 + x.sin_cos().1, x.to_degrees(), x.to_radians()];",
        "    let a: f32 = black_box(1.5);",
        "    let b: f32 = black_box(2.5);",
        "    let s = [a.powf(b), a.powi(n), a.exp(), a.exp2(), a.ln(), a.log2(), a.log10(), a.log(b), a.sin(), a.cos(), a.tan(),",
        "        a.asin(), a.acos(), a.atan(), a.atan2(b), a.sinh(), a.cosh(), a.tanh(), a.asinh(), a.acosh(), a.atanh(),",
        "        a.cbrt(), a.hypot(b), a.exp_m1(), a.ln_1p(), a.mul_add(b, a), a.floor(), a.ceil(), a.round(), a.round_ties_even(),",
        "        a.trunc(), a.fract(), a % b, a.rem_euclid(b), a.div_euclid(b), a.sqrt(), a.abs(), a.signum(), a.copysign(b),",
        "        a.min(b), a.max(b), a.recip(), 10f32.powf(a), 2f32.powf(a), a.sin_cos().0 + a.sin_cos().1, a.to_degrees(), a.to_radians()];",
        "    let count = black_box(64usize);",
        "    let mut pattern = vec![0u64; count];",
        "    for e in pattern.iter_mut() { *e = 0x0102030405060708; }",
        "    let mut words = vec![0u32; count];",
        "    for e in words.iter_mut() { *e = 0x01020304; }",
        "    println!(\"probe {} {} {} {}\", d.iter().sum::<f64>(), s.iter().sum::<f32>(), black_box(&pattern)[1], black_box(&words)[1]);",
        "}",
    });

    /// <summary>The first line of every generated Dockerfile.</summary>
    public const string HeaderLine = "# Generated by CodeBrix.RustTools.Docker. Identical text gives an identical image tag.";

    /// <summary>
    /// Generates the Dockerfile for a spec. Unset base image and tag parts use the library
    /// defaults (<c>rust:latest</c>).
    /// </summary>
    /// <param name="spec">The image description.</param>
    /// <returns>The Dockerfile text, with <c>\n</c> line endings and a final newline.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A value in the spec is not safe to put in a
    /// Dockerfile (for example an apt package name with spaces, or a multi-line extra
    /// instruction).</exception>
    /// <exception cref="CrossTargetNotSupportedException">The spec asks for a target that
    /// container builds do not support.</exception>
    public static string Generate(BuildImageSpec spec)
    {
        BuildImageSpec.ThrowIfNull(spec);
        var baseImage = string.IsNullOrWhiteSpace(spec.BaseImage) ? DockerRustWorkbenchOptions.DefaultBaseImage : spec.BaseImage.Trim();
        var tag = string.IsNullOrWhiteSpace(spec.Tag) ? DockerRustWorkbenchOptions.DefaultBaseTag : spec.Tag.Trim();
        Require(ImageNamePattern().IsMatch(baseImage), $"'{baseImage}' is not a valid image repository name.", nameof(spec.BaseImage));
        Require(TagPattern().IsMatch(tag), $"'{tag}' is not a valid image tag.", nameof(spec.Tag));

        var targets = NormalizeTargets(spec.CrossTargets);
        var lines = new List<string>
        {
            HeaderLine,
            $"FROM {baseImage}:{tag}",
        };

        if (!string.IsNullOrWhiteSpace(spec.RustVersion))
        {
            var version = spec.RustVersion.Trim();
            Require(RustVersionPattern().IsMatch(version), $"'{version}' is not a valid Rust toolchain name.", nameof(spec.RustVersion));
            lines.Add($"RUN rustup default {version}");
        }

        var packages = NormalizeAptPackages(spec.AptPackages);
        if (packages.Count > 0)
        {
            lines.Add(AptInstall(packages));
        }

        if (spec.InstallClippyRustfmt)
        {
            lines.Add("RUN rustup component add clippy rustfmt");
        }

        if (targets.Any(t => CrossTargetInfo.Get(t).IsWindows))
        {
            lines.AddRange(WindowsLayer());
        }

        foreach (var info in CrossTargetInfo.All.Where(i => i.IsLinux && targets.Contains(i.Target)))
        {
            lines.AddRange(LinuxCrossLayer(info));
        }

        if (targets.Contains(CrossTarget.AndroidArm64) || targets.Contains(CrossTarget.AndroidX64))
        {
            lines.AddRange(AndroidLayer());
        }

        if (targets.Any(t => CrossTargetInfo.Get(t).IsMacOS))
        {
            lines.AddRange(MacOSLayer());
        }

        if (spec.ExtraDockerfileLines != null)
        {
            foreach (var raw in spec.ExtraDockerfileLines)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                Require(raw.IndexOf('\n') < 0 && raw.IndexOf('\r') < 0,
                    "Each extra Dockerfile line must be a single line.", nameof(spec.ExtraDockerfileLines));
                lines.Add(raw.Trim());
            }
        }

        lines.Add($"WORKDIR {WorkDirectory}");
        lines.Add("ENV CARGO_TERM_COLOR=never RUSTUP_AUTO_INSTALL=0");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>
    /// The hash that identifies a generated Dockerfile: the first 16 lowercase hexadecimal
    /// digits of the SHA-256 of its UTF-8 text. Used as the derived image's tag.
    /// </summary>
    /// <param name="dockerfileText">Text returned by <see cref="Generate"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dockerfileText"/> is
    /// <c>null</c>.</exception>
    public static string ComputeSpecHash(string dockerfileText)
    {
        if (dockerfileText == null)
        {
            throw new ArgumentNullException(nameof(dockerfileText));
        }

        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(dockerfileText));
        return Convert.ToHexStringLower(digest).Substring(0, 16);
    }

    /// <summary>
    /// The hash that identifies a derived image built for a platform: the
    /// <see cref="ComputeSpecHash(string)"/> of the Dockerfile text when
    /// <paramref name="platform"/> is <c>null</c> or blank (the engine's own platform - the hash a
    /// spec without <see cref="BuildImageSpec.Platform"/> always had), otherwise of the text
    /// followed by a <c>platform=&lt;platform&gt;</c> line, so the images of one Dockerfile for
    /// two platforms get two tags.
    /// </summary>
    /// <param name="dockerfileText">Text returned by <see cref="Generate"/>.</param>
    /// <param name="platform">The image's platform (<see cref="BuildImageSpec.Platform"/>), or
    /// <c>null</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dockerfileText"/> is
    /// <c>null</c>.</exception>
    /// <exception cref="ArgumentException"><paramref name="platform"/> is not valid (see
    /// <see cref="Engine.DockerPlatforms.Normalize"/>).</exception>
    public static string ComputeSpecHash(string dockerfileText, string platform)
    {
        var normalized = Engine.DockerPlatforms.Normalize(platform);
        if (dockerfileText == null)
        {
            throw new ArgumentNullException(nameof(dockerfileText));
        }

        return normalized == null ? ComputeSpecHash(dockerfileText) : ComputeSpecHash(dockerfileText + "platform=" + normalized + "\n");
    }

    /// <summary>
    /// The full reference (<c>repository:hash</c>) of the derived image for a spec (the hash
    /// covers <see cref="BuildImageSpec.Platform"/> when it is set, see
    /// <see cref="ComputeSpecHash(string, string)"/>).
    /// </summary>
    /// <param name="spec">The image description.</param>
    /// <param name="defaultRepository">The repository name to use when
    /// <see cref="BuildImageSpec.ImageName"/> is unset. Default
    /// <see cref="DockerRustWorkbenchOptions.DefaultBuildImageRepository"/>, which is what a
    /// workbench with the default label prefix uses; pass the workbench's
    /// <see cref="DockerRustWorkbenchOptions.BuildImageRepository"/> to get the reference a
    /// workbench with another <see cref="DockerRustWorkbenchOptions.LabelPrefix"/> (or an
    /// explicit repository) builds and reuses.</param>
    public static string DerivedImageReference(BuildImageSpec spec, string defaultRepository = DockerRustWorkbenchOptions.DefaultBuildImageRepository)
    {
        BuildImageSpec.ThrowIfNull(spec);
        var repository = string.IsNullOrWhiteSpace(spec.ImageName) ? defaultRepository : spec.ImageName.Trim();
        Require(ImageNamePattern().IsMatch(repository ?? string.Empty), $"'{repository}' is not a valid image repository name.", nameof(spec.ImageName));
        return repository + ":" + ComputeSpecHash(Generate(spec), spec.Platform);
    }

    /// <summary>
    /// Sorts, trims and de-duplicates apt package names and checks each one is a plain Debian
    /// package name (optionally with <c>:arch</c> and <c>=version</c>).
    /// </summary>
    internal static List<string> NormalizeAptPackages(IEnumerable<string> packages)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        if (packages != null)
        {
            foreach (var raw in packages)
            {
                if (string.IsNullOrWhiteSpace(raw))
                {
                    continue;
                }

                var name = raw.Trim();
                Require(AptPackagePattern().IsMatch(name), $"'{name}' is not a valid Debian package name.", nameof(BuildImageSpec.AptPackages));
                result.Add(name);
            }
        }

        return result.ToList();
    }

    private static HashSet<CrossTarget> NormalizeTargets(IEnumerable<CrossTarget> targets)
    {
        var result = new HashSet<CrossTarget>();
        if (targets == null)
        {
            return result;
        }

        foreach (var target in targets)
        {
            var info = CrossTargetInfo.Get(target);
            if (!info.IsSupported)
            {
                throw new CrossTargetNotSupportedException(target);
            }

            result.Add(target);
        }

        return result;
    }

    private static string AptInstall(IEnumerable<string> packages)
        => "RUN apt-get update && apt-get install -y --no-install-recommends "
           + string.Join(" ", packages)
           + " && rm -rf /var/lib/apt/lists/*";

    /// <summary>
    /// The Windows layer: llvm-mingw (<see cref="LlvmMingwVersion"/>) for the engine's own
    /// architecture, verified against the pinned SHA-256, unpacked to <see cref="LlvmMingwHome"/>,
    /// plus both windows-gnullvm rustup targets. One layer serves x64 and ARM64 Windows.
    /// </summary>
    internal static IEnumerable<string> WindowsLayer()
    {
        yield return "# Windows x64 and ARM64 (x86_64-pc-windows-gnullvm, aarch64-pc-windows-gnullvm): llvm-mingw 20261006 - clang, lld and the";
        yield return "# mingw-w64 runtime (LLVM: Apache-2.0 with LLVM exceptions; mingw-w64: its own permissive licences; LICENSE.TXT and";
        yield return "# COPYING.* stay in /opt/llvm-mingw), downloaded from https://github.com/mstorsjo/llvm-mingw/releases for the engine's own";
        yield return "# architecture and verified against a pinned SHA-256. Output links against Windows' Universal C Runtime (Windows 10 and";
        yield return "# later). No Microsoft SDK, compiler or runtime is downloaded; CodeBrix.RustTools.Docker redistributes nothing.";
        yield return "RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl xz-utils && rm -rf /var/lib/apt/lists/* \\";
        yield return "    && case \"$(uname -m)\" in \\";
        yield return "         x86_64) url=\"https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-ubuntu-22.04-x86_64.tar.xz\"; sha=\"5f9c6ed95b2d4bdb2869a488c5fd5857fbdabcf288a0aa3eb1da43f6a08d8ab4\" ;; \\";
        yield return "         aarch64) url=\"https://github.com/mstorsjo/llvm-mingw/releases/download/20261006/llvm-mingw-20261006-ucrt-ubuntu-22.04-aarch64.tar.xz\"; sha=\"e77c4acfa44184f6b0c52b814d724ad63b2ecebb3c3f6f842c0846e717cbd23c\" ;; \\";
        yield return "         *) echo \"llvm-mingw ships no build for $(uname -m)\" >&2; exit 1 ;; \\";
        yield return "       esac \\";
        yield return "    && curl -fsSL -o /tmp/llvm-mingw.tar.xz \"$url\" \\";
        yield return "    && echo \"$sha  /tmp/llvm-mingw.tar.xz\" | sha256sum -c - \\";
        yield return "    && mkdir -p /opt/llvm-mingw && tar -xJf /tmp/llvm-mingw.tar.xz -C /opt/llvm-mingw --strip-components=1 \\";
        yield return "    && rm /tmp/llvm-mingw.tar.xz \\";
        yield return "    && rustup target add x86_64-pc-windows-gnullvm aarch64-pc-windows-gnullvm";
        yield return "ENV LLVM_MINGW_HOME=/opt/llvm-mingw";
    }

    /// <summary>
    /// The layer for a Linux target of another architecture: Debian's cross GCC, g++ and glibc
    /// for it (which also serve crates that compile C), and the rustup target.
    /// </summary>
    internal static IEnumerable<string> LinuxCrossLayer(CrossTargetInfo info)
    {
        yield return $"# Linux {info.Triple}: Debian's cross GCC, g++ and glibc as the linker and C toolchain of another Linux architecture.";
        yield return $"RUN apt-get update && apt-get install -y --no-install-recommends {string.Join(" ", info.DebianCrossPackages)} && rm -rf /var/lib/apt/lists/* \\";
        yield return $"    && rustup target add {info.Triple}";
    }

    private static IEnumerable<string> AndroidLayer()
    {
        yield return $"# Android NDK {AndroidNdkVersion} (LTS). The NDK is licensed under the Android Software Development Kit";
        yield return "# License Agreement (https://developer.android.com/studio/terms). It is downloaded from Google here, at the";
        yield return "# consumer's request; CodeBrix.RustTools.Docker does not redistribute it.";
        yield return "RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl unzip && rm -rf /var/lib/apt/lists/* \\";
        yield return $"    && curl -fsSL -o /tmp/android-ndk.zip {AndroidNdkUrl} \\";
        yield return $"    && echo \"{AndroidNdkSha1}  /tmp/android-ndk.zip\" | sha1sum -c - \\";
        yield return "    && unzip -q /tmp/android-ndk.zip -d /opt \\";
        yield return $"    && mv /opt/android-ndk-{AndroidNdkVersion} {AndroidNdkHome} \\";
        yield return "    && rm /tmp/android-ndk.zip";
        yield return $"ENV ANDROID_NDK_HOME={AndroidNdkHome}";
        yield return "RUN rustup target add aarch64-linux-android x86_64-linux-android";
    }

    /// <summary>
    /// The macOS layer: both Darwin targets and llvm-tools from rustup, then a shell script (a
    /// Dockerfile heredoc, so the image hash covers it) that derives the libSystem symbol stub
    /// from the installed Darwin standard libraries and <see cref="MacOSProbeProgram"/>, adds
    /// <see cref="MacOSStubSupplement"/>, and closes the loop by linking the probe with rust-lld.
    /// The result is <see cref="MacOSSysrootDirectory"/>; the linker is linked to
    /// <see cref="MacOSRustLldPath"/>. Requires BuildKit (Dockerfile heredocs), which the
    /// library's image builds use.
    /// </summary>
    private static IEnumerable<string> MacOSLayer()
    {
        yield return "# macOS (aarch64-apple-darwin, x86_64-apple-darwin), DEVELOPMENT AND TESTING ONLY: Rust's own Darwin standard";
        yield return "# libraries, linked by rustup's rust-lld against a libSystem symbol stub that is derived right here from those";
        yield return "# libraries, a probe program and the C math function names. Nothing from Apple is downloaded, copied or needed. The";
        yield return "# binaries carry an ad-hoc linker signature and must not be distributed or used in production; any macOS build that";
        yield return "# will be is made on a Mac (CodeBrix.RustTools can do it there, with Apple's toolchain under Apple's terms).";
        yield return "RUN rustup target add aarch64-apple-darwin x86_64-apple-darwin && rustup component add llvm-tools";
        yield return "RUN <<'CODEBRIX_MACOS_SYSROOT'";
        yield return "set -eu";
        yield return "export LC_ALL=C";
        yield return "root=/opt/codebrix/macos-sysroot";
        yield return "sysroot=\"$(rustc --print sysroot)\"";
        yield return "host=\"$(rustc -vV | sed -n 's/^host: //p')\"";
        yield return "nm=\"$sysroot/lib/rustlib/$host/bin/llvm-nm\"";
        yield return "lld=\"$sysroot/lib/rustlib/$host/bin/rust-lld\"";
        yield return "mkdir -p \"$root/usr/lib\" /opt/codebrix/bin";
        yield return "ln -sf \"$lld\" /opt/codebrix/bin/rust-lld";
        yield return "work=/tmp/codebrix-macos-sysroot";
        yield return "rm -rf \"$work\"; mkdir -p \"$work\"";
        yield return "cat > \"$work/probe.rs\" <<'CODEBRIX_MACOS_PROBE'";
        foreach (var line in MacOSProbeProgram)
        {
            yield return line;
        }

        yield return "CODEBRIX_MACOS_PROBE";
        yield return "# 1. For each target: the symbols Rust's precompiled rlibs leave undefined, minus the ones they define, plus what";
        yield return "#    the probe object needs (float and memory operations are inlined into the compiled crate and lowered by LLVM to";
        yield return "#    C library calls there, so only a compiled program shows them). What remains is what the system library must";
        yield return "#    provide. Linker-provided section bounds and the allocator shim rustc generates at link time are dropped. Then";
        yield return "#    the ISO C math function names and the Darwin library calls LLVM can emit are added, so any crate reaches them.";
        yield return "for triple in aarch64-apple-darwin x86_64-apple-darwin; do";
        yield return "  libdir=\"$sysroot/lib/rustlib/$triple/lib\"";
        yield return "  : > \"$work/undef-$triple\"; : > \"$work/def-$triple\"";
        yield return "  for rlib in \"$libdir\"/*.rlib; do";
        yield return "    \"$nm\" --undefined-only --format=just-symbols \"$rlib\" >> \"$work/undef-$triple\" 2>/dev/null || true";
        yield return "    \"$nm\" --defined-only --extern-only --format=just-symbols \"$rlib\" >> \"$work/def-$triple\" 2>/dev/null || true";
        yield return "  done";
        yield return "  rustc --target \"$triple\" -O --emit=obj -o \"$work/probe-$triple.o\" \"$work/probe.rs\"";
        yield return "  \"$nm\" --undefined-only --format=just-symbols \"$work/probe-$triple.o\" >> \"$work/undef-$triple\"";
        yield return "  sort -u \"$work/undef-$triple\" -o \"$work/undef-$triple\"";
        yield return "  sort -u \"$work/def-$triple\" -o \"$work/def-$triple\"";
        yield return "  comm -23 \"$work/undef-$triple\" \"$work/def-$triple\" \\";
        yield return "    | grep -v -E '^(__mh_execute_header|___dso_handle)$' \\";
        yield return "    | grep -v -E '^(section|segment)\\$(start|end)\\$' \\";
        yield return "    | grep -v -E '__rustc|___rust_(alloc|dealloc|realloc|alloc_zeroed|no_alloc_shim)' \\";
        yield return "    > \"$root/symbols-$triple.txt\"";
        yield return "  printf '%s\\n' \\";
        for (var i = 0; i < MacOSStubSupplement.Count; i += 10)
        {
            yield return "    " + string.Join(" ", MacOSStubSupplement.Skip(i).Take(10)) + " \\";
        }

        yield return "    >> \"$root/symbols-$triple.txt\"";
        yield return "  sort -u \"$root/symbols-$triple.txt\" -o \"$root/symbols-$triple.txt\"";
        yield return "done";
        yield return "# 2. The stub: a TAPI text file naming the install name and the symbols, nothing else; libc, libm, libpthread";
        yield return "#    and libdl are the same stub under the names rustc links with.";
        yield return "write_stub() {";
        yield return "  a=\"$root/symbols-aarch64-apple-darwin.txt\"; x=\"$root/symbols-x86_64-apple-darwin.txt\"";
        yield return "  common=\"$(comm -12 \"$a\" \"$x\" | paste -sd, - | sed 's/,/, /g')\"";
        yield return "  armonly=\"$(comm -23 \"$a\" \"$x\" | paste -sd, - | sed 's/,/, /g')\"";
        yield return "  x64only=\"$(comm -13 \"$a\" \"$x\" | paste -sd, - | sed 's/,/, /g')\"";
        yield return "  {";
        yield return "    echo '--- !tapi-tbd'";
        yield return "    echo 'tbd-version:     4'";
        yield return "    echo 'targets:         [ arm64-macos, x86_64-macos ]'";
        yield return "    echo \"install-name:    '/usr/lib/libSystem.B.dylib'\"";
        yield return "    echo 'current-version: 1'";
        yield return "    echo 'compatibility-version: 1'";
        yield return "    echo 'exports:'";
        yield return "    echo '  - targets:     [ arm64-macos, x86_64-macos ]'";
        yield return "    echo \"    symbols:     [ $common ]\"";
        yield return "    [ -n \"$armonly\" ] && { echo '  - targets:     [ arm64-macos ]'; echo \"    symbols:     [ $armonly ]\"; }";
        yield return "    [ -n \"$x64only\" ] && { echo '  - targets:     [ x86_64-macos ]'; echo \"    symbols:     [ $x64only ]\"; }";
        yield return "    echo '...'";
        yield return "  } > \"$root/usr/lib/libSystem.tbd\"";
        yield return "  for alias in libc libm libpthread libdl; do cp \"$root/usr/lib/libSystem.tbd\" \"$root/usr/lib/$alias.tbd\"; done";
        yield return "}";
        yield return "# 3. Close the loop: link the probe against the stub and add whatever the linker still reports as";
        yield return "#    undefined (symbols that only appear at link time: dyld_stub_binder, __tlv_bootstrap, _memmove).";
        yield return "for triple in aarch64-apple-darwin x86_64-apple-darwin; do";
        yield return "  round=0";
        yield return "  while :; do";
        yield return "    write_stub";
        yield return "    if SDKROOT=\"$root\" rustc --target \"$triple\" -O -C linker=\"$lld\" -C linker-flavor=ld64.lld -C link-arg=-S \\";
        yield return "         -o \"$work/probe-$triple\" \"$work/probe.rs\" > \"$work/link-$triple.log\" 2>&1; then break; fi";
        yield return "    missing=\"$(grep -o 'undefined symbol: [^ ]*' \"$work/link-$triple.log\" | awk '{print $3}' | sort -u)\"";
        yield return "    round=$((round + 1))";
        yield return "    if [ -z \"$missing\" ] || [ \"$round\" -gt 6 ]; then echo \"macOS sysroot: $triple does not link:\"; cat \"$work/link-$triple.log\"; exit 1; fi";
        yield return "    printf '%s\\n' \"$missing\" >> \"$root/symbols-$triple.txt\"";
        yield return "    sort -u \"$root/symbols-$triple.txt\" -o \"$root/symbols-$triple.txt\"";
        yield return "  done";
        yield return "done";
        yield return "write_stub";
        yield return "{";
        yield return "  echo 'Generated by CodeBrix.RustTools.Docker when this image was built.'";
        yield return "  echo 'usr/lib/libSystem.tbd lists the symbol names Rust'\"'\"'s standard library imports from /usr/lib/libSystem.B.dylib,'";
        yield return "  echo 'derived from the rustup-installed Darwin targets of this image'\"'\"'s toolchain, plus the ISO C math functions and the'";
        yield return "  echo 'Darwin library calls LLVM emits for float and memory operations (names only); symbols-<triple>.txt are the lists.'";
        yield return "  echo 'Nothing in this folder comes from Apple. Binaries linked against it are for development and testing only'";
        yield return "  echo 'and must not be distributed or used in production.'";
        yield return "  echo \"toolchain: $(rustc -vV | sed -n 's/^release: //p') ($(rustc -vV | sed -n 's/^commit-hash: //p'))\"";
        yield return "} > \"$root/README.txt\"";
        yield return "rm -rf \"$work\"";
        yield return "CODEBRIX_MACOS_SYSROOT";
    }

    private static void Require(bool condition, string message, string parameterName)
    {
        if (!condition)
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._/:-]*$")]
    private static partial Regex ImageNamePattern();

    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex RustVersionPattern();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9+.-]*(:[a-z0-9-]+)?(=[A-Za-z0-9.+:~-]+)?$")]
    private static partial Regex AptPackagePattern();
}
