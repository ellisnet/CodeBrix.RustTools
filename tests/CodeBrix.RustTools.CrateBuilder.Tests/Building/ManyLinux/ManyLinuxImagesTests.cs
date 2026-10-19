using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building.ManyLinux;

public class ManyLinuxImagesTests
{
    [Fact]
    public void the_x64_containerfile_is_embedded_and_pins_the_digest_rustup_and_rust()
    {
        //Act
        var text = ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxX64);

        //Assert
        text.Should().Contain("ARG BASE_IMAGE=" + ManyLinuxImages.X64BaseImage);
        text.Should().Contain("ARG RUST_VERSION=" + ManyLinuxImages.RustVersion);
        text.Should().Contain("ARG RUSTUP_VERSION=" + ManyLinuxImages.RustupVersion);
        text.Should().Contain("ARG RUSTUP_INIT_SHA256=" + ManyLinuxImages.RustupInitSha256X64);
        text.Should().Contain("sha256sum -c -");
        text.Should().Contain("CARGO_HOME=/usr/local/cargo");
        text.Should().NotContain("\r");
    }

    [Fact]
    public void the_build_arguments_repeat_every_pin()
    {
        //Act
        var arguments = ManyLinuxImages.BuildArguments(CrateTarget.LinuxX64);

        //Assert
        arguments["BASE_IMAGE"].Should().Be(ManyLinuxImages.X64BaseImage);
        arguments["RUST_VERSION"].Should().Be(ManyLinuxImages.RustVersion);
        arguments["RUSTUP_VERSION"].Should().Be(ManyLinuxImages.RustupVersion);
        arguments["RUSTUP_INIT_SHA256"].Should().Be(ManyLinuxImages.RustupInitSha256X64);
        arguments["RUSTUP_HOST"].Should().Be("x86_64-unknown-linux-gnu");
        ManyLinuxImages.GlibcFloor(CrateTarget.LinuxX64).Should().Be("2.28");
    }

    [Theory]
    [InlineData(CrateTarget.LinuxX64, true)]
    [InlineData(CrateTarget.LinuxArm64, true)]
    [InlineData(CrateTarget.LinuxRiscV64, true)]
    [InlineData(CrateTarget.WinX64, false)]
    [InlineData(CrateTarget.OsxArm64, false)]
    [InlineData(CrateTarget.AndroidArm64, false)]
    public void the_three_linux_targets_have_an_image(CrateTarget target, bool expected) =>
        ManyLinuxImages.HasImage(target).Should().Be(expected);

    [Fact]
    public void there_is_no_image_for_a_non_linux_target() =>
        ((Action)(() => ManyLinuxImages.ReadContainerfile(CrateTarget.WinX64))).Should().Throw<ArgumentException>().WithMessage("*" + ManyLinuxImages.NonLinuxReason + "*");

    [Theory]
    [InlineData(CrateTarget.LinuxArm64, ManyLinuxImages.Arm64BaseImage, ManyLinuxImages.RustupInitSha256Arm64, "aarch64-unknown-linux-gnu", "Containerfile.aarch64", "linux/arm64")]
    [InlineData(CrateTarget.LinuxRiscV64, ManyLinuxImages.RiscV64BaseImage, ManyLinuxImages.RustupInitSha256RiscV64, "riscv64gc-unknown-linux-gnu", "Containerfile.riscv64", "linux/riscv64")]
    [InlineData(CrateTarget.LinuxX64, ManyLinuxImages.X64BaseImage, ManyLinuxImages.RustupInitSha256X64, "x86_64-unknown-linux-gnu", "Containerfile.x86_64", "linux/amd64")]
    public void each_containerfile_is_embedded_and_pins_its_own_digest_rustup_and_host(CrateTarget target, string baseImage, string rustupSha, string host, string fileName, string platform)
    {
        //Act
        var text = ManyLinuxImages.ReadContainerfile(target);
        var arguments = ManyLinuxImages.BuildArguments(target);

        //Assert
        ManyLinuxImages.ContainerfileName(target).Should().Be(fileName);
        ManyLinuxImages.PlatformOf(target).Should().Be(platform);
        text.Should().Contain("ARG BASE_IMAGE=" + baseImage);
        text.Should().Contain("ARG RUST_VERSION=" + ManyLinuxImages.RustVersion);
        text.Should().Contain("ARG RUSTUP_VERSION=" + ManyLinuxImages.RustupVersion);
        text.Should().Contain("ARG RUSTUP_INIT_SHA256=" + rustupSha);
        text.Should().Contain("ARG RUSTUP_HOST=" + host);
        text.Should().Contain("sha256sum -c -");
        text.Should().Contain("CARGO_HOME=/usr/local/cargo");
        text.Should().NotContain("\r");
        arguments["BASE_IMAGE"].Should().Be(baseImage);
        arguments["RUSTUP_INIT_SHA256"].Should().Be(rustupSha);
        arguments["RUSTUP_HOST"].Should().Be(host);
        arguments["RUST_VERSION"].Should().Be(ManyLinuxImages.RustVersion);
    }

    [Fact]
    public void the_foreign_architecture_containerfiles_state_their_platform_and_the_binfmt_prerequisite()
    {
        //Assert
        ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxArm64).Should().Contain("/proc/sys/fs/binfmt_misc/qemu-aarch64");
        ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxRiscV64).Should().Contain("/proc/sys/fs/binfmt_misc/qemu-riscv64");
        ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxRiscV64).Should().Contain("glibc 2.39");
    }

    [Theory]
    [InlineData(CrateTarget.LinuxX64, "2.28")]
    [InlineData(CrateTarget.LinuxArm64, "2.28")]
    [InlineData(CrateTarget.LinuxRiscV64, "2.39")]
    public void each_target_has_its_own_glibc_floor(CrateTarget target, string floor) =>
        ManyLinuxImages.GlibcFloor(target).Should().Be(floor);

    [Fact]
    public void CreateSpec_sets_the_platform_for_arm64_and_riscv64_and_leaves_x64_unchanged()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var x64 = ManyLinuxImages.CreateSpec(CrateTarget.LinuxX64, Path.Combine(sandbox.Directory, "x64"));
        var arm64 = ManyLinuxImages.CreateSpec(CrateTarget.LinuxArm64, Path.Combine(sandbox.Directory, "arm64"));
        var riscv64 = ManyLinuxImages.CreateSpec(CrateTarget.LinuxRiscV64, Path.Combine(sandbox.Directory, "riscv64"));

        //Assert
        x64.Platform.Should().BeNull();
        arm64.Platform.Should().Be("linux/arm64");
        riscv64.Platform.Should().Be("linux/riscv64");
        Path.GetFileName(arm64.Containerfile).Should().Be("Containerfile.aarch64");
        File.ReadAllText(riscv64.Containerfile).Should().Be(ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxRiscV64));
        arm64.ContainerfileBuildArgs["BASE_IMAGE"].Should().Be(ManyLinuxImages.Arm64BaseImage);
    }

    [Fact]
    public void EmulationUnavailableReason_names_qemu_user_binfmt_the_probe_and_the_guidance()
    {
        //Arrange
        var engine = new DockerEngineInfo { Architecture = "x86_64", HostOperatingSystem = OperatingSystemKind.DebianLinux };
        var probe = new PlatformProbeResult { Platform = "linux/arm64", Outcome = PlatformProbeOutcome.NoEmulator, Message = "linux/arm64 programs cannot execute on this engine (x86_64): exec format error." };

        //Act
        var reason = ManyLinuxImages.EmulationUnavailableReason(CrateTarget.LinuxArm64, engine, probe);

        //Assert
        reason.Should().Contain("linux-arm64");
        reason.Should().Contain("qemu-user binfmt handler for aarch64");
        reason.Should().Contain("/proc/sys/fs/binfmt_misc/qemu-aarch64");
        reason.Should().Contain("exec format error");
        reason.Should().Contain("sudo apt install qemu-user-static binfmt-support");
        reason.Should().Contain("LinuxBaseline.RustImage");
    }

    [Fact]
    public void CreateSpec_writes_the_containerfile_into_its_own_context_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = Path.Combine(sandbox.Directory, "ctx");

        //Act
        var spec = ManyLinuxImages.CreateSpec(CrateTarget.LinuxX64, folder);

        //Assert
        spec.UsesContainerfile.Should().BeTrue();
        spec.ContainerfileContext.Should().Be(folder);
        File.ReadAllText(spec.Containerfile).Should().Be(ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxX64));
        Directory.GetFiles(folder).Should().HaveCount(1);
        spec.ContainerfileBuildArgs["BASE_IMAGE"].Should().Be(ManyLinuxImages.X64BaseImage);
        spec.BaseImage.Should().BeNull();
    }
}
