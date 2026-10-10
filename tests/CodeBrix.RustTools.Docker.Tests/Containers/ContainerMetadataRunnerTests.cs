using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class ContainerMetadataRunnerTests
{
    [Fact]
    public void BuildCommand_runs_cargo_metadata_for_the_container_manifest()
        => ContainerMetadataRunner.BuildCommand("/work/Cargo.toml", null)
            .Should().Equal("cargo", "metadata", "--format-version", "1", "--manifest-path", "/work/Cargo.toml");

    [Fact]
    public void BuildCommand_passes_the_metadata_options()
    {
        //Arrange
        var options = new CargoMetadataOptions { Offline = true, Locked = true };
        options.Features.Add("json");

        //Act
        var command = ContainerMetadataRunner.BuildCommand("/work/crates/a/Cargo.toml", options);

        //Assert
        command.Should().ContainInOrder("--manifest-path", "/work/crates/a/Cargo.toml", "--offline", "--locked", "--features", "json");
    }
}
