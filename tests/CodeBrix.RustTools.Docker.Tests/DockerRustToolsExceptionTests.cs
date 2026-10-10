using System;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests;

public class DockerRustToolsExceptionTests
{
    [Fact]
    public void every_exception_derives_from_the_phase_one_base()
    {
        //Assert
        new DockerRustToolsException().Should().BeAssignableTo<RustToolsException>();
        new DockerUnavailableException("x").Should().BeAssignableTo<DockerRustToolsException>();
        new ContainerBuildException("x").Should().BeAssignableTo<DockerRustToolsException>();
        new ArtifactRetrievalException("x").Should().BeAssignableTo<DockerRustToolsException>();
        new CrossTargetNotSupportedException(CrossTarget.MacOSX64).Should().BeAssignableTo<DockerRustToolsException>();
    }

    [Fact]
    public void DockerUnavailableException_never_has_null_guidance()
        => new DockerUnavailableException("down").Guidance.Should().BeEmpty();

    [Fact]
    public void DockerUnavailableException_carries_engine_and_guidance()
    {
        //Arrange
        var info = new DockerEngineInfo { Issues = new[] { DockerEngineIssue.NotReachable }, HostOperatingSystem = OperatingSystemKind.MacOS };
        var guidance = DockerGuidanceProvider.ForEngine(info);
        var inner = new InvalidOperationException("socket");

        //Act
        var ex = new DockerUnavailableException("down", info, guidance, inner);

        //Assert
        ex.Engine.Should().BeSameAs(info);
        ex.Guidance.Should().HaveCount(guidance.Count);
        ex.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void ContainerBuildException_carries_the_failed_command()
    {
        //Act
        var ex = new ContainerBuildException("failed", new[] { "cargo", "build" }, 101, "out", "err");

        //Assert
        ex.Command.Should().Equal("cargo", "build");
        ex.ExitCode.Should().Be(101);
        ex.StandardOutput.Should().Be("out");
        ex.StandardError.Should().Be("err");
    }

    [Fact]
    public void ContainerBuildException_without_a_command_has_no_exit_code()
    {
        //Act
        var ex = new ContainerBuildException("failed", new InvalidOperationException());

        //Assert
        ex.ExitCode.Should().BeNull();
        ex.Command.Should().BeNull();
        ex.StandardError.Should().BeEmpty();
    }

    [Fact]
    public void ArtifactRetrievalException_names_the_container_path()
        => new ArtifactRetrievalException("missing", "/work/target/release/app").ContainerPath.Should().Be("/work/target/release/app");

    [Fact]
    public void CrossTargetNotSupportedException_explains_the_target()
    {
        //Act
        var ex = new CrossTargetNotSupportedException(CrossTarget.MacOSArm64);

        //Assert
        ex.Target.Should().Be(CrossTarget.MacOSArm64);
        ex.Message.Should().Contain("aarch64-apple-darwin");
    }
}
