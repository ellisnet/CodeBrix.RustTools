using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Builds;

public class ContainerBuildPipelineTests
{
    [Fact]
    public void ImageSpecFor_adds_the_layers_the_targets_need()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.LinuxX64, CrossTarget.WindowsX64Gnullvm, CrossTarget.AndroidArm64 },
            ImageSpec = new BuildImageSpec { AptPackages = { "pkg-config" } },
        };

        //Act
        var spec = ContainerBuildPipeline.ImageSpecFor(request);

        //Assert
        spec.CrossTargets.Should().Equal(CrossTarget.LinuxX64, CrossTarget.WindowsX64Gnullvm, CrossTarget.AndroidArm64);
        spec.AptPackages.Should().Equal("pkg-config");
        ContainerBuildPipeline.ImageSpecFor(request, X64Engine).CrossTargets.Should().Equal(CrossTarget.WindowsX64Gnullvm, CrossTarget.AndroidArm64);
        request.ImageSpec.CrossTargets.Should().BeEmpty();
    }

    private static readonly DockerEngineInfo X64Engine = new DockerEngineInfo { Architecture = "x86_64" };

    private static readonly DockerEngineInfo Arm64Engine = new DockerEngineInfo { Architecture = "aarch64" };

    [Fact]
    public void BuildTargetsFor_expands_the_universal_target_into_its_slices_once()
        => ContainerBuildPipeline.BuildTargetsFor(new[] { CrossTarget.MacOSUniversal, CrossTarget.MacOSArm64, CrossTarget.LinuxX64, CrossTarget.MacOSUniversal }, X64Engine)
            .Should().Equal(CrossTarget.MacOSArm64, CrossTarget.MacOSX64, CrossTarget.LinuxX64);

    [Fact]
    public void BuildTargetsFor_resolves_the_native_target_from_the_engine_and_merges_it_with_an_explicit_one()
    {
        //Assert
        ContainerBuildPipeline.BuildTargetsFor(new[] { CrossTarget.LinuxNative }, X64Engine).Should().Equal(CrossTarget.LinuxX64);
        ContainerBuildPipeline.BuildTargetsFor(new[] { CrossTarget.LinuxNative, CrossTarget.LinuxArm64 }, Arm64Engine).Should().Equal(CrossTarget.LinuxArm64);
        ContainerBuildPipeline.BuildTargetsFor(new[] { CrossTarget.LinuxX64, CrossTarget.LinuxNative }, Arm64Engine).Should().Equal(CrossTarget.LinuxX64, CrossTarget.LinuxArm64);
    }

    [Fact]
    public void ImageSpecFor_adds_cross_layers_only_for_linux_targets_that_are_not_the_engine_s_own()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.LinuxNative, CrossTarget.LinuxX64, CrossTarget.LinuxArm64, CrossTarget.LinuxRiscV64 },
        };

        //Assert
        ContainerBuildPipeline.ImageSpecFor(request, X64Engine).CrossTargets.Should().Equal(CrossTarget.LinuxArm64, CrossTarget.LinuxRiscV64);
        ContainerBuildPipeline.ImageSpecFor(request, Arm64Engine).CrossTargets.Should().Equal(CrossTarget.LinuxX64, CrossTarget.LinuxRiscV64);
        ContainerBuildPipeline.ImageSpecFor(new ContainerBuildRequest { ProjectDirectory = "/a" }, Arm64Engine).RequiresDerivedImage.Should().BeFalse();
    }

    [Fact]
    public void ImageSpecFor_adds_the_macos_layer_for_the_universal_target()
        => ContainerBuildPipeline.ImageSpecFor(new ContainerBuildRequest { ProjectDirectory = "/a", Targets = new List<CrossTarget> { CrossTarget.MacOSUniversal } })
            .CrossTargets.Should().Equal(CrossTarget.MacOSUniversal);

    [Fact]
    public void ImageSpecFor_uses_the_base_image_for_a_linux_build()
        => ContainerBuildPipeline.ImageSpecFor(new ContainerBuildRequest { ProjectDirectory = "/a" }).RequiresDerivedImage.Should().BeFalse();

    [Fact]
    public void ImageSpecFor_does_not_repeat_a_target_already_in_the_spec()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.WindowsX64Gnullvm },
            ImageSpec = new BuildImageSpec { CrossTargets = { CrossTarget.WindowsX64Gnullvm } },
        };

        //Act
        var spec = ContainerBuildPipeline.ImageSpecFor(request);

        //Assert
        spec.CrossTargets.Should().Equal(CrossTarget.WindowsX64Gnullvm);
    }

    [Fact]
    public void ContainerDirectoryFor_maps_the_root_to_work_and_a_member_below_it()
    {
        //Arrange
        var root = Path.Combine(Path.GetTempPath(), "ws");

        //Assert
        ContainerBuildPipeline.ContainerDirectoryFor(root, root).Should().Be("/work");
        ContainerBuildPipeline.ContainerDirectoryFor(root, Path.Combine(root, "crates", "cli")).Should().Be("/work/crates/cli");
    }

    [Fact]
    public void ContainerBuildResult_ToString_names_the_outcome()
        => new ContainerBuildResult { Success = false, FailedStage = BuildStage.AuditingLicenses }.ToString()
            .Should().StartWith("Container build failed in AuditingLicenses");

    [Theory]
    [InlineData(CrossTarget.LinuxArm64, "aarch64-unknown-linux-gnu")]
    [InlineData(CrossTarget.WindowsX64Gnullvm, "x86_64-pc-windows-gnullvm")]
    [InlineData(CrossTarget.LinuxNative, null)]
    public void MetadataOptionsFor_filters_the_graph_to_the_target_triple(CrossTarget target, string expected)
    {
        //Arrange
        var options = new CargoBuildOptions { Offline = true, AllFeatures = true };

        //Act
        var metadataOptions = ContainerBuildPipeline.MetadataOptionsFor(options, target);

        //Assert
        metadataOptions.FilterPlatform.Should().Be(expected);
        metadataOptions.Offline.Should().BeTrue();
        metadataOptions.AllFeatures.Should().BeTrue();
    }

    [Fact]
    public void CopyRootFor_uses_the_copy_root_only_when_it_contains_the_workspace_root()
    {
        //Arrange
        var root = System.IO.Path.GetFullPath("/stage");
        var inside = new ContainerBuildRequest { ProjectDirectory = "/stage/src/crate", CopyRootDirectory = "/stage/" };
        var outside = new ContainerBuildRequest { ProjectDirectory = "/elsewhere", CopyRootDirectory = "/stage" };
        var act = () => ContainerBuildPipeline.CopyRootFor(outside, System.IO.Path.GetFullPath("/elsewhere"));

        //Act + Assert
        ContainerBuildPipeline.CopyRootFor(inside, System.IO.Path.GetFullPath("/stage/src/crate")).Should().Be(root);
        ContainerBuildPipeline.CopyRootFor(inside, root).Should().Be(root);
        ContainerBuildPipeline.CopyRootFor(new ContainerBuildRequest { ProjectDirectory = "/w" }, "/w").Should().Be("/w");
        act.Should().Throw<ArgumentException>().WithMessage("*does not contain*");
        ContainerBuildPipeline.ContainerDirectoryFor(root, System.IO.Path.GetFullPath("/stage/src/crate")).Should().Be("/work/src/crate");
    }

    [Fact]
    public void OptionsForTarget_adds_only_that_targets_environment()
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.Environment["SHARED"] = "1";
        var request = new ContainerBuildRequest { ProjectDirectory = "/a", Targets = { CrossTarget.WindowsX64Gnullvm, CrossTarget.MacOSUniversal } };
        request.TargetEnvironment[CrossTarget.LinuxNative] = new System.Collections.Generic.Dictionary<string, string> { ["NATIVE"] = "n", ["SHARED"] = "2" };
        request.TargetEnvironment[CrossTarget.MacOSUniversal] = new System.Collections.Generic.Dictionary<string, string> { ["MAC"] = "m" };

        //Act
        var linux = ContainerBuildPipeline.OptionsForTarget(request, options, CrossTarget.LinuxX64, X64Engine);
        var mac = ContainerBuildPipeline.OptionsForTarget(request, options, CrossTarget.MacOSX64, X64Engine);
        var windows = ContainerBuildPipeline.OptionsForTarget(request, options, CrossTarget.WindowsX64Gnullvm, X64Engine);

        //Assert
        linux.Environment["NATIVE"].Should().Be("n");
        linux.Environment["SHARED"].Should().Be("2");
        mac.Environment["MAC"].Should().Be("m");
        mac.Environment.ContainsKey("NATIVE").Should().BeFalse();
        windows.Should().BeSameAs(options);
        options.Environment["SHARED"].Should().Be("1");
    }

    [Fact]
    public void a_request_for_another_platform_builds_its_linux_target_natively_in_that_platform_s_container()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.LinuxNative, CrossTarget.LinuxArm64, CrossTarget.LinuxX64 },
            ImageSpec = new BuildImageSpec { Platform = "linux/arm64" },
        };

        //Act
        var platform = ContainerBuildPipeline.PlatformFor(request);
        var buildEngine = X64Engine.ForContainerPlatform(platform);
        var spec = ContainerBuildPipeline.ImageSpecFor(request, buildEngine);
        var targets = ContainerBuildPipeline.BuildTargetsFor(request.Targets, buildEngine);

        //Assert
        platform.Should().Be("linux/arm64");
        spec.Platform.Should().Be("linux/arm64");
        spec.CrossTargets.Should().Equal(CrossTarget.LinuxX64);
        targets.Should().Equal(CrossTarget.LinuxArm64, CrossTarget.LinuxX64);
    }

    [Fact]
    public void PlatformFor_takes_the_container_options_before_the_image_spec()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            ImageSpec = new BuildImageSpec { Platform = "linux/arm64" },
            ContainerOptions = new BuildContainerOptions { Platform = "linux/riscv64" },
        };

        //Assert
        ContainerBuildPipeline.PlatformFor(request).Should().Be("linux/riscv64");
        ContainerBuildPipeline.PlatformFor(new ContainerBuildRequest { ProjectDirectory = "/a" }).Should().BeNull();
    }
}
