using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Everything <see cref="RustToolchainDetector"/> learned about the Rust build environment on
/// this machine (optionally as seen from one project folder, so that a
/// <c>rust-toolchain.toml</c> override is honoured).
/// </summary>
public sealed class RustToolchainInfo
{
    private IReadOnlyDictionary<RustTool, InstalledTool> _tools = new Dictionary<RustTool, InstalledTool>();

    /// <summary>The operating-system family, used to choose install guidance.</summary>
    public OperatingSystemKind OperatingSystem { get; init; } = OperatingSystemKind.Unknown;

    /// <summary>A human-readable operating-system name (for example the Linux distribution's
    /// pretty name).</summary>
    public string OperatingSystemDescription { get; init; }

    /// <summary>The processor architecture of the machine (<c>X64</c>, <c>Arm64</c>, ...).</summary>
    public string Architecture { get; init; }

    /// <summary>Detection results keyed by tool; every <see cref="RustTool"/> value has an
    /// entry (tools that were not probed are <see cref="ToolStatus.Unknown"/>).</summary>
    public IReadOnlyDictionary<RustTool, InstalledTool> Tools
    {
        get => _tools;
        init => _tools = value ?? new Dictionary<RustTool, InstalledTool>();
    }

    /// <summary>The host target triple reported by <c>rustc -vV</c> (for example
    /// <c>x86_64-unknown-linux-gnu</c>), or <c>null</c>.</summary>
    public string HostTriple { get; init; }

    /// <summary>The LLVM version rustc was built with, or <c>null</c>.</summary>
    public string LlvmVersion { get; init; }

    /// <summary>The toolchain's sysroot folder, from <c>rustc --print sysroot</c> (for example
    /// <c>~/.rustup/toolchains/stable-aarch64-apple-darwin</c>): the folder that holds the
    /// precompiled standard libraries under <c>lib/rustlib/&lt;triple&gt;/lib</c> and rustup's
    /// own tools, such as <c>rust-lld</c>, under <c>lib/rustlib/&lt;host&gt;/bin</c>. <c>null</c>
    /// when rustc is missing or the probe failed.</summary>
    public string Sysroot { get; init; }

    /// <summary>The full rustc commit hash, or <c>null</c>.</summary>
    public string RustcCommitHash { get; init; }

    /// <summary>The name of the toolchain rustup selects in the probed folder (for example
    /// <c>stable-x86_64-unknown-linux-gnu</c>), or <c>null</c> without rustup.</summary>
    public string ActiveToolchain { get; init; }

    /// <summary>Why rustup selected <see cref="ActiveToolchain"/> (for example
    /// <c>it's the default toolchain</c> or <c>overridden by '/path/rust-toolchain.toml'</c>),
    /// or <c>null</c>.</summary>
    public string ActiveToolchainReason { get; init; }

    /// <summary>The rustup default toolchain, or <c>null</c>.</summary>
    public string DefaultToolchain { get; init; }

    /// <summary>The channel of the compiler in use: from the active toolchain name when rustup
    /// is present, otherwise from the rustc version.</summary>
    public RustChannel Channel { get; init; } = RustChannel.Unknown;

    /// <summary>The toolchains rustup has installed (empty without rustup).</summary>
    public IReadOnlyList<string> InstalledToolchains { get; init; } = Array.Empty<string>();

    /// <summary>The compilation targets installed for the active toolchain (empty without
    /// rustup).</summary>
    public IReadOnlyList<string> InstalledTargets { get; init; } = Array.Empty<string>();

    /// <summary>The components installed for the active toolchain, as rustup names them (for
    /// example <c>clippy-x86_64-unknown-linux-gnu</c>, <c>rust-src</c>).</summary>
    public IReadOnlyList<string> InstalledComponents { get; init; } = Array.Empty<string>();

    /// <summary>The Android NDK found on this machine (<see cref="AndroidNdkLocator"/>), or
    /// <c>null</c> when none was found or Google ships no NDK host tools for this machine.</summary>
    public AndroidNdk AndroidNdk { get; init; }

    /// <summary>The cargo home folder in effect (<c>CARGO_HOME</c> or <c>~/.cargo</c>).</summary>
    public string CargoHome { get; init; }

    /// <summary>The rustup home folder in effect, or <c>null</c> without rustup.</summary>
    public string RustupHome { get; init; }

    /// <summary>The toolchain override file that applies to <see cref="ProjectDirectory"/>, or
    /// <c>null</c>.</summary>
    public RustToolchainFile ToolchainFile { get; init; }

    /// <summary>The folder detection ran in, or <c>null</c> when no project folder was given.</summary>
    public string ProjectDirectory { get; init; }

    /// <summary>When detection ran.</summary>
    public DateTimeOffset DetectedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Non-fatal problems met while probing (a probe that timed out, output that could
    /// not be parsed, ...).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>The cargo entry.</summary>
    public InstalledTool Cargo => GetTool(RustTool.Cargo);

    /// <summary>The rustc entry.</summary>
    public InstalledTool Rustc => GetTool(RustTool.Rustc);

    /// <summary>The rustup entry.</summary>
    public InstalledTool Rustup => GetTool(RustTool.Rustup);

    /// <summary>The clippy entry.</summary>
    public InstalledTool Clippy => GetTool(RustTool.Clippy);

    /// <summary>The rustfmt entry.</summary>
    public InstalledTool Rustfmt => GetTool(RustTool.Rustfmt);

    /// <summary>The rustdoc entry.</summary>
    public InstalledTool Rustdoc => GetTool(RustTool.Rustdoc);

    /// <summary>The C compiler entry.</summary>
    public InstalledTool CCompiler => GetTool(RustTool.CCompiler);

    /// <summary>The linker entry.</summary>
    public InstalledTool Linker => GetTool(RustTool.Linker);

    /// <summary>True when rustup is available, so toolchains, components and targets can be
    /// managed with rustup commands.</summary>
    public bool IsRustupManaged => Rustup.IsAvailable;

    /// <summary>True when both cargo and rustc are available.</summary>
    public bool HasCargoAndRustc => Cargo.IsAvailable && Rustc.IsAvailable;

    /// <summary>
    /// Returns the entry for a tool; a tool with no entry is reported as
    /// <see cref="ToolStatus.Unknown"/> ("not probed").
    /// </summary>
    /// <param name="tool">The tool to look up.</param>
    public InstalledTool GetTool(RustTool tool)
        => _tools.TryGetValue(tool, out var installed) && installed != null
            ? installed
            : InstalledTool.Unknown(tool, "Not probed.");

    /// <summary>
    /// A multi-line, human-readable report of the detected environment.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder();
        builder.Append("Operating system: ").Append(OperatingSystem);
        if (!string.IsNullOrEmpty(OperatingSystemDescription))
        {
            builder.Append(" (").Append(OperatingSystemDescription).Append(')');
        }

        builder.AppendLine();
        if (!string.IsNullOrEmpty(Sysroot))
        {
            builder.Append("Sysroot: ").AppendLine(Sysroot);
        }

        if (AndroidNdk != null)
        {
            builder.Append("Android NDK: ").AppendLine(AndroidNdk.ToString());
        }

        if (!string.IsNullOrEmpty(HostTriple))
        {
            builder.Append("Host triple: ").AppendLine(HostTriple);
        }

        if (!string.IsNullOrEmpty(ActiveToolchain))
        {
            builder.Append("Active toolchain: ").Append(ActiveToolchain);
            if (!string.IsNullOrEmpty(ActiveToolchainReason))
            {
                builder.Append(" (").Append(ActiveToolchainReason).Append(')');
            }

            builder.AppendLine();
        }

        foreach (var tool in Enum.GetValues<RustTool>())
        {
            builder.AppendLine(GetTool(tool).ToString());
        }

        if (InstalledTargets.Count > 0)
        {
            builder.Append("Installed targets: ").AppendLine(string.Join(", ", InstalledTargets));
        }

        if (ToolchainFile != null)
        {
            builder.Append("Toolchain file: ").Append(ToolchainFile.FilePath).Append(" (channel ")
                .Append(ToolchainFile.Channel ?? "custom path").AppendLine(")");
        }

        foreach (var warning in Warnings.Where(w => !string.IsNullOrEmpty(w)))
        {
            builder.Append("Warning: ").AppendLine(warning);
        }

        return builder.ToString().TrimEnd();
    }
}
