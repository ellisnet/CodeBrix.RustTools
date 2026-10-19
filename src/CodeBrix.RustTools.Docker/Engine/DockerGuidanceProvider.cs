using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// Produces operating-system-specific <see cref="DockerGuidance"/>: how to install Docker,
/// start it, give the current user access to it, enable the WSL 2 backend on Windows, switch
/// Docker Desktop to Linux containers and let the engine run other architectures (qemu-user
/// binfmt handlers, <see cref="DockerGuidanceTopic.EnableForeignPlatforms"/>). The guidance is data for the consuming application to
/// show; nothing here is ever executed.
/// </summary>
public static class DockerGuidanceProvider
{
    private const string WindowsInstallUrl = "https://docs.docker.com/desktop/setup/install/windows-install/";
    private const string MacInstallUrl = "https://docs.docker.com/desktop/setup/install/mac-install/";
    private const string DebianInstallUrl = "https://docs.docker.com/engine/install/debian/";
    private const string LinuxInstallUrl = "https://docs.docker.com/engine/install/";
    private const string LinuxPostInstallUrl = "https://docs.docker.com/engine/install/linux-postinstall/";
    private const string DesktopUrl = "https://docs.docker.com/desktop/";
    private const string WslUrl = "https://docs.docker.com/desktop/features/wsl/";
    private const string WindowsFaqUrl = "https://docs.docker.com/desktop/troubleshoot-and-support/faqs/windowsfaqs/";
    private const string ContextsUrl = "https://docs.docker.com/engine/manage-resources/contexts/";
    private const string MultiPlatformUrl = "https://docs.docker.com/build/building/multi-platform/";

    /// <summary>
    /// Returns the guidance that resolves the issues an engine check found, in the order the
    /// user should follow it. Empty when the engine has no issues.
    /// </summary>
    /// <param name="info">An engine check from <see cref="DockerAvailability"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="info"/> is <c>null</c>.</exception>
    public static IReadOnlyList<DockerGuidance> ForEngine(DockerEngineInfo info)
    {
        if (info == null)
        {
            throw new ArgumentNullException(nameof(info));
        }

        return ForIssues(info.Issues, info.HostOperatingSystem);
    }

    /// <summary>
    /// Returns the guidance that resolves the given issues on the given operating system, in the
    /// order the user should follow it, without duplicates.
    /// </summary>
    /// <param name="issues">The issues to resolve; <c>null</c> is treated as none.</param>
    /// <param name="os">The operating system the commands are written for.</param>
    public static IReadOnlyList<DockerGuidance> ForIssues(IEnumerable<DockerEngineIssue> issues, OperatingSystemKind os)
    {
        var topics = new List<DockerGuidanceTopic>();
        foreach (var issue in issues ?? Enumerable.Empty<DockerEngineIssue>())
        {
            foreach (var topic in TopicsFor(issue, os))
            {
                if (!topics.Contains(topic))
                {
                    topics.Add(topic);
                }
            }
        }

        return topics.Select(topic => GetGuidance(topic, os)).ToList();
    }

    /// <summary>
    /// Returns the guidance for one topic on one operating system. Every combination returns at
    /// least one command line (a <c>#</c> comment when the step is not a terminal command or does
    /// not apply).
    /// </summary>
    /// <param name="topic">What to resolve.</param>
    /// <param name="os">The operating system the commands are written for.</param>
    public static DockerGuidance GetGuidance(DockerGuidanceTopic topic, OperatingSystemKind os) => topic switch
    {
        DockerGuidanceTopic.InstallDocker => InstallDocker(os),
        DockerGuidanceTopic.StartDocker => StartDocker(os),
        DockerGuidanceTopic.GrantDockerAccess => GrantAccess(os),
        DockerGuidanceTopic.EnableWsl2Backend => EnableWsl2(os),
        DockerGuidanceTopic.SwitchToLinuxContainers => SwitchToLinux(os),
        DockerGuidanceTopic.CheckEndpoint => CheckEndpoint(os),
        DockerGuidanceTopic.InstallDockerCli => InstallCli(os),
        DockerGuidanceTopic.EnableForeignPlatforms => EnableForeignPlatforms(os),
        _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Unknown guidance topic."),
    };

    private static IEnumerable<DockerGuidanceTopic> TopicsFor(DockerEngineIssue issue, OperatingSystemKind os)
    {
        switch (issue)
        {
            case DockerEngineIssue.EndpointNotSupported:
                yield return DockerGuidanceTopic.CheckEndpoint;
                break;
            case DockerEngineIssue.NotReachable:
                yield return DockerGuidanceTopic.InstallDocker;
                if (os == OperatingSystemKind.Windows)
                {
                    yield return DockerGuidanceTopic.EnableWsl2Backend;
                }

                yield return DockerGuidanceTopic.StartDocker;
                if (IsLinux(os))
                {
                    yield return DockerGuidanceTopic.GrantDockerAccess;
                }

                break;
            case DockerEngineIssue.PermissionDenied:
                yield return DockerGuidanceTopic.GrantDockerAccess;
                break;
            case DockerEngineIssue.NotLinuxDaemon:
                yield return DockerGuidanceTopic.SwitchToLinuxContainers;
                break;
            case DockerEngineIssue.DockerCliNotFound:
                yield return DockerGuidanceTopic.InstallDockerCli;
                break;
        }
    }

    private static bool IsLinux(OperatingSystemKind os)
        => os == OperatingSystemKind.DebianLinux || os == OperatingSystemKind.OtherLinux;

    private static DockerGuidance InstallDocker(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDocker,
            OperatingSystem = os,
            Title = "Install Docker Desktop",
            Explanation = "Rust builds run inside a Linux container, so this machine needs Docker Desktop (with its WSL 2 backend) but no Rust tools. Skip this step if Docker Desktop is already installed.",
            Commands = new[]
            {
                "winget install --exact --id Docker.DockerDesktop",
                "# or download the installer from " + WindowsInstallUrl,
            },
            DocumentationUrl = WindowsInstallUrl,
            RequiresAdministrator = true,
            RequiresNewSession = true,
        },
        OperatingSystemKind.MacOS => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDocker,
            OperatingSystem = os,
            Title = "Install Docker Desktop",
            Explanation = "Rust builds run inside a Linux container, so this Mac needs Docker Desktop but no Rust tools. Skip this step if Docker Desktop is already installed.",
            Commands = new[]
            {
                "brew install --cask docker",
                "# or download Docker Desktop from " + MacInstallUrl,
            },
            DocumentationUrl = MacInstallUrl,
            RequiresAdministrator = true,
        },
        OperatingSystemKind.DebianLinux => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDocker,
            OperatingSystem = os,
            Title = "Install Docker Engine from Docker's apt repository",
            Explanation = "Rust builds run inside a Linux container, so this machine needs Docker Engine and the docker command-line tool with its buildx plugin, but no Rust tools. These commands add Docker's Debian repository and install it; on Ubuntu and its derivatives use the Ubuntu instructions instead (replace 'debian' with 'ubuntu' and use the Ubuntu codename). Skip this step if Docker is already installed.",
            Commands = new[]
            {
                "sudo apt-get update",
                "sudo apt-get install ca-certificates curl",
                "sudo install -m 0755 -d /etc/apt/keyrings",
                "sudo curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc",
                "sudo chmod a+r /etc/apt/keyrings/docker.asc",
                "echo \"deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $(. /etc/os-release && echo \"$VERSION_CODENAME\") stable\" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null",
                "sudo apt-get update",
                "sudo apt-get install docker-ce docker-ce-cli containerd.io docker-buildx-plugin",
            },
            DocumentationUrl = DebianInstallUrl,
            RequiresAdministrator = true,
        },
        _ => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDocker,
            OperatingSystem = os,
            Title = "Install Docker Engine",
            Explanation = "Rust builds run inside a Linux container, so this machine needs Docker Engine and the docker command-line tool with its buildx plugin, but no Rust tools. Follow Docker's installation instructions for this distribution. Skip this step if Docker is already installed.",
            Commands = new[] { "# Follow the Docker Engine installation instructions for your distribution: " + LinuxInstallUrl },
            DocumentationUrl = LinuxInstallUrl,
            RequiresAdministrator = true,
        },
    };

    private static DockerGuidance StartDocker(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.StartDocker,
            OperatingSystem = os,
            Title = "Start Docker Desktop",
            Explanation = "Docker Desktop must be running before containers can be used. Start it and wait until it reports that the engine is running.",
            Commands = new[] { "Start-Process \"$Env:ProgramFiles\\Docker\\Docker\\Docker Desktop.exe\"" },
            DocumentationUrl = DesktopUrl,
        },
        OperatingSystemKind.MacOS => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.StartDocker,
            OperatingSystem = os,
            Title = "Start Docker Desktop",
            Explanation = "Docker Desktop must be running before containers can be used. Start it and wait until it reports that the engine is running.",
            Commands = new[] { "open -a Docker" },
            DocumentationUrl = DesktopUrl,
        },
        OperatingSystemKind.DebianLinux or OperatingSystemKind.OtherLinux => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.StartDocker,
            OperatingSystem = os,
            Title = "Start the Docker service",
            Explanation = "The Docker daemon must be running before containers can be used. The second command also starts it at every boot.",
            Commands = new[]
            {
                "sudo systemctl start docker",
                "sudo systemctl enable docker",
            },
            DocumentationUrl = LinuxPostInstallUrl,
            RequiresAdministrator = true,
        },
        _ => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.StartDocker,
            OperatingSystem = os,
            Title = "Start Docker",
            Explanation = "The Docker engine must be running before containers can be used.",
            Commands = new[] { "# Start Docker Desktop or the Docker service, then check with: docker info" },
            DocumentationUrl = LinuxInstallUrl,
        },
    };

    private static DockerGuidance GrantAccess(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.GrantDockerAccess,
            OperatingSystem = os,
            Title = "Add your account to the docker-users group",
            Explanation = "Docker Desktop lets members of the local docker-users group use the engine. Run this in an elevated PowerShell, then sign out and back in.",
            Commands = new[] { "Add-LocalGroupMember -Group docker-users -Member $env:USERNAME" },
            DocumentationUrl = WindowsFaqUrl,
            RequiresAdministrator = true,
            RequiresNewSession = true,
        },
        OperatingSystemKind.MacOS => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.GrantDockerAccess,
            OperatingSystem = os,
            Title = "Restart Docker Desktop as the signed-in user",
            Explanation = "Docker Desktop gives the user who runs it access to the engine through ~/.docker/run/docker.sock. Quit Docker Desktop and start it again from your own account.",
            Commands = new[] { "osascript -e 'quit app \"Docker\"'", "open -a Docker" },
            DocumentationUrl = MacInstallUrl,
        },
        OperatingSystemKind.DebianLinux or OperatingSystemKind.OtherLinux => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.GrantDockerAccess,
            OperatingSystem = os,
            Title = "Add your user to the docker group",
            Explanation = "Only root and members of the docker group may use the Docker socket. Add yourself to the group, then sign out and back in (or start a shell with the new group) so the change takes effect. Membership of the docker group is equivalent to root access on this machine.",
            Commands = new[]
            {
                "sudo usermod -aG docker $USER",
                "# then sign out and back in, or run: newgrp docker",
            },
            DocumentationUrl = LinuxPostInstallUrl,
            RequiresAdministrator = true,
            RequiresNewSession = true,
        },
        _ => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.GrantDockerAccess,
            OperatingSystem = os,
            Title = "Give your account access to Docker",
            Explanation = "The Docker engine refused this user. Ask an administrator to grant access to the Docker engine.",
            Commands = new[] { "# Grant this account access to the Docker engine, then check with: docker info" },
            DocumentationUrl = LinuxPostInstallUrl,
            RequiresAdministrator = true,
        },
    };

    private static DockerGuidance EnableWsl2(OperatingSystemKind os) => os == OperatingSystemKind.Windows
        ? new DockerGuidance
        {
            Topic = DockerGuidanceTopic.EnableWsl2Backend,
            OperatingSystem = os,
            Title = "Install or update WSL 2 for Docker Desktop",
            Explanation = "Docker Desktop on Windows runs Linux containers in WSL 2. Install or update WSL from an elevated prompt, restart if asked, then make sure Docker Desktop uses the WSL 2 based engine (Settings > General).",
            Commands = new[]
            {
                "wsl --install",
                "wsl --update",
                "# In Docker Desktop: Settings > General > Use the WSL 2 based engine",
            },
            DocumentationUrl = WslUrl,
            RequiresAdministrator = true,
            RequiresNewSession = true,
        }
        : new DockerGuidance
        {
            Topic = DockerGuidanceTopic.EnableWsl2Backend,
            OperatingSystem = os,
            Title = "WSL 2 is not needed here",
            Explanation = "WSL 2 is the Linux-container backend of Docker Desktop on Windows; this operating system does not use it.",
            Commands = new[] { "# Nothing to do on this operating system." },
            DocumentationUrl = WslUrl,
        };

    private static DockerGuidance SwitchToLinux(OperatingSystemKind os) => os == OperatingSystemKind.Windows
        ? new DockerGuidance
        {
            Topic = DockerGuidanceTopic.SwitchToLinuxContainers,
            OperatingSystem = os,
            Title = "Switch Docker Desktop to Linux containers",
            Explanation = "Docker Desktop is running Windows containers, but Rust builds need Linux containers. Switch the engine, then try again.",
            Commands = new[]
            {
                "& \"$Env:ProgramFiles\\Docker\\Docker\\DockerCli.exe\" -SwitchLinuxEngine",
                "# or right-click the Docker icon in the notification area and choose 'Switch to Linux containers...'",
            },
            DocumentationUrl = WindowsFaqUrl,
        }
        : new DockerGuidance
        {
            Topic = DockerGuidanceTopic.SwitchToLinuxContainers,
            OperatingSystem = os,
            Title = "Use a Docker engine that runs Linux containers",
            Explanation = "The configured engine reports Windows containers, but Rust builds need Linux containers. Point the endpoint at a Linux engine.",
            Commands = new[] { "docker info --format \"{{.OSType}}\"", "docker context ls" },
            DocumentationUrl = ContextsUrl,
        };

    private static DockerGuidance CheckEndpoint(OperatingSystemKind os) => new DockerGuidance
    {
        Topic = DockerGuidanceTopic.CheckEndpoint,
        OperatingSystem = os,
        Title = "Check the Docker endpoint",
        Explanation = "The configured Docker endpoint (the workbench options or the DOCKER_HOST environment variable) is not one the Docker client can use. Supported forms are unix://, npipe://, tcp:// and ssh://; TLS-secured https:// endpoints are not supported - reach a remote engine over ssh:// instead.",
        Commands = new[]
        {
            "docker context ls",
            os == OperatingSystemKind.Windows ? "echo $env:DOCKER_HOST" : "echo \"$DOCKER_HOST\"",
        },
        DocumentationUrl = ContextsUrl,
    };

    private static DockerGuidance EnableForeignPlatforms(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows or OperatingSystemKind.MacOS => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.EnableForeignPlatforms,
            OperatingSystem = os,
            Title = "Use Docker Desktop's emulation for other architectures",
            Explanation = "Images of another architecture (for example linux/arm64 or linux/riscv64 on an x86-64 machine) run under QEMU emulation. Docker Desktop includes it; if a foreign-platform container still fails with 'exec format error', update Docker Desktop and restart it.",
            Commands = new[] { "docker run --rm --platform linux/arm64 busybox uname -m" },
            DocumentationUrl = MultiPlatformUrl,
        },
        OperatingSystemKind.DebianLinux => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.EnableForeignPlatforms,
            OperatingSystem = os,
            Title = "Install the qemu-user binfmt handlers",
            Explanation = "Images of another architecture (for example linux/arm64 or linux/riscv64 on an x86-64 machine) run under qemu-user emulation, which the kernel starts through a binfmt_misc handler per architecture. Without one, every program in such a container fails with 'exec format error'. These packages install the static qemu-user emulators and register the handlers (they appear as /proc/sys/fs/binfmt_misc/qemu-aarch64, qemu-riscv64, ...). Emulated builds are many times slower than native ones.",
            Commands = new[]
            {
                "sudo apt install qemu-user-static binfmt-support",
                "ls /proc/sys/fs/binfmt_misc/",
            },
            DocumentationUrl = MultiPlatformUrl,
            RequiresAdministrator = true,
        },
        _ => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.EnableForeignPlatforms,
            OperatingSystem = os,
            Title = "Register qemu-user binfmt handlers",
            Explanation = "Images of another architecture (for example linux/arm64 or linux/riscv64 on an x86-64 machine) run under qemu-user emulation, which the kernel starts through a binfmt_misc handler per architecture. Without one, every program in such a container fails with 'exec format error'. Install your distribution's static qemu-user package with its binfmt registration (on Debian and Ubuntu: qemu-user-static and binfmt-support).",
            Commands = new[]
            {
                "# Install the static qemu-user emulators and their binfmt registration for your distribution: " + MultiPlatformUrl,
                "ls /proc/sys/fs/binfmt_misc/",
            },
            DocumentationUrl = MultiPlatformUrl,
            RequiresAdministrator = true,
        },
    };

    private static DockerGuidance InstallCli(OperatingSystemKind os) => os switch
    {
        OperatingSystemKind.Windows or OperatingSystemKind.MacOS => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDockerCli,
            OperatingSystem = os,
            Title = "Put the docker command on PATH",
            Explanation = "Building derived images uses the docker command-line tool (BuildKit). Docker Desktop installs it; make sure its folder is on PATH, or set the Docker CLI path in the workbench's client options.",
            Commands = new[] { "docker --version", "docker buildx version" },
            DocumentationUrl = os == OperatingSystemKind.Windows ? WindowsInstallUrl : MacInstallUrl,
        },
        OperatingSystemKind.DebianLinux => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDockerCli,
            OperatingSystem = os,
            Title = "Install the docker command-line tool",
            Explanation = "Building derived images uses the docker command-line tool with its buildx plugin (BuildKit). Install both from Docker's apt repository.",
            Commands = new[] { "sudo apt-get install docker-ce-cli docker-buildx-plugin" },
            DocumentationUrl = DebianInstallUrl,
            RequiresAdministrator = true,
        },
        _ => new DockerGuidance
        {
            Topic = DockerGuidanceTopic.InstallDockerCli,
            OperatingSystem = os,
            Title = "Install the docker command-line tool",
            Explanation = "Building derived images uses the docker command-line tool with its buildx plugin (BuildKit). Install them with your distribution's Docker packages.",
            Commands = new[] { "# Install the docker CLI and the buildx plugin: " + LinuxInstallUrl },
            DocumentationUrl = LinuxInstallUrl,
            RequiresAdministrator = true,
        },
    };
}
