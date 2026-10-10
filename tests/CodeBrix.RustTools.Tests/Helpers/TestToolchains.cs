using System.Collections.Generic;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Tests.Helpers;

/// <summary>
/// Builds synthetic RustToolchainInfo values for readiness, guidance and update tests.
/// </summary>
internal static class TestToolchains
{
    public static InstalledTool Installed(RustTool tool, string version)
        => new InstalledTool { Tool = tool, Status = ToolStatus.Installed, Version = ToolVersion.Parse(version), ExecutablePath = "/fake/" + tool };

    /// <summary>A complete, rustup-managed stable 1.94.1 environment on the given OS.</summary>
    public static Dictionary<RustTool, InstalledTool> CompleteTools() => new()
    {
        [RustTool.Rustup] = Installed(RustTool.Rustup, "rustup 1.29.0 (28d1352db 2026-03-05)"),
        [RustTool.Cargo] = Installed(RustTool.Cargo, "cargo 1.94.1 (29ea6fb6a 2026-03-24)"),
        [RustTool.Rustc] = Installed(RustTool.Rustc, "rustc 1.94.1 (e408947bf 2026-03-25)"),
        [RustTool.Clippy] = Installed(RustTool.Clippy, "clippy 0.1.94 (e408947bfd 2026-03-25)"),
        [RustTool.Rustfmt] = Installed(RustTool.Rustfmt, "rustfmt 1.8.0-stable (e408947bfd 2026-03-25)"),
        [RustTool.Rustdoc] = Installed(RustTool.Rustdoc, "rustdoc 1.94.1 (e408947bf 2026-03-25)"),
        [RustTool.CCompiler] = Installed(RustTool.CCompiler, "cc (Debian 14.2.0-19) 14.2.0"),
        [RustTool.Linker] = Installed(RustTool.Linker, "cc (Debian 14.2.0-19) 14.2.0"),
    };

    public static RustToolchainInfo Create(OperatingSystemKind os, Dictionary<RustTool, InstalledTool> tools, RustToolchainFile toolchainFile = null)
        => new RustToolchainInfo
        {
            OperatingSystem = os,
            Tools = tools,
            HostTriple = "x86_64-unknown-linux-gnu",
            ActiveToolchain = "stable-x86_64-unknown-linux-gnu",
            InstalledToolchains = new[] { "stable-x86_64-unknown-linux-gnu" },
            InstalledTargets = new[] { "x86_64-unknown-linux-gnu" },
            ToolchainFile = toolchainFile,
        };

    /// <summary>A complete, rustup-managed Debian environment on <paramref name="hostTriple"/>
    /// with the host target and <paramref name="otherTargets"/> installed.</summary>
    public static RustToolchainInfo WithTargets(string hostTriple, params string[] otherTargets)
    {
        var targets = new List<string> { hostTriple };
        targets.AddRange(otherTargets);
        return new RustToolchainInfo
        {
            OperatingSystem = OperatingSystemKind.DebianLinux,
            Tools = CompleteTools(),
            HostTriple = hostTriple,
            ActiveToolchain = "stable-" + hostTriple,
            InstalledToolchains = new[] { "stable-" + hostTriple },
            InstalledTargets = targets,
        };
    }

    public static RustToolchainInfo Complete(OperatingSystemKind os = OperatingSystemKind.DebianLinux)
        => Create(os, CompleteTools());

    public static RustToolchainInfo With(OperatingSystemKind os, params InstalledTool[] replacements)
    {
        var tools = CompleteTools();
        foreach (var replacement in replacements)
        {
            tools[replacement.Tool] = replacement;
        }

        return Create(os, tools);
    }
}
