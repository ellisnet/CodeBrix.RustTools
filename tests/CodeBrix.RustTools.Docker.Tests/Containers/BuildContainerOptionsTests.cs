using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class BuildContainerOptionsTests
{
    [Fact]
    public void defaults_create_a_fresh_container_that_follows_the_workbench_default()
    {
        //Act
        var options = new BuildContainerOptions();

        //Assert
        options.Name.Should().BeNull();
        options.ReuseExisting.Should().BeFalse();
        options.KeepOnDispose.Should().BeNull();
        options.Env.Should().BeEmpty();
        options.Mounts.Should().BeEmpty();
        options.ResourceLimits.Should().BeNull();
        options.TargetVolumeName.Should().BeNull();
        options.NetworkDisabled.Should().BeFalse();
    }

    [Fact]
    public void Clone_copies_the_collections()
    {
        //Arrange
        var options = new BuildContainerOptions { Name = "a", Env = { ["X"] = "1" }, Mounts = { MountSpec.Tmpfs("/tmp") }, KeepOnDispose = true };

        //Act
        var copy = options.Clone();
        copy.Env["Y"] = "2";
        copy.Mounts.Clear();

        //Assert
        copy.Name.Should().Be("a");
        copy.KeepOnDispose.Should().Be(true);
        options.Env.Should().HaveCount(1);
        options.Mounts.Should().HaveCount(1);
    }

    [Fact]
    public void Clone_copies_NetworkDisabled()
    {
        //Arrange
        var options = new BuildContainerOptions { NetworkDisabled = true };

        //Act
        var copy = options.Clone();

        //Assert
        copy.NetworkDisabled.Should().BeTrue();
    }

    [Fact]
    public void Platform_defaults_to_null_and_Clone_copies_it()
    {
        //Arrange
        var options = new BuildContainerOptions { Platform = "linux/riscv64" };

        //Act
        var copy = options.Clone();

        //Assert
        new BuildContainerOptions().Platform.Should().BeNull();
        copy.Platform.Should().Be("linux/riscv64");
    }
}
