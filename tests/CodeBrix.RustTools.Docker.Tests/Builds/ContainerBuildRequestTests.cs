using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Builds;

public class ContainerBuildRequestTests
{
    [Fact]
    public void defaults_build_for_linux_and_remove_the_target_volume()
    {
        //Act
        var request = new ContainerBuildRequest();

        //Assert
        request.Targets.Should().Equal(CrossTarget.LinuxNative);
        request.DestroyContainerAfterBuild.Should().BeNull();
        request.RemoveTargetVolumeAfterBuild.Should().BeTrue();
        request.ContinueOnTargetFailure.Should().BeFalse();
        request.Retrieval.Should().BeNull();
        request.Timeout.Should().BeNull();
        request.ExtraExcludePatterns.Should().BeEmpty();
    }

    [Fact]
    public void Validate_accepts_a_project_directory()
    {
        //Act
        var act = () => new ContainerBuildRequest { ProjectDirectory = "/src/app" }.Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_requires_exactly_one_source()
    {
        //Act
        var none = () => new ContainerBuildRequest().Validate();
        var two = () => new ContainerBuildRequest { ProjectDirectory = "/src/app", ArchivePath = "/src/app.zip" }.Validate();

        //Assert
        none.Should().Throw<ArgumentException>().WithMessage("*exactly one*");
        two.Should().Throw<ArgumentException>().WithMessage("*exactly one*");
    }

    [Fact]
    public void Validate_requires_targets_without_repeats()
    {
        //Act
        var empty = () => new ContainerBuildRequest { ProjectDirectory = "/a", Targets = new List<CrossTarget>() }.Validate();
        var twice = () => new ContainerBuildRequest { ProjectDirectory = "/a", Targets = { CrossTarget.LinuxNative } }.Validate();

        //Assert
        empty.Should().Throw<ArgumentException>().WithMessage("*at least one*");
        twice.Should().Throw<ArgumentException>().WithMessage("*twice*");
    }

    [Fact]
    public void Validate_accepts_the_macos_targets()
    {
        //Act
        var act = () => new ContainerBuildRequest { ProjectDirectory = "/a", Targets = new List<CrossTarget> { CrossTarget.MacOSX64, CrossTarget.MacOSArm64 } }.Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_accepts_the_flat_layout_for_the_universal_target_alone()
    {
        //Act
        var act = () => new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.MacOSUniversal },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = "/out", Layout = ArtifactLayout.Flat },
        }.Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_requires_a_host_directory_for_retrieval()
    {
        //Act
        var act = () => new ContainerBuildRequest { ProjectDirectory = "/a", Retrieval = new ArtifactRetrievalOptions() }.Validate();

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*HostDirectory*");
    }

    [Fact]
    public void Validate_rejects_the_flat_layout_for_several_targets()
    {
        //Arrange
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.AndroidArm64, CrossTarget.AndroidX64 },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = "/out", Layout = ArtifactLayout.Flat },
        };

        //Act
        var act = () => request.Validate();

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*Flat*");
    }

    [Fact]
    public void Clone_copies_the_lists()
    {
        //Arrange
        var request = new ContainerBuildRequest { ProjectDirectory = "/a", ExtraExcludePatterns = { "*.log" } };

        //Act
        var copy = request.Clone();
        copy.Targets.Add(CrossTarget.WindowsX64Gnullvm);
        copy.ExtraExcludePatterns.Add("docs");

        //Assert
        copy.ProjectDirectory.Should().Be("/a");
        request.Targets.Should().Equal(CrossTarget.LinuxNative);
        request.ExtraExcludePatterns.Should().Equal("*.log");
    }

    [Fact]
    public void defaults_link_android_at_api_24_with_no_copy_root_and_no_target_environment()
    {
        //Act
        var request = new ContainerBuildRequest();

        //Assert
        request.AndroidApiLevel.Should().Be(24);
        request.CopyRootDirectory.Should().BeNull();
        request.TargetEnvironment.Should().BeEmpty();
        new BuildContainerOptions().AndroidApiLevel.Should().Be(24);
        new BuildContainerOptions { AndroidApiLevel = 33 }.Clone().AndroidApiLevel.Should().Be(33);
    }

    [Fact]
    public void Validate_rejects_an_android_api_level_below_21()
    {
        //Arrange
        var request = new ContainerBuildRequest { ProjectDirectory = "/a", AndroidApiLevel = 19 };
        var act = () => request.Validate();

        //Act + Assert
        act.Should().Throw<ArgumentException>().WithMessage("*AndroidApiLevel*");
    }

    [Fact]
    public void Validate_allows_a_copy_root_only_with_a_project_directory()
    {
        //Arrange
        var request = new ContainerBuildRequest { ArchivePath = "/a.zip", CopyRootDirectory = "/stage" };
        var act = () => request.Validate();

        //Act + Assert
        act.Should().Throw<ArgumentException>().WithMessage("*CopyRootDirectory*");
    }

    [Fact]
    public void Validate_requires_target_environment_keys_to_be_requested_targets()
    {
        //Arrange
        var request = new ContainerBuildRequest { ProjectDirectory = "/a", Targets = { CrossTarget.WindowsX64Gnullvm } };
        request.TargetEnvironment[CrossTarget.WindowsX64Gnullvm] = new System.Collections.Generic.Dictionary<string, string> { ["X"] = "1" };
        request.Validate();
        request.TargetEnvironment[CrossTarget.AndroidArm64] = new System.Collections.Generic.Dictionary<string, string> { ["X"] = "1" };
        var act = () => request.Validate();

        //Act + Assert
        act.Should().Throw<ArgumentException>().WithMessage("*AndroidArm64*");
    }

    [Fact]
    public void Clone_copies_the_new_options_and_their_dictionaries()
    {
        //Arrange
        var request = new ContainerBuildRequest { ProjectDirectory = "/a", CopyRootDirectory = "/stage", AndroidApiLevel = 33 };
        request.TargetEnvironment[CrossTarget.LinuxNative] = new System.Collections.Generic.Dictionary<string, string> { ["X"] = "1" };

        //Act
        var copy = request.Clone();
        copy.TargetEnvironment[CrossTarget.LinuxNative]["X"] = "2";

        //Assert
        copy.CopyRootDirectory.Should().Be("/stage");
        copy.AndroidApiLevel.Should().Be(33);
        request.TargetEnvironment[CrossTarget.LinuxNative]["X"].Should().Be("1");
    }
}
