using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.Toolchain;

/// <summary>
/// Produces operating-system-specific install and update instructions. Everything it returns
/// is data for the consuming application to show; nothing is ever executed.
/// </summary>
public static class InstallGuidanceProvider
{
    /// <summary>The official Rust installation page.</summary>
    public const string RustInstallUrl = "https://www.rust-lang.org/tools/install";

    private const string RustupUnixCommand = "curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh";
    private const string CargoEnvCommand = ". \"$HOME/.cargo/env\"";
    private const string RustupUpdateUrl = "https://rust-lang.github.io/rustup/basics.html#keeping-rust-up-to-date";
    private const string RustupComponentsUrl = "https://rust-lang.github.io/rustup/concepts/components.html";
    private const string RustupTargetsUrl = "https://rust-lang.github.io/rustup/cross-compilation.html";
    private const string RustupToolchainsUrl = "https://rust-lang.github.io/rustup/overrides.html#the-toolchain-file";
    private const string WindowsMsvcUrl = "https://rust-lang.github.io/rustup/installation/windows-msvc.html";

    /// <summary>
    /// Returns the guidance that resolves every blocking issue in <paramref name="readiness"/>,
    /// in the order the steps should be done (C toolchain first, then Rust, then components,
    /// targets and toolchains). Returns an empty list when the environment is ready.
    /// </summary>
    /// <param name="readiness">The evaluation to resolve.</param>
    /// <exception cref="ArgumentNullException"><paramref name="readiness"/> is null.</exception>
    public static IReadOnlyList<InstallGuidance> ForReadiness(ToolchainReadiness readiness)
    {
        ArgumentNullException.ThrowIfNull(readiness);
        var os = readiness.OperatingSystem;
        var rustupManaged = readiness.Toolchain.IsRustupManaged;
        var blocking = readiness.Issues.Where(i => i.IsBlocking).ToList();
        var result = new List<InstallGuidance>();

        bool Missing(RustTool tool) => blocking.Any(i => i.Kind == ReadinessIssueKind.ToolMissing && i.Tool == tool);

        if (Missing(RustTool.CCompiler))
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallCToolchain, os));
        }

        var rustMissing = Missing(RustTool.Cargo) || Missing(RustTool.Rustc);
        if (rustMissing)
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallRust, os));
        }
        else if (Missing(RustTool.Rustup))
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallRustup, os));
        }

        if (!rustMissing && blocking.Any(i => i.Kind == ReadinessIssueKind.ToolOutdated))
        {
            result.Add(ForUpdate(os, rustupManaged));
        }

        var components = new List<string>();
        if (Missing(RustTool.Clippy))
        {
            components.Add("clippy");
        }

        if (Missing(RustTool.Rustfmt))
        {
            components.Add("rustfmt");
        }

        if (!rustMissing && components.Count > 0)
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallComponents, os, components, rustupManaged));
        }

        var targets = readiness.MissingTargets;
        if (!rustMissing && targets.Count > 0)
        {
            result.Add(rustupManaged
                ? GetGuidance(InstallGuidanceTopic.AddTargets, os, targets)
                : GetGuidance(InstallGuidanceTopic.InstallRustup, os));
        }

        var toolchains = blocking.Where(i => i.Kind == ReadinessIssueKind.ToolchainNotInstalled && i.Subject != null).Select(i => i.Subject).Distinct().ToList();
        if (!rustMissing && toolchains.Count > 0)
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallToolchain, os, toolchains));
        }

        if (readiness.AndroidNdkMissing)
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallAndroidNdk, os));
        }

        var crossTargets = readiness.MissingLinuxCrossTargets;
        if (crossTargets.Count > 0)
        {
            result.Add(GetGuidance(InstallGuidanceTopic.InstallLinuxCrossTools, os, crossTargets));
        }

        return result
            .GroupBy(g => g.Topic)
            .Select(g => g.First())
            .ToList();
    }

    /// <summary>
    /// Returns the guidance for bringing Rust up to date: <c>rustup update</c> when rustup
    /// manages the installation, otherwise how to switch to rustup.
    /// </summary>
    /// <param name="operatingSystem">The operating-system family to write commands for.</param>
    /// <param name="rustupManaged">True when rustup is installed.</param>
    public static InstallGuidance ForUpdate(OperatingSystemKind operatingSystem, bool rustupManaged)
        => GetGuidance(rustupManaged ? InstallGuidanceTopic.UpdateRust : InstallGuidanceTopic.InstallRustup, operatingSystem);

    /// <summary>
    /// Returns the guidance for one topic on one operating-system family.
    /// </summary>
    /// <param name="topic">What the guidance should achieve.</param>
    /// <param name="operatingSystem">The operating-system family to write commands for.</param>
    /// <param name="items">The components, targets or toolchain names the topic applies to
    /// (used by <see cref="InstallGuidanceTopic.InstallComponents"/>,
    /// <see cref="InstallGuidanceTopic.AddTargets"/>,
    /// <see cref="InstallGuidanceTopic.InstallToolchain"/> and
    /// <see cref="InstallGuidanceTopic.InstallLinuxCrossTools"/> (Linux target triples; others
    /// are ignored); defaults apply when empty).</param>
    /// <param name="rustupManaged">For <see cref="InstallGuidanceTopic.InstallComponents"/>:
    /// false gives the operating-system package commands where they exist.</param>
    public static InstallGuidance GetGuidance(InstallGuidanceTopic topic, OperatingSystemKind operatingSystem, IEnumerable<string> items = null, bool rustupManaged = true)
    {
        var list = items?.Where(i => !string.IsNullOrWhiteSpace(i)).Select(i => i.Trim()).Distinct(StringComparer.Ordinal).ToList() ?? new List<string>();
        return topic switch
        {
            InstallGuidanceTopic.InstallRust => InstallRust(operatingSystem),
            InstallGuidanceTopic.UpdateRust => UpdateRust(operatingSystem),
            InstallGuidanceTopic.InstallRustup => InstallRustup(operatingSystem),
            InstallGuidanceTopic.InstallComponents => InstallComponents(operatingSystem, list.Count == 0 ? new List<string> { "clippy", "rustfmt" } : list, rustupManaged),
            InstallGuidanceTopic.InstallCToolchain => InstallCToolchain(operatingSystem),
            InstallGuidanceTopic.AddTargets => AddTargets(operatingSystem, list),
            InstallGuidanceTopic.InstallToolchain => InstallToolchain(operatingSystem, list.Count == 0 ? new List<string> { "stable" } : list),
            InstallGuidanceTopic.InstallAndroidNdk => InstallAndroidNdk(operatingSystem),
            InstallGuidanceTopic.InstallLinuxCrossTools => InstallLinuxCrossTools(operatingSystem, list),
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Unknown guidance topic."),
        };
    }

    private static InstallGuidance InstallRust(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallRust,
            OperatingSystem = os,
            Title = "Install the Rust toolchain with rustup",
            Explanation = "Rust is installed and kept current by rustup. Install it with winget (or download and run rustup-init.exe from https://rustup.rs), then open a new terminal. The default MSVC toolchain also needs the Microsoft C++ build tools.",
            Commands = new[] { "winget install --id Rustlang.Rustup -e", "rustup default stable" },
            DocumentationUrl = RustInstallUrl,
            RequiresNewShell = true,
        },
        OperatingSystemKind.DebianLinux => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallRust,
            OperatingSystem = os,
            Title = "Install the Rust toolchain with rustup",
            Explanation = "Install curl, then run the official rustup installer and accept the default installation. The installer adds ~/.cargo/bin to PATH for new shells; the last command loads it into the current shell. Prefer rustup over the apt rustc/cargo packages, which lag behind the current release.",
            Commands = new[] { "sudo apt update", "sudo apt install curl", RustupUnixCommand, CargoEnvCommand },
            DocumentationUrl = RustInstallUrl,
            RequiresAdministrator = true,
            RequiresNewShell = true,
        },
        OperatingSystemKind.MacOS => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallRust,
            OperatingSystem = os,
            Title = "Install the Rust toolchain with rustup",
            Explanation = "Run the official rustup installer and accept the default installation. The installer adds ~/.cargo/bin to PATH for new shells; the last command loads it into the current shell.",
            Commands = new[] { RustupUnixCommand, CargoEnvCommand },
            DocumentationUrl = RustInstallUrl,
            RequiresNewShell = true,
        },
        _ => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallRust,
            OperatingSystem = os,
            Title = "Install the Rust toolchain with rustup",
            Explanation = "Make sure curl is installed (with your distribution's package manager), then run the official rustup installer and accept the default installation. The last command loads ~/.cargo/bin into the current shell.",
            Commands = new[] { RustupUnixCommand, CargoEnvCommand },
            DocumentationUrl = RustInstallUrl,
            RequiresNewShell = true,
        },
    };

    private static InstallGuidance UpdateRust(OperatingSystemKind os) => new InstallGuidance
    {
        Topic = InstallGuidanceTopic.UpdateRust,
        OperatingSystem = os,
        Title = "Update Rust with rustup",
        Explanation = "rustup updates every installed toolchain (and, by default, itself) to the latest release.",
        Commands = new[] { "rustup update" },
        DocumentationUrl = RustupUpdateUrl,
    };

    private static InstallGuidance InstallRustup(OperatingSystemKind os)
    {
        var explanation = "This Rust installation is not managed by rustup, so it cannot be updated, extended with components or targets, or switched between toolchains independently. Remove the operating-system-packaged Rust and install rustup instead.";
        var commands = os switch
        {
            OperatingSystemKind.Windows => new[] { "winget install --id Rustlang.Rustup -e", "rustup default stable" },
            OperatingSystemKind.DebianLinux => new[] { "sudo apt remove rustc cargo", "sudo apt install curl", RustupUnixCommand, CargoEnvCommand },
            OperatingSystemKind.MacOS => new[] { "# If Rust was installed with Homebrew:", "brew uninstall rust", RustupUnixCommand, CargoEnvCommand },
            _ => new[] { "# Remove your distribution's rust/cargo packages first, then:", RustupUnixCommand, CargoEnvCommand },
        };
        return new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallRustup,
            OperatingSystem = os,
            Title = "Switch to a rustup-managed Rust installation",
            Explanation = explanation,
            Commands = commands,
            DocumentationUrl = RustInstallUrl,
            RequiresAdministrator = os == OperatingSystemKind.DebianLinux,
            RequiresNewShell = true,
        };
    }

    private static InstallGuidance InstallComponents(OperatingSystemKind os, IReadOnlyList<string> components, bool rustupManaged)
    {
        if (!rustupManaged && os == OperatingSystemKind.DebianLinux)
        {
            var packages = components.Select(c => c == "clippy" ? "rust-clippy" : c).ToList();
            return new InstallGuidance
            {
                Topic = InstallGuidanceTopic.InstallComponents,
                OperatingSystem = os,
                Title = "Install the missing Rust components with apt",
                Explanation = "This Rust comes from apt, so its components are apt packages too. A rustup-managed installation is recommended instead.",
                Commands = new[] { "sudo apt install " + string.Join(" ", packages) },
                DocumentationUrl = RustupComponentsUrl,
                RequiresAdministrator = true,
            };
        }

        return new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallComponents,
            OperatingSystem = os,
            Title = "Install the missing Rust components",
            Explanation = "clippy and rustfmt are rustup components; add them to the active toolchain.",
            Commands = new[] { "rustup component add " + string.Join(" ", components) },
            DocumentationUrl = RustupComponentsUrl,
        };
    }

    private static InstallGuidance InstallCToolchain(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallCToolchain,
            OperatingSystem = os,
            Title = "Install the Microsoft C++ build tools",
            Explanation = "Rust's default Windows toolchain links with the Microsoft C++ linker. Install the Visual Studio Build Tools (or Visual Studio) with the \"Desktop development with C++\" workload, which includes the MSVC compiler, linker and a Windows SDK.",
            Commands = new[] { "winget install --id Microsoft.VisualStudio.2022.BuildTools -e --override \"--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended\"" },
            DocumentationUrl = WindowsMsvcUrl,
            RequiresAdministrator = true,
        },
        OperatingSystemKind.MacOS => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallCToolchain,
            OperatingSystem = os,
            Title = "Install the Xcode Command Line Tools",
            Explanation = "rustc links through Apple's C compiler and linker, which come with the Xcode Command Line Tools. A dialog asks for confirmation.",
            Commands = new[] { "xcode-select --install" },
            DocumentationUrl = RustInstallUrl,
        },
        OperatingSystemKind.DebianLinux => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallCToolchain,
            OperatingSystem = os,
            Title = "Install a C compiler and linker",
            Explanation = "rustc links through the system C compiler (cc). build-essential provides gcc, the linker and make; pkg-config lets build scripts find system libraries.",
            Commands = new[] { "sudo apt update", "sudo apt install build-essential pkg-config" },
            DocumentationUrl = RustInstallUrl,
            RequiresAdministrator = true,
        },
        OperatingSystemKind.OtherLinux => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallCToolchain,
            OperatingSystem = os,
            Title = "Install a C compiler and linker",
            Explanation = "rustc links through the system C compiler (cc). Run the command for your distribution family.",
            Commands = new[]
            {
                "# Fedora / RHEL / CentOS Stream:",
                "sudo dnf install gcc make pkgconf-pkg-config",
                "# Arch Linux:",
                "sudo pacman -S --needed base-devel",
                "# openSUSE:",
                "sudo zypper install gcc make pkg-config",
            },
            DocumentationUrl = RustInstallUrl,
            RequiresAdministrator = true,
        },
        _ => new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallCToolchain,
            OperatingSystem = os,
            Title = "Install a C compiler and linker",
            Explanation = "rustc needs a C compiler driver available as 'cc' (gcc or clang) to link executables. Install one with your system's package manager, then confirm it runs.",
            Commands = new[] { "cc --version" },
            DocumentationUrl = RustInstallUrl,
        },
    };

    private static InstallGuidance AddTargets(OperatingSystemKind os, IReadOnlyList<string> targets) => new InstallGuidance
    {
        Topic = InstallGuidanceTopic.AddTargets,
        OperatingSystem = os,
        Title = "Add the missing compilation targets",
        Explanation = "Cross-compiling needs the standard library for each target; rustup downloads it. Some targets also need a matching linker.",
        Commands = targets.Count == 0
            ? new[] { "rustup target list --installed" }
            : new[] { "rustup target add " + string.Join(" ", targets) },
        DocumentationUrl = RustupTargetsUrl,
    };

    private static InstallGuidance InstallAndroidNdk(OperatingSystemKind os)
    {
        var sdkmanager = os == OperatingSystemKind.Windows ? "sdkmanager.bat" : "sdkmanager";
        var export = os == OperatingSystemKind.Windows
            ? "setx ANDROID_NDK_HOME \"%LOCALAPPDATA%\\Android\\Sdk\\ndk\\<version>\""
            : os == OperatingSystemKind.MacOS
                ? "export ANDROID_NDK_HOME=\"$HOME/Library/Android/sdk/ndk/<version>\""
                : "export ANDROID_NDK_HOME=\"$HOME/Android/Sdk/ndk/<version>\"";
        var hostNote = os is OperatingSystemKind.DebianLinux or OperatingSystemKind.OtherLinux or OperatingSystemKind.Windows
            ? " Google ships the NDK's host tools for x86-64 only on " + (os == OperatingSystemKind.Windows ? "Windows" : "Linux") + "; an ARM64 machine cannot run it."
            : string.Empty;
        return new InstallGuidance
        {
            Topic = InstallGuidanceTopic.InstallAndroidNdk,
            OperatingSystem = os,
            Title = "Install the Android NDK",
            Explanation = "Building for Android needs the NDK's clang (as cargo's linker and the C compiler for build scripts) and llvm-ar. Install it with Android Studio's SDK Manager (SDK Tools, \"NDK (Side by side)\") or with the command-line sdkmanager, then name it with ANDROID_NDK_HOME, or name the SDK with ANDROID_HOME so the newest NDK under its ndk/ folder is used." + hostNote,
            Commands = new[]
            {
                "# Android Studio: Settings > Languages & Frameworks > Android SDK > SDK Tools > NDK (Side by side)",
                sdkmanager + " --list | grep \"ndk;\"",
                sdkmanager + " --install \"ndk;<version>\"",
                export,
            },
            DocumentationUrl = "https://developer.android.com/studio/projects/install-ndk",
            RequiresNewShell = true,
        };
    }

    private static InstallGuidance InstallLinuxCrossTools(OperatingSystemKind os, IReadOnlyList<string> items)
    {
        var triples = items.Where(LinuxLinkProfile.IsLinuxTriple).ToList();
        if (triples.Count == 0)
        {
            triples = new List<string> { LinuxLinkProfile.Arm64Triple, LinuxLinkProfile.RiscV64Triple };
        }

        var rustup = "rustup target add " + string.Join(" ", triples);
        var apt = "sudo apt install " + string.Join(" ", triples.SelectMany(LinuxLinkProfile.DebianPackagesOf).Distinct(StringComparer.Ordinal));
        var tools = string.Join(", ", triples.Select(t => LinuxLinkProfile.ToolPrefixOf(t) + "-gcc"));
        const string why = "Building a Linux target of another processor architecture needs the target's standard library (rustup) and a GNU cross toolchain: the cross gcc links the program and compiles C for build scripts, the cross ar archives, and the cross glibc headers and libraries are what it links against.";
        return os switch
        {
            OperatingSystemKind.DebianLinux => new InstallGuidance
            {
                Topic = InstallGuidanceTopic.InstallLinuxCrossTools,
                OperatingSystem = os,
                Title = "Install the Linux cross toolchain",
                Explanation = why + " Debian packages them per architecture.",
                Commands = new[] { rustup, "sudo apt update", apt },
                DocumentationUrl = RustupTargetsUrl,
                RequiresAdministrator = true,
            },
            OperatingSystemKind.OtherLinux => new InstallGuidance
            {
                Topic = InstallGuidanceTopic.InstallLinuxCrossTools,
                OperatingSystem = os,
                Title = "Install the Linux cross toolchain",
                Explanation = why + " Install your distribution's cross gcc, g++, binutils and glibc packages so that " + tools + " are on PATH.",
                Commands = new[] { rustup, "# Debian / Ubuntu:", apt },
                DocumentationUrl = RustupTargetsUrl,
                RequiresAdministrator = true,
            },
            _ => new InstallGuidance
            {
                Topic = InstallGuidanceTopic.InstallLinuxCrossTools,
                OperatingSystem = os,
                Title = "Install the Linux cross toolchain",
                Explanation = why + " Put a GNU cross toolchain (" + tools + " and the matching g++, ar and ranlib) on PATH, or name its folder in the link profile; a Linux machine or CodeBrix.RustTools.Docker is the simpler way to build these targets.",
                Commands = new[] { rustup, "# On a Debian or Ubuntu machine:", apt },
                DocumentationUrl = RustupTargetsUrl,
            },
        };
    }

    private static InstallGuidance InstallToolchain(OperatingSystemKind os, IReadOnlyList<string> toolchains) => new InstallGuidance
    {
        Topic = InstallGuidanceTopic.InstallToolchain,
        OperatingSystem = os,
        Title = "Install the toolchain the project asks for",
        Explanation = "The project's rust-toolchain.toml selects a toolchain that is not installed. Install it (its listed components and targets are added automatically).",
        Commands = toolchains.Select(t => "rustup toolchain install " + t).ToList(),
        DocumentationUrl = RustupToolchainsUrl,
    };
}
