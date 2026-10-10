using System;
using System.Linq;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class ToolchainReadinessTests
{
    [Fact]
    public void Evaluate_is_ready_for_a_complete_environment()
    {
        //Act
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(), ToolchainRequirements.WithLintAndFormat());

        //Assert
        readiness.IsReady.Should().BeTrue();
        readiness.Issues.Should().BeEmpty();
        readiness.ToString().Should().Be("Ready to build.");
    }

    [Fact]
    public void Evaluate_blocks_when_an_android_ndk_is_required_but_missing()
    {
        //Act
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(), ToolchainRequirements.ForAndroid());

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.AndroidNdkMissing.Should().BeTrue();
        readiness.Issues.Should().Contain(i => i.Kind == ReadinessIssueKind.AndroidNdkMissing && i.IsBlocking);
        readiness.MissingTargets.Should().Equal("aarch64-linux-android", "x86_64-linux-android");
    }

    [Fact]
    public void Evaluate_is_ready_for_android_with_an_ndk_and_both_targets()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var ndk = AndroidNdk.Load(FakeAndroidNdk.Create(System.IO.Path.Combine(sandbox.Directory, "ndk"), "30.0.16248370", "linux-x86_64", new[] { 24 }), "linux-x86_64");
        var info = new RustToolchainInfo
        {
            OperatingSystem = OperatingSystemKind.DebianLinux,
            Tools = TestToolchains.CompleteTools(),
            HostTriple = "x86_64-unknown-linux-gnu",
            ActiveToolchain = "stable-x86_64-unknown-linux-gnu",
            InstalledToolchains = new[] { "stable-x86_64-unknown-linux-gnu" },
            InstalledTargets = new[] { "x86_64-unknown-linux-gnu", "aarch64-linux-android", "x86_64-linux-android" },
            AndroidNdk = ndk,
        };

        //Act
        var readiness = ToolchainReadiness.Evaluate(info, ToolchainRequirements.ForAndroid());

        //Assert
        readiness.IsReady.Should().BeTrue(readiness.ToString());
        readiness.AndroidNdkMissing.Should().BeFalse();
        info.ToString().Should().Contain("Android NDK: Android NDK 30.0.16248370");
    }

    [Fact]
    public void Evaluate_blocks_when_cargo_is_missing()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Cargo, "not found"));

        //Act
        var readiness = ToolchainReadiness.Evaluate(info);

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.MissingTools.Should().ContainSingle().Which.Should().Be(RustTool.Cargo);
        readiness.Issues[0].IsBlocking.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_flags_tools_older_than_the_minimum_version()
    {
        //Arrange
        var requirements = new ToolchainRequirements { MinimumRustVersion = new ToolVersion(1, 95, 0) };

        //Act
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(), requirements);

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.OutdatedTools.Should().Contain(RustTool.Rustc).And.Contain(RustTool.Cargo);
    }

    [Fact]
    public void Evaluate_warns_but_does_not_block_when_the_c_compiler_is_unknown()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.Windows, InstalledTool.Unknown(RustTool.CCompiler, "vswhere failed"));

        //Act
        var readiness = ToolchainReadiness.Evaluate(info);

        //Assert
        readiness.IsReady.Should().BeTrue();
        readiness.Issues.Should().ContainSingle().Which.Kind.Should().Be(ReadinessIssueKind.ToolStatusUnknown);
    }

    [Fact]
    public void Evaluate_ignores_optional_tools_that_are_missing()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.MacOS, InstalledTool.Missing(RustTool.Clippy, "not installed"), InstalledTool.Missing(RustTool.Rustup, "none"));

        //Act
        var readiness = ToolchainReadiness.Evaluate(info, ToolchainRequirements.Default);

        //Assert
        readiness.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_reports_missing_targets()
    {
        //Arrange
        var requirements = new ToolchainRequirements();
        requirements.RequiredTargets.Add("x86_64-unknown-linux-gnu");
        requirements.RequiredTargets.Add("wasm32-unknown-unknown");

        //Act
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(), requirements);

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.MissingTargets.Should().ContainSingle().Which.Should().Be("wasm32-unknown-unknown");
    }

    [Fact]
    public void Evaluate_reports_an_uninstalled_toolchain_file_channel()
    {
        //Arrange
        var file = RustToolchainFile.Parse("[toolchain]\nchannel = \"nightly-2026-01-15\"\n", "/work/rust-toolchain.toml");
        var info = TestToolchains.Create(OperatingSystemKind.DebianLinux, TestToolchains.CompleteTools(), file);

        //Act
        var readiness = ToolchainReadiness.Evaluate(info);

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.Issues.Should().ContainSingle().Which.Subject.Should().Be("nightly-2026-01-15");
    }

    [Fact]
    public void Evaluate_accepts_an_installed_toolchain_file_channel()
    {
        //Arrange
        var file = RustToolchainFile.Parse("[toolchain]\nchannel = \"stable\"\n");
        var info = TestToolchains.Create(OperatingSystemKind.DebianLinux, TestToolchains.CompleteTools(), file);

        //Act
        var readiness = ToolchainReadiness.Evaluate(info);

        //Assert
        readiness.IsReady.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_rejects_a_null_toolchain()
    {
        //Arrange
        Action act = () => ToolchainReadiness.Evaluate(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static string FakeCrossTools(SampleSandbox sandbox, params string[] names)
    {
        var folder = sandbox.CreateDirectory("cross-bin");
        foreach (var name in names)
        {
            // The locator looks for <name>.exe on Windows.
            var fileName = OperatingSystem.IsWindows() ? name + ".exe" : name;
            var path = sandbox.WriteFile("cross-bin/" + fileName, "#!/bin/sh\nexit 0\n");
            if (!OperatingSystem.IsWindows())
            {
                System.IO.File.SetUnixFileMode(path, System.IO.UnixFileMode.UserRead | System.IO.UnixFileMode.UserWrite | System.IO.UnixFileMode.UserExecute);
            }
        }

        return folder;
    }

    [Fact]
    public void ForLinuxCross_requires_each_target_and_its_cross_tools()
    {
        //Act
        var requirements = ToolchainRequirements.ForLinuxCross(new[] { "aarch64-unknown-linux-gnu", " riscv64gc-unknown-linux-gnu " }, "/opt/cross/bin");

        //Assert
        requirements.RequiredTargets.Should().Equal("aarch64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");
        requirements.RequiredLinuxCrossTools.Keys.Should().Equal("aarch64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");
        requirements.RequiredLinuxCrossTools["riscv64gc-unknown-linux-gnu"].CompilerPath.Should().Be("/opt/cross/bin/riscv64-linux-gnu-gcc");
        requirements.RequireCCompiler.Should().BeTrue();
    }

    [Fact]
    public void ForLinuxCross_rejects_a_triple_that_is_not_linux()
    {
        //Act
        var act = () => ToolchainRequirements.ForLinuxCross(new[] { "aarch64-apple-darwin" });

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*aarch64-apple-darwin*");
    }

    [Fact]
    public void Evaluate_blocks_when_a_linux_cross_tool_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = FakeCrossTools(sandbox, "aarch64-linux-gnu-gcc");
        var info = TestToolchains.WithTargets("x86_64-unknown-linux-gnu", "aarch64-unknown-linux-gnu");

        //Act
        var readiness = ToolchainReadiness.Evaluate(info, ToolchainRequirements.ForLinuxCross(new[] { "aarch64-unknown-linux-gnu" }, folder));

        //Assert
        readiness.IsReady.Should().BeFalse();
        readiness.MissingTargets.Should().BeEmpty();
        readiness.MissingLinuxCrossTargets.Should().Equal("aarch64-unknown-linux-gnu");
        var issue = readiness.Issues.Should().ContainSingle().Which;
        issue.Kind.Should().Be(ReadinessIssueKind.LinuxCrossToolMissing);
        issue.IsBlocking.Should().BeTrue();
        issue.Message.Should().Contain("aarch64-linux-gnu-ar").And.NotContain("aarch64-linux-gnu-gcc'");
    }

    [Fact]
    public void Evaluate_is_ready_for_a_linux_cross_target_with_its_tools_and_standard_library()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = FakeCrossTools(sandbox, "riscv64-linux-gnu-gcc", "riscv64-linux-gnu-ar");
        var info = TestToolchains.WithTargets("x86_64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu");

        //Act
        var readiness = ToolchainReadiness.Evaluate(info, ToolchainRequirements.ForLinuxCross(new[] { "riscv64gc-unknown-linux-gnu" }, folder));

        //Assert
        readiness.IsReady.Should().BeTrue(readiness.ToString());
        readiness.MissingLinuxCrossTargets.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_reports_the_missing_target_and_the_missing_cross_tools_together()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var info = TestToolchains.Complete();

        //Act
        var readiness = ToolchainReadiness.Evaluate(info, ToolchainRequirements.ForLinuxCross(new[] { "aarch64-unknown-linux-gnu" }, sandbox.Directory));

        //Assert
        readiness.MissingTargets.Should().Equal("aarch64-unknown-linux-gnu");
        readiness.MissingLinuxCrossTargets.Should().Equal("aarch64-unknown-linux-gnu");
        readiness.Issues.Should().Contain(i => i.Kind == ReadinessIssueKind.LinuxCrossToolMissing && i.Message.Contains("were not found"));
    }
}
