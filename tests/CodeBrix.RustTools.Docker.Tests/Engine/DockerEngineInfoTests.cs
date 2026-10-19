using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Engine;

public class DockerEngineInfoTests
{
    private static readonly DockerEngineInfo X64Engine = new DockerEngineInfo { IsAvailable = true, Architecture = "x86_64", ServerVersion = "29.0.0", OsType = "linux" };

    [Fact]
    public void NativePlatform_follows_the_engine_architecture()
    {
        //Assert
        X64Engine.NativePlatform.Should().Be("linux/amd64");
        new DockerEngineInfo { Architecture = "aarch64" }.NativePlatform.Should().Be("linux/arm64");
        new DockerEngineInfo().NativePlatform.Should().BeNull();
    }

    [Fact]
    public void IsNativePlatform_is_true_for_the_engine_s_own_and_for_none()
    {
        //Assert
        X64Engine.IsNativePlatform(null).Should().BeTrue();
        X64Engine.IsNativePlatform("linux/amd64").Should().BeTrue();
        X64Engine.IsNativePlatform("linux/arm64").Should().BeFalse();
        X64Engine.IsNativePlatform("linux/riscv64").Should().BeFalse();
    }

    [Fact]
    public void ForContainerPlatform_makes_the_platform_s_linux_target_the_native_one()
    {
        //Act
        var arm64 = X64Engine.ForContainerPlatform("linux/arm64");
        var riscv64 = X64Engine.ForContainerPlatform("linux/riscv64");

        //Assert
        X64Engine.ForContainerPlatform(null).Should().BeSameAs(X64Engine);
        X64Engine.ForContainerPlatform("linux/amd64").Should().BeSameAs(X64Engine);
        arm64.Architecture.Should().Be("aarch64");
        arm64.NativeLinuxTriple.Should().Be("aarch64-unknown-linux-gnu");
        arm64.ServerVersion.Should().Be("29.0.0");
        arm64.IsAvailable.Should().BeTrue();
        CrossTargetInfo.NativeLinuxTargetFor(arm64).Should().Be(CrossTarget.LinuxArm64);
        CrossTargetInfo.NativeLinuxTargetFor(riscv64).Should().Be(CrossTarget.LinuxRiscV64);
        X64Engine.Architecture.Should().Be("x86_64", "the engine itself is not changed");
    }
}
