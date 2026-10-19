using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
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
    [InlineData(CrateTarget.LinuxArm64, false)]
    [InlineData(CrateTarget.LinuxRiscV64, false)]
    [InlineData(CrateTarget.WinX64, false)]
    public void only_linux_x64_has_an_image(CrateTarget target, bool expected) =>
        ManyLinuxImages.HasImage(target).Should().Be(expected);

    [Fact]
    public void there_is_no_image_for_linux_arm64() =>
        ((Action)(() => ManyLinuxImages.ReadContainerfile(CrateTarget.LinuxArm64))).Should().Throw<ArgumentException>();

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
