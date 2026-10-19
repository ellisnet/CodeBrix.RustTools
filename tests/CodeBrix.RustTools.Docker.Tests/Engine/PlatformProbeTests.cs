using System.Threading.Tasks;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Internal;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Engine;

public class PlatformProbeTests
{
    private static readonly DockerEngineInfo X64Engine = new DockerEngineInfo { IsAvailable = true, Architecture = "x86_64", HostOperatingSystem = OperatingSystemKind.DebianLinux };

    [Fact]
    public async Task the_engine_s_own_platform_is_native_without_starting_anything()
    {
        //Act (no client: nothing may contact an engine)
        var result = await PlatformProbe.RunAsync(null, X64Engine, new ResourceLabels("p", "w"), "box", "linux/amd64", "busybox:latest", TestContext.Current.CancellationToken);
        var none = await PlatformProbe.RunAsync(null, X64Engine, new ResourceLabels("p", "w"), "box", null, "busybox:latest", TestContext.Current.CancellationToken);

        //Assert
        result.Outcome.Should().Be(PlatformProbeOutcome.Native);
        result.CanRun.Should().BeTrue();
        result.IsEmulated.Should().BeFalse();
        result.Guidance.Should().BeNull();
        none.Outcome.Should().Be(PlatformProbeOutcome.Native);
        none.Platform.Should().BeNull();
    }

    [Fact]
    public void an_exec_format_error_means_no_emulator_and_carries_the_binfmt_guidance()
    {
        //Act
        var result = PlatformProbe.Classify("linux/arm64", "busybox:latest", X64Engine, 255, "exec /bin/uname: exec format error", "`uname -m` exited with code 255: exec /bin/uname: exec format error");

        //Assert
        result.Outcome.Should().Be(PlatformProbeOutcome.NoEmulator);
        result.CanRun.Should().BeFalse();
        result.IsEmulated.Should().BeTrue();
        result.Message.Should().Contain("qemu-user binfmt handler for aarch64");
        result.Message.Should().Contain("/proc/sys/fs/binfmt_misc/qemu-aarch64");
        result.Guidance.Topic.Should().Be(DockerGuidanceTopic.EnableForeignPlatforms);
        result.Guidance.Commands.Should().Contain("sudo apt install qemu-user-static binfmt-support");
    }

    [Fact]
    public void an_exec_format_error_at_start_is_also_no_emulator()
        => PlatformProbe.Classify("linux/riscv64", "busybox:latest", X64Engine, -1, "failed to create task: exec format error", "could not be started").Outcome.Should().Be(PlatformProbeOutcome.NoEmulator);

    [Fact]
    public void a_refused_creation_is_refused_and_a_failed_run_is_failed()
    {
        //Act
        var refused = PlatformProbe.Classify("linux/arm64", "busybox:latest", X64Engine, -1, "image does not match the specified platform", "could not be created");
        var failed = PlatformProbe.Classify("linux/arm64", "busybox:latest", X64Engine, 1, "uname: broken", "exited with code 1");

        //Assert
        refused.Outcome.Should().Be(PlatformProbeOutcome.Refused);
        refused.CanRun.Should().BeFalse();
        refused.Guidance.Should().BeNull();
        failed.Outcome.Should().Be(PlatformProbeOutcome.Failed);
        failed.Guidance.Should().BeNull();
    }
}
