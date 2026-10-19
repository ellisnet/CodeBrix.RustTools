using System.IO;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Internal;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Images;

public class RustBuildImageBuilderTests
{
    private static readonly ResourceLabels Labels = new ResourceLabels("codebrix.rusttools.docker", "wb1");

    [Fact]
    public void the_containerfile_build_passes_the_spec_platform_as_build_platform()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var containerfile = sandbox.WriteFile("Containerfile", "FROM alpine:latest\n");
        var spec = new BuildImageSpec { Containerfile = containerfile, Platform = "Linux/ARM64", ContainerfileBuildArgs = { ["A"] = "1" } };

        //Act
        var build = RustBuildImageBuilder.ContainerfileBuildSpec(spec, "repo:containerfile-0123", "0123", Labels, null);
        var plain = RustBuildImageBuilder.ContainerfileBuildSpec(new BuildImageSpec { Containerfile = containerfile }, "repo:containerfile-0456", "0456", Labels, null);

        //Assert
        build.Platform.Should().Be("linux/arm64");
        build.Tags.Should().Equal("repo:containerfile-0123");
        build.DockerfilePath.Should().Be(Path.GetFullPath(containerfile));
        build.BuildArgs["A"].Should().Be("1");
        build.Labels["codebrix.rusttools.docker.spec-hash"].Should().Be("0123");
        build.Labels["codebrix.rusttools.docker.kind"].Should().Be("image");
        plain.Platform.Should().BeNull();
    }

    [Fact]
    public void the_dockerfile_build_passes_the_platform_as_build_platform()
    {
        //Act
        var build = RustBuildImageBuilder.DockerfileBuildSpec("/tmp/ctx", "repo:abc", "abc", "linux/riscv64", Labels, null);
        var plain = RustBuildImageBuilder.DockerfileBuildSpec("/tmp/ctx", "repo:abc", "abc", null, Labels, null);

        //Assert
        build.Platform.Should().Be("linux/riscv64");
        build.ContextDirectory.Should().Be("/tmp/ctx");
        build.Labels["codebrix.rusttools.docker.spec-hash"].Should().Be("abc");
        plain.Platform.Should().BeNull();
    }

    [Fact]
    public void the_base_image_pull_carries_the_platform_and_keeps_the_plain_pull_without_one()
    {
        //Assert
        RustBuildImageBuilder.PullOptionsFor("linux/arm64").Platform.Should().Be("linux/arm64");
        RustBuildImageBuilder.PullOptionsFor(" LINUX/riscv64 ").Platform.Should().Be("linux/riscv64");
        RustBuildImageBuilder.PullOptionsFor(null).Should().BeNull();
        RustBuildImageBuilder.PullOptionsFor("  ").Should().BeNull();
    }
}
