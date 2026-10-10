using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// What a build needs from the toolchain. <see cref="ToolchainReadiness"/> checks a detected
/// environment against these requirements.
/// </summary>
public sealed class ToolchainRequirements
{
    /// <summary>The oldest acceptable rustc/cargo version, or <c>null</c> (the default) to accept
    /// any version. A project's <c>rust-version</c> (MSRV) is the usual source.</summary>
    public ToolVersion MinimumRustVersion { get; set; }

    /// <summary>True (the default) when the C toolchain (<see cref="RustTool.CCompiler"/>) must be
    /// present: <c>cc</c> on Linux, the Xcode Command Line Tools on macOS, the MSVC build tools
    /// (or <c>gcc</c> on a <c>windows-gnu</c> host) on Windows. Every Rust executable needs the
    /// system linker, so only turn this off for check-only workflows. A missing C toolchain is
    /// blocking; one whose status could not be determined is a non-blocking
    /// <see cref="ReadinessIssueKind.ToolStatusUnknown"/> issue.</summary>
    public bool RequireCCompiler { get; set; } = true;

    /// <summary>True when clippy must be installed (for <c>cargo clippy</c>). Default false.</summary>
    public bool RequireClippy { get; set; }

    /// <summary>True when rustfmt must be installed (for <c>cargo fmt</c>). Default false.</summary>
    public bool RequireRustfmt { get; set; }

    /// <summary>True when rustup itself must be present (for example to manage targets).
    /// Default false: an operating-system-packaged Rust is accepted.</summary>
    public bool RequireRustup { get; set; }

    /// <summary>True (the default) when the toolchain named by the project's
    /// <c>rust-toolchain.toml</c> must already be installed.</summary>
    public bool RequireToolchainFileChannel { get; set; } = true;

    /// <summary>Compilation target triples that must be installed (for cross-compiling).
    /// Empty by default.</summary>
    public IList<string> RequiredTargets { get; } = new List<string>();

    /// <summary>True when an Android NDK must have been found
    /// (<see cref="RustToolchainInfo.AndroidNdk"/>), for building Android targets with
    /// <c>AndroidLinkProfile</c>. Default false.</summary>
    public bool RequireAndroidNdk { get; set; }

    /// <summary>The GNU cross tools that must be present, keyed by Linux target triple: for
    /// each entry the profile's <see cref="LinuxLinkProfile.CompilerPath"/> and
    /// <see cref="LinuxLinkProfile.ArchiverPath"/> must exist on PATH (or in the profile's
    /// <see cref="LinuxLinkProfile.BinDirectory"/>). A missing tool is a blocking
    /// <see cref="ReadinessIssueKind.LinuxCrossToolMissing"/> issue. Empty by default.</summary>
    public IDictionary<string, LinuxLinkProfile> RequiredLinuxCrossTools { get; } = new Dictionary<string, LinuxLinkProfile>(StringComparer.Ordinal);

    /// <summary>
    /// Requirements for building the two 64-bit Android targets: the default tools, the
    /// <c>aarch64-linux-android</c> and <c>x86_64-linux-android</c> standard libraries, and an
    /// NDK.
    /// </summary>
    public static ToolchainRequirements ForAndroid()
    {
        var requirements = new ToolchainRequirements { RequireAndroidNdk = true };
        requirements.RequiredTargets.Add("aarch64-linux-android");
        requirements.RequiredTargets.Add("x86_64-linux-android");
        return requirements;
    }

    /// <summary>
    /// Requirements for building Linux targets of another processor architecture with the GNU
    /// cross toolchain: the default tools, each target's standard library, and each target's
    /// cross <c>gcc</c> and <c>ar</c> (<see cref="LinuxLinkProfile.ForTriple"/>).
    /// </summary>
    /// <param name="triples">Linux target triples (<see cref="LinuxLinkProfile.Triples"/>).</param>
    /// <param name="binDirectory">The folder that holds the cross tools, or <c>null</c> (the
    /// default) when they are on PATH.</param>
    /// <exception cref="ArgumentNullException"><paramref name="triples"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentException">A triple is not a Linux target triple.</exception>
    public static ToolchainRequirements ForLinuxCross(IEnumerable<string> triples, string binDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(triples);
        var requirements = new ToolchainRequirements();
        foreach (var triple in triples)
        {
            var normalized = (triple ?? string.Empty).Trim();
            var profile = LinuxLinkProfile.ForTriple(normalized, binDirectory);
            if (!requirements.RequiredTargets.Contains(normalized))
            {
                requirements.RequiredTargets.Add(normalized);
            }

            requirements.RequiredLinuxCrossTools[normalized] = profile;
        }

        return requirements;
    }

    /// <summary>
    /// The default requirements: cargo, rustc and a C toolchain of any version.
    /// </summary>
    public static ToolchainRequirements Default => new ToolchainRequirements();

    /// <summary>
    /// Requirements for running <c>cargo clippy</c> and <c>cargo fmt --check</c> as well as
    /// building.
    /// </summary>
    public static ToolchainRequirements WithLintAndFormat()
        => new ToolchainRequirements { RequireClippy = true, RequireRustfmt = true };
}
