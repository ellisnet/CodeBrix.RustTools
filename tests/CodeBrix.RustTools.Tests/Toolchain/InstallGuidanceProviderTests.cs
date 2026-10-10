using System;
using System.Linq;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Toolchain;

public class InstallGuidanceProviderTests
{
    [Theory]
    [InlineData(OperatingSystemKind.Windows)]
    [InlineData(OperatingSystemKind.MacOS)]
    [InlineData(OperatingSystemKind.DebianLinux)]
    [InlineData(OperatingSystemKind.OtherLinux)]
    [InlineData(OperatingSystemKind.Unknown)]
    public void GetGuidance_yields_commands_for_every_topic(OperatingSystemKind os)
    {
        foreach (var topic in Enum.GetValues<InstallGuidanceTopic>())
        {
            //Act
            var guidance = InstallGuidanceProvider.GetGuidance(topic, os, new[] { "clippy" });

            //Assert
            guidance.Topic.Should().Be(topic);
            guidance.OperatingSystem.Should().Be(os);
            guidance.Title.Should().NotBeNullOrWhiteSpace();
            guidance.Explanation.Should().NotBeNullOrWhiteSpace();
            guidance.Commands.Should().NotBeEmpty();
            guidance.DocumentationUrl.Should().StartWith("https://");
        }
    }

    [Fact]
    public void GetGuidance_uses_apt_for_the_debian_c_toolchain()
    {
        //Act
        var guidance = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallCToolchain, OperatingSystemKind.DebianLinux);

        //Assert
        guidance.Commands.Should().Contain("sudo apt install build-essential pkg-config");
        guidance.RequiresAdministrator.Should().BeTrue();
    }

    [Fact]
    public void GetGuidance_uses_the_vc_tools_workload_on_windows()
        => InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallCToolchain, OperatingSystemKind.Windows)
            .Commands.Single().Should().Contain("Microsoft.VisualStudio.Workload.VCTools");

    [Fact]
    public void GetGuidance_uses_xcode_select_on_macos()
        => InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallCToolchain, OperatingSystemKind.MacOS)
            .Commands.Should().ContainSingle().Which.Should().Be("xcode-select --install");

    [Fact]
    public void GetGuidance_installs_rust_with_the_official_rustup_script_on_unix()
        => InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallRust, OperatingSystemKind.MacOS)
            .Commands.Should().Contain("curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs | sh");

    [Fact]
    public void ForUpdate_uses_rustup_update_when_rustup_manages_rust()
        => InstallGuidanceProvider.ForUpdate(OperatingSystemKind.Windows, rustupManaged: true)
            .Commands.Should().ContainSingle().Which.Should().Be("rustup update");

    [Fact]
    public void ForUpdate_recommends_rustup_for_a_distro_packaged_rust()
        => InstallGuidanceProvider.ForUpdate(OperatingSystemKind.DebianLinux, rustupManaged: false)
            .Topic.Should().Be(InstallGuidanceTopic.InstallRustup);

    [Fact]
    public void ForReadiness_adds_the_ndk_guidance_when_the_ndk_is_missing()
    {
        //Arrange
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(OperatingSystemKind.MacOS), ToolchainRequirements.ForAndroid());

        //Act
        var guidance = InstallGuidanceProvider.ForReadiness(readiness);

        //Assert
        guidance.Select(g => g.Topic).Should().Equal(InstallGuidanceTopic.AddTargets, InstallGuidanceTopic.InstallAndroidNdk);
        var ndk = guidance.Single(g => g.Topic == InstallGuidanceTopic.InstallAndroidNdk);
        ndk.Commands.Should().Contain(c => c.StartsWith("sdkmanager --install", StringComparison.Ordinal));
        ndk.Commands.Should().Contain(c => c.Contains("ANDROID_NDK_HOME"));
        ndk.RequiresNewShell.Should().BeTrue();
        InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallAndroidNdk, OperatingSystemKind.Windows).Commands.Should().Contain(c => c.StartsWith("sdkmanager.bat", StringComparison.Ordinal));
    }

    [Fact]
    public void ForReadiness_is_empty_when_ready()
        => InstallGuidanceProvider.ForReadiness(ToolchainReadiness.Evaluate(TestToolchains.Complete())).Should().BeEmpty();

    [Fact]
    public void ForReadiness_puts_the_c_toolchain_before_rust()
    {
        //Arrange
        var info = TestToolchains.With(
            OperatingSystemKind.Windows,
            InstalledTool.Missing(RustTool.Cargo, "x"),
            InstalledTool.Missing(RustTool.Rustc, "x"),
            InstalledTool.Missing(RustTool.CCompiler, "x"));

        //Act
        var guidance = InstallGuidanceProvider.ForReadiness(ToolchainReadiness.Evaluate(info));

        //Assert
        guidance.Select(g => g.Topic).Should().ContainInOrder(InstallGuidanceTopic.InstallCToolchain, InstallGuidanceTopic.InstallRust);
        guidance.Should().HaveCount(2);
    }

    [Fact]
    public void ForReadiness_lists_only_the_missing_components()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Rustfmt, "not installed"));

        //Act
        var guidance = InstallGuidanceProvider.ForReadiness(ToolchainReadiness.Evaluate(info, ToolchainRequirements.WithLintAndFormat()));

        //Assert
        guidance.Should().ContainSingle().Which.Commands.Should().ContainSingle().Which.Should().Be("rustup component add rustfmt");
    }

    [Fact]
    public void ForReadiness_uses_apt_packages_for_components_without_rustup()
    {
        //Arrange
        var info = TestToolchains.With(
            OperatingSystemKind.DebianLinux,
            InstalledTool.Missing(RustTool.Rustup, "none"),
            InstalledTool.Missing(RustTool.Clippy, "none"));

        //Act
        var guidance = InstallGuidanceProvider.ForReadiness(ToolchainReadiness.Evaluate(info, ToolchainRequirements.WithLintAndFormat()));

        //Assert
        guidance.Should().ContainSingle().Which.Commands.Should().Contain("sudo apt install rust-clippy");
    }

    [Fact]
    public void ToString_includes_title_commands_and_link()
    {
        //Act
        var text = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.AddTargets, OperatingSystemKind.DebianLinux, new[] { "wasm32-unknown-unknown" }).ToString();

        //Assert
        text.Should().Contain("Add the missing compilation targets");
        text.Should().Contain("    rustup target add wasm32-unknown-unknown");
        text.Should().Contain("More information: https://");
    }

    [Fact]
    public void GetGuidance_names_rustup_and_the_debian_cross_packages_for_linux_cross_tools()
    {
        //Act
        var guidance = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallLinuxCrossTools, OperatingSystemKind.DebianLinux, new[] { "aarch64-unknown-linux-gnu", "riscv64gc-unknown-linux-gnu" });

        //Assert
        guidance.Commands.Should().Equal(
            "rustup target add aarch64-unknown-linux-gnu riscv64gc-unknown-linux-gnu",
            "sudo apt update",
            "sudo apt install gcc-aarch64-linux-gnu g++-aarch64-linux-gnu libc6-dev-arm64-cross gcc-riscv64-linux-gnu g++-riscv64-linux-gnu libc6-dev-riscv64-cross");
        guidance.RequiresAdministrator.Should().BeTrue();
    }

    [Fact]
    public void GetGuidance_defaults_the_linux_cross_tools_to_the_arm64_and_riscv64_targets()
    {
        //Act
        var guidance = InstallGuidanceProvider.GetGuidance(InstallGuidanceTopic.InstallLinuxCrossTools, OperatingSystemKind.MacOS, new[] { "not-a-triple" });

        //Assert
        guidance.Commands[0].Should().Be("rustup target add aarch64-unknown-linux-gnu riscv64gc-unknown-linux-gnu");
        guidance.Explanation.Should().Contain("CodeBrix.RustTools.Docker");
    }

    [Fact]
    public void ForReadiness_adds_the_cross_tool_guidance_after_the_target_guidance()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var readiness = ToolchainReadiness.Evaluate(TestToolchains.Complete(), ToolchainRequirements.ForLinuxCross(new[] { "riscv64gc-unknown-linux-gnu" }, sandbox.Directory));

        //Act
        var guidance = InstallGuidanceProvider.ForReadiness(readiness);

        //Assert
        guidance.Select(g => g.Topic).Should().Equal(InstallGuidanceTopic.AddTargets, InstallGuidanceTopic.InstallLinuxCrossTools);
        guidance[1].Commands.Should().Contain("sudo apt install gcc-riscv64-linux-gnu g++-riscv64-linux-gnu libc6-dev-riscv64-cross");
    }
}
