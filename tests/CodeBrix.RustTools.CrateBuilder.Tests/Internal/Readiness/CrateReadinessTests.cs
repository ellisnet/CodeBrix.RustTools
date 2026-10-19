using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Internal.Readiness;

public class CrateReadinessTests
{
    private const string LinuxHost = "x86_64-unknown-linux-gnu";

    private static RustToolchainInfo Toolchain(bool cargo = true, string sysroot = null, params string[] installedTargets)
    {
        var tools = new Dictionary<RustTool, InstalledTool>();
        foreach (var tool in Enum.GetValues<RustTool>())
        {
            tools[tool] = new InstalledTool { Tool = tool, Status = ToolStatus.Installed, Version = new ToolVersion(1, 99, 0), ExecutablePath = "/usr/bin/" + tool };
        }

        if (!cargo)
        {
            tools[RustTool.Cargo] = InstalledTool.Missing(RustTool.Cargo, "not on PATH");
        }

        return new RustToolchainInfo
        {
            OperatingSystem = OperatingSystemKind.DebianLinux,
            HostTriple = LinuxHost,
            Tools = tools,
            Sysroot = sysroot,
            InstalledToolchains = new[] { "stable-" + LinuxHost },
            InstalledTargets = new[] { LinuxHost }.Concat(installedTargets).ToList(),
        };
    }

    private static HostEvaluationSettings Settings(string llvmMingw = null, string macSysroot = null, string crossTools = null) => new HostEvaluationSettings
    {
        LlvmMingwDirectory = llvmMingw ?? "/nonexistent-llvm-mingw",
        MacOSSysrootDirectory = macSysroot,
        LinuxCrossToolDirectory = crossTools ?? "/nonexistent-cross-tools",
    };

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, "#!/bin/sh\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    // The cross-tool and rust-lld lookups add .exe to a tool's name on a Windows host.
    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    // ---- host mode, mapping ----

    [Fact]
    public void host_the_native_target_is_buildable_and_a_missing_std_is_not()
    {
        //Act
        var report = ReadinessReportBuilder.ForHost(new[] { CrateTarget.LinuxX64, CrateTarget.OsxArm64 }, Toolchain(), Settings());

        //Assert
        report.IsReady.Should().BeTrue();
        report.Mode.Should().Be(BuildMode.Host);
        report.BuildableTargets.Should().Equal(CrateTarget.LinuxX64);
        report.UnbuildableTargets.Should().Equal(CrateTarget.OsxArm64);
        report.Issues.Should().Contain(i => i.Code == "target.target-missing" && i.Target == CrateTarget.OsxArm64 && !i.IsBlocking);
        report.GetGuidance(CrateTarget.OsxArm64).Should().Contain(g => g.Topic == InstallGuidanceTopic.AddTargets);
        report.GetGuidance(CrateTarget.LinuxX64).Should().BeEmpty();
        report.Toolchain.Should().NotBeNull();
    }

    [Fact]
    public void host_missing_cargo_blocks_everything_with_guidance()
    {
        //Act
        var report = ReadinessReportBuilder.ForHost(new[] { CrateTarget.LinuxX64 }, Toolchain(cargo: false), Settings());

        //Assert
        report.IsReady.Should().BeFalse();
        report.Issues[0].Code.Should().Be("toolchain.tool-missing");
        report.Issues[0].IsBlocking.Should().BeTrue();
        report.UnbuildableTargets.Should().Equal(CrateTarget.LinuxX64);
        report.Guidance.Should().NotBeEmpty();
    }

    [Fact]
    public void host_no_buildable_target_is_blocking()
    {
        //Act
        var report = ReadinessReportBuilder.ForHost(new[] { CrateTarget.AndroidArm64 }, Toolchain(), Settings());

        //Assert
        report.IsReady.Should().BeFalse();
        report.Issues.Should().Contain(i => i.Code == "targets.none-buildable" && i.IsBlocking);
        report.Issues.Should().Contain(i => i.Code == "target.android-ndk-missing" && i.Target == CrateTarget.AndroidArm64);
    }

    [Fact]
    public void host_linux_cross_needs_the_std_and_the_cross_tools()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var tools = sandbox.CreateDirectory("cross");
        Touch(Path.Combine(tools, Executable("aarch64-linux-gnu-gcc")));
        Touch(Path.Combine(tools, Executable("aarch64-linux-gnu-ar")));
        var toolchain = Toolchain(installedTargets: "aarch64-unknown-linux-gnu");

        //Act
        var withTools = HostTargetEvaluator.Evaluate(CrateTarget.LinuxArm64, toolchain, Settings(crossTools: tools));
        var withoutTools = HostTargetEvaluator.Evaluate(CrateTarget.LinuxArm64, toolchain, Settings());
        var withoutStd = HostTargetEvaluator.Evaluate(CrateTarget.LinuxRiscV64, toolchain, Settings(crossTools: tools));

        //Assert
        withTools.IsBuildable.Should().BeTrue();
        withoutTools.IsBuildable.Should().BeFalse();
        withoutTools.Issues.Should().Contain(i => i.Code == "target.linux-cross-tool-missing");
        withoutTools.Guidance.Should().Contain(g => g.Topic == InstallGuidanceTopic.InstallLinuxCrossTools);
        withoutStd.IsBuildable.Should().BeFalse();
        withoutStd.Issues.Should().Contain(i => i.Code == "target.target-missing");
    }

    [Fact]
    public void host_windows_needs_llvm_mingw_through_the_host_packages_requirement()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = sandbox.CreateDirectory("llvm-mingw");
        Touch(Path.Combine(root, "bin", "x86_64-w64-mingw32-clang"));
        var toolchain = Toolchain(installedTargets: "x86_64-pc-windows-gnullvm");

        //Act
        var found = HostTargetEvaluator.Evaluate(CrateTarget.WinX64, toolchain, Settings(llvmMingw: root));
        var missing = HostTargetEvaluator.Evaluate(CrateTarget.WinX64, toolchain, Settings());
        var missingBoth = HostTargetEvaluator.Evaluate(CrateTarget.WinArm64, toolchain, Settings(llvmMingw: root));

        //Assert
        found.IsBuildable.Should().BeTrue();
        found.Guidance.Should().BeEmpty();
        missing.IsBuildable.Should().BeFalse();
        missing.Issues.Should().ContainSingle(i => i.Code == HostTargetEvaluator.LlvmMingwMissingCode && i.Target == CrateTarget.WinX64);
        missing.Issues[0].Message.Should().Contain("CrateBuilderOptions.LlvmMingwDirectory");
        missing.Guidance.Should().ContainSingle(g => g.Topic == InstallGuidanceTopic.InstallLlvmMingw);
        missingBoth.Issues.Select(i => i.Code).Should().BeEquivalentTo(new[] { "target.target-missing", HostTargetEvaluator.LlvmMingwMissingCode });
    }

    [Fact]
    public void host_macos_on_linux_needs_the_link_profile_sysroot()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var rustSysroot = sandbox.CreateDirectory("rust-sysroot");
        Touch(Path.Combine(rustSysroot, "lib", "rustlib", LinuxHost, "bin", Executable("rust-lld")));
        var macSysroot = sandbox.CreateDirectory("macos-sysroot");
        Touch(Path.Combine(macSysroot, "usr", "lib", "libSystem.tbd"));
        var toolchain = Toolchain(sysroot: rustSysroot, installedTargets: "aarch64-apple-darwin");

        //Act
        var withSysroot = HostTargetEvaluator.Evaluate(CrateTarget.OsxArm64, toolchain, Settings(macSysroot: macSysroot));
        var withoutSysroot = HostTargetEvaluator.Evaluate(CrateTarget.OsxArm64, toolchain, Settings());
        var emptySysroot = HostTargetEvaluator.Evaluate(CrateTarget.OsxArm64, toolchain, Settings(macSysroot: sandbox.CreateDirectory("empty")));

        //Assert
        withSysroot.IsBuildable.Should().BeTrue();
        withoutSysroot.IsBuildable.Should().BeFalse();
        withoutSysroot.Issues.Should().ContainSingle(i => i.Code == "macos.link-profile-unavailable");
        emptySysroot.IsBuildable.Should().BeFalse();
    }

    [Fact]
    public void the_report_text_lists_targets_and_issues()
    {
        //Act
        var text = ReadinessReportBuilder.ForHost(new[] { CrateTarget.LinuxX64, CrateTarget.OsxArm64 }, Toolchain(), Settings()).ToString();

        //Assert
        text.Should().StartWith("Ready to build in Host mode.");
        text.Should().Contain("Buildable: linux-x64");
        text.Should().Contain("Not buildable: osx-arm64");
        text.Should().Contain("Warning: [osx-arm64]");
    }

    [Fact]
    public void ProjectDirectoryOf_uses_an_existing_local_manifest_only()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var manifest = sandbox.WriteFile("crate/Cargo.toml", "[package]\nname = \"x\"\n");

        //Act
        var fromFile = ReadinessChecker.ProjectDirectoryOf(new LocalManifestSource(manifest));
        var fromFolder = ReadinessChecker.ProjectDirectoryOf(new LocalManifestSource(Path.GetDirectoryName(manifest)));
        var missing = ReadinessChecker.ProjectDirectoryOf(new LocalManifestSource(Path.Combine(sandbox.Directory, "nope", "Cargo.toml")));
        var git = ReadinessChecker.ProjectDirectoryOf(new GitRepositorySource("https://example.com/a.git"));

        //Assert
        fromFile.Should().Be(Path.GetDirectoryName(manifest));
        fromFolder.Should().Be(Path.GetDirectoryName(manifest));
        missing.Should().BeNull();
        git.Should().BeNull();
    }

    // ---- Docker mode, mapping ----

    private static DockerEngineInfo Engine(string architecture = "x86_64", bool cli = true) => new DockerEngineInfo
    {
        IsAvailable = true,
        IsLinuxDaemon = true,
        OsType = "linux",
        Architecture = architecture,
        DockerCliFound = cli,
        HostOperatingSystem = OperatingSystemKind.DebianLinux,
        ServerVersion = "29.0.0",
    };

    [Fact]
    public void docker_an_unusable_engine_blocks_with_docker_guidance()
    {
        //Arrange
        var engine = new DockerEngineInfo { IsAvailable = false, Issues = new[] { DockerEngineIssue.NotReachable }, HostOperatingSystem = OperatingSystemKind.DebianLinux, ErrorMessage = "connection refused" };

        //Act
        var report = ReadinessReportBuilder.ForDocker(new[] { CrateTarget.LinuxX64 }, engine, LinuxBaseline.RustImage);

        //Assert
        report.IsReady.Should().BeFalse();
        report.Mode.Should().Be(BuildMode.Docker);
        report.Issues.Should().ContainSingle(i => i.Code == "docker.engine-unavailable" && i.IsBlocking);
        report.Issues[0].Message.Should().Contain("connection refused");
        report.DockerGuidance.Should().NotBeEmpty();
        report.UnbuildableTargets.Should().Equal(CrateTarget.LinuxX64);
        report.Engine.Should().BeSameAs(engine);
    }

    [Fact]
    public void docker_every_target_builds_on_an_x64_engine_with_the_cli()
    {
        //Act
        var report = ReadinessReportBuilder.ForDocker(CrateTargets.All, Engine(), LinuxBaseline.RustImage);

        //Assert
        report.IsReady.Should().BeTrue();
        report.BuildableTargets.Should().Equal(CrateTargets.All);
        report.Issues.Should().BeEmpty();
    }

    [Fact]
    public void docker_android_needs_an_x64_engine()
    {
        //Act
        var report = ReadinessReportBuilder.ForDocker(new[] { CrateTarget.AndroidArm64, CrateTarget.LinuxArm64 }, Engine("aarch64"), LinuxBaseline.RustImage);

        //Assert
        report.BuildableTargets.Should().Equal(CrateTarget.LinuxArm64);
        report.Issues.Should().ContainSingle(i => i.Code == "docker.android-needs-x64-engine" && i.Target == CrateTarget.AndroidArm64);
    }

    [Fact]
    public void docker_without_the_cli_only_the_engines_own_linux_target_builds()
    {
        //Act
        var plain = ReadinessReportBuilder.ForDocker(new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64 }, Engine(cli: false), LinuxBaseline.RustImage);
        var manyLinux = ReadinessReportBuilder.ForDocker(new[] { CrateTarget.LinuxX64 }, Engine(cli: false), LinuxBaseline.ManyLinux);

        //Assert
        plain.BuildableTargets.Should().Equal(CrateTarget.LinuxX64);
        plain.Issues.Should().ContainSingle(i => i.Code == "docker.cli-missing" && i.Target == CrateTarget.LinuxArm64);
        plain.DockerGuidance.Should().Contain(g => g.Topic == DockerGuidanceTopic.InstallDockerCli);
        manyLinux.IsReady.Should().BeFalse();
        manyLinux.Issues.Should().Contain(i => i.Code == "docker.cli-missing");
    }

    // ---- live ----

    [Fact]
    public async Task host_live_detection_finds_this_machines_own_target_buildable()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(13);
        using var builder = new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = Path.Combine(sandbox.Directory, "..", "scratch") });
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Targets = CrateTargets.All.ToList(),
            OutputDirectory = Path.Combine(sandbox.Directory, "..", "out"),
        };

        //Act
        var report = await builder.CheckReadinessAsync(request, TestContext.Current.CancellationToken);

        //Assert
        report.Toolchain.Should().NotBeNull();
        report.Toolchain.HasCargoAndRustc.Should().BeTrue();
        (report.BuildableTargets.Count + report.UnbuildableTargets.Count).Should().Be(9);
        var native = CrateTargets.All.Where(t => CrateTargets.Triple(t) == report.Toolchain.HostTriple).ToList();
        if (native.Count == 1 && report.Toolchain.GetTool(RustTool.CCompiler).Status != ToolStatus.Missing)
        {
            report.IsReady.Should().BeTrue();
            report.BuildableTargets.Should().Contain(native[0]);
        }

        foreach (var target in report.UnbuildableTargets)
        {
            report.Issues.Should().Contain(i => i.Target == target);
        }
    }

    [Fact]
    public async Task docker_live_engine_check_makes_the_engines_own_linux_target_buildable()
    {
        //Arrange
        await Capabilities.SkipIfDockerUnavailableAsync();
        using var sandbox = SampleSandbox.CreateEmpty();
        using var builder = new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = Path.Combine(sandbox.Directory, "scratch") });
        var request = new CrateBuildRequest
        {
            Source = new GitRepositorySource("https://example.com/a.git"),
            Mode = BuildMode.Docker,
            Targets = CrateTargets.Linux.ToList(),
            OutputDirectory = Path.Combine(sandbox.Directory, "out"),
        };

        //Act
        var report = await builder.CheckReadinessAsync(request, TestContext.Current.CancellationToken);

        //Assert
        report.Mode.Should().Be(BuildMode.Docker);
        report.Engine.IsAvailable.Should().BeTrue();
        report.IsReady.Should().BeTrue();
        var native = CrateTargets.Linux.Where(t => CrateTargets.Triple(t) == report.Engine.NativeLinuxTriple).ToList();
        native.Should().HaveCount(1);
        report.BuildableTargets.Should().Contain(native[0]);
    }
}
