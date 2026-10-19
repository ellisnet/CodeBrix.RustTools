using System;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Engine;

public class DockerPlatformsTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" Linux/ARM64 ", "linux/arm64")]
    [InlineData("linux/arm64/v8", "linux/arm64/v8")]
    [InlineData("linux/riscv64", "linux/riscv64")]
    public void Normalize_trims_and_lower_cases_and_treats_blank_as_the_engine_s_own(string value, string expected)
        => DockerPlatforms.Normalize(value).Should().Be(expected);

    [Theory]
    [InlineData("linux/amd64,linux/arm64")]
    [InlineData("arm64")]
    [InlineData("linux/arm64/v8/extra")]
    [InlineData("linux arm64")]
    public void Normalize_rejects_lists_and_malformed_values(string value)
        => ((Action)(() => DockerPlatforms.Normalize(value))).Should().Throw<ArgumentException>();

    [Theory]
    [InlineData("linux/amd64", "x86_64")]
    [InlineData("linux/arm64", "aarch64")]
    [InlineData("linux/arm64/v8", "aarch64")]
    [InlineData("linux/riscv64", "riscv64")]
    [InlineData("linux/s390x", "s390x")]
    [InlineData(null, null)]
    public void MachineOf_names_the_machine_uname_reports(string platform, string expected)
        => DockerPlatforms.MachineOf(platform).Should().Be(expected);

    [Theory]
    [InlineData("x86_64", "linux/amd64")]
    [InlineData("amd64", "linux/amd64")]
    [InlineData("aarch64", "linux/arm64")]
    [InlineData("riscv64", "linux/riscv64")]
    [InlineData("s390x", null)]
    [InlineData(null, null)]
    public void ForArchitecture_maps_the_engine_architecture_to_its_linux_platform(string architecture, string expected)
        => DockerPlatforms.ForArchitecture(architecture).Should().Be(expected);

    [Fact]
    public void ForLinuxTarget_maps_the_three_linux_targets_and_nothing_else()
    {
        //Assert
        DockerPlatforms.ForLinuxTarget(CrossTarget.LinuxX64).Should().Be(DockerPlatforms.LinuxAmd64);
        DockerPlatforms.ForLinuxTarget(CrossTarget.LinuxArm64).Should().Be(DockerPlatforms.LinuxArm64);
        DockerPlatforms.ForLinuxTarget(CrossTarget.LinuxRiscV64).Should().Be(DockerPlatforms.LinuxRiscV64);
        DockerPlatforms.ForLinuxTarget(CrossTarget.WindowsX64Gnullvm).Should().BeNull();
        DockerPlatforms.ForLinuxTarget(CrossTarget.LinuxNative).Should().BeNull();
    }

    [Fact]
    public void AreSame_ignores_the_variant_and_the_spelling()
    {
        //Assert
        DockerPlatforms.AreSame("linux/arm64/v8", "LINUX/arm64").Should().BeTrue();
        DockerPlatforms.AreSame("linux/arm64", "linux/aarch64").Should().BeTrue();
        DockerPlatforms.AreSame("linux/arm64", "linux/amd64").Should().BeFalse();
        DockerPlatforms.AreSame(null, null).Should().BeTrue();
        DockerPlatforms.AreSame(null, "linux/amd64").Should().BeFalse();
    }
}
