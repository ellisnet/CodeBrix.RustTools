using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Images;

public class BuildImageSpecTests
{
    [Fact]
    public void BaseImageReference_defaults_to_rust_latest()
        => new BuildImageSpec().BaseImageReference.Should().Be("rust:latest");

    [Fact]
    public void BaseImageReference_uses_the_spec_values()
        => new BuildImageSpec { BaseImage = "rust", Tag = "1-slim-bookworm" }.BaseImageReference.Should().Be("rust:1-slim-bookworm");

    [Fact]
    public void RequiresDerivedImage_is_false_for_an_empty_spec()
        => new BuildImageSpec().RequiresDerivedImage.Should().BeFalse();

    [Fact]
    public void RequiresDerivedImage_is_false_for_the_native_linux_target_only()
        => new BuildImageSpec { CrossTargets = { CrossTarget.LinuxNative }, AptPackages = { " " } }.RequiresDerivedImage.Should().BeFalse();

    [Fact]
    public void RequiresDerivedImage_is_true_for_a_concrete_linux_target_until_the_engine_s_own_is_removed()
    {
        //Arrange
        var spec = new BuildImageSpec { CrossTargets = { CrossTarget.LinuxX64, CrossTarget.LinuxNative, CrossTarget.WindowsX64Gnullvm } };

        //Act
        var onX64 = spec.WithoutNativeLinuxTargets(new DockerEngineInfo { Architecture = "x86_64" });
        var onArm64 = spec.WithoutNativeLinuxTargets(new DockerEngineInfo { Architecture = "aarch64" });

        //Assert
        spec.RequiresDerivedImage.Should().BeTrue();
        onX64.CrossTargets.Should().Equal(CrossTarget.WindowsX64Gnullvm);
        onArm64.CrossTargets.Should().Equal(CrossTarget.LinuxX64, CrossTarget.WindowsX64Gnullvm);
        new BuildImageSpec { CrossTargets = { CrossTarget.LinuxX64 } }.WithoutNativeLinuxTargets(new DockerEngineInfo { Architecture = "x86_64" }).RequiresDerivedImage.Should().BeFalse();
        spec.CrossTargets.Should().HaveCount(3, "the original spec is not modified");
    }

    [Fact]
    public void RequiresDerivedImage_is_true_for_any_layer()
    {
        //Assert
        new BuildImageSpec { RustVersion = "1.94.1" }.RequiresDerivedImage.Should().BeTrue();
        new BuildImageSpec { AptPackages = { "cmake" } }.RequiresDerivedImage.Should().BeTrue();
        new BuildImageSpec { InstallClippyRustfmt = true }.RequiresDerivedImage.Should().BeTrue();
        new BuildImageSpec { CrossTargets = { CrossTarget.WindowsX64Gnullvm } }.RequiresDerivedImage.Should().BeTrue();
        new BuildImageSpec { ExtraDockerfileLines = { "LABEL a=b" } }.RequiresDerivedImage.Should().BeTrue();
    }

    [Fact]
    public void Clone_copies_the_collections()
    {
        //Arrange
        var spec = new BuildImageSpec { AptPackages = { "cmake" }, CrossTargets = { CrossTarget.AndroidArm64 }, ExtraDockerfileLines = { "LABEL a=b" } };

        //Act
        var copy = spec.Clone();
        copy.AptPackages.Add("pkg-config");
        copy.CrossTargets.Add(CrossTarget.WindowsX64Gnullvm);
        copy.ExtraDockerfileLines.Clear();

        //Assert
        spec.AptPackages.Should().Equal("cmake");
        spec.CrossTargets.Should().Equal(CrossTarget.AndroidArm64);
        spec.ExtraDockerfileLines.Should().Equal("LABEL a=b");
    }

    [Fact]
    public void WithDefaults_fills_unset_values_from_the_workbench_options()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { DefaultImage = "rust", DefaultTag = "1-bookworm", BuildImageRepository = "my-images" };

        //Act
        var resolved = new BuildImageSpec().WithDefaults(options);
        var kept = new BuildImageSpec { Tag = "latest", ImageName = "mine" }.WithDefaults(options);

        //Assert
        resolved.BaseImageReference.Should().Be("rust:1-bookworm");
        resolved.ImageName.Should().Be("my-images");
        kept.BaseImageReference.Should().Be("rust:latest");
        kept.ImageName.Should().Be("mine");
    }

    [Fact]
    public void WithDefaults_takes_the_image_name_from_the_label_prefix_when_the_repository_is_unset()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { LabelPrefix = "codebrix.rusttools.scratch" };

        //Act
        var resolved = new BuildImageSpec().WithDefaults(options);

        //Assert
        resolved.ImageName.Should().Be("codebrix-rusttools-scratch");
    }
}
