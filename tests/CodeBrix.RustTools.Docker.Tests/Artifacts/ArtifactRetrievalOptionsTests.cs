using CodeBrix.RustTools.Docker.Artifacts;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Artifacts;

public class ArtifactRetrievalOptionsTests
{
    [Fact]
    public void defaults_take_programs_and_shared_libraries_by_target()
    {
        //Act
        var options = new ArtifactRetrievalOptions();

        //Assert
        options.HostDirectory.Should().BeNull();
        options.Layout.Should().Be(ArtifactLayout.ByTarget);
        options.IncludeExecutables.Should().BeTrue();
        options.IncludeSharedLibraries.Should().BeTrue();
        options.IncludeStaticLibraries.Should().BeFalse();
        options.IncludeRlibs.Should().BeFalse();
        options.Overwrite.Should().BeTrue();
    }

    [Fact]
    public void Clone_is_independent()
    {
        //Arrange
        var options = new ArtifactRetrievalOptions { HostDirectory = "/out", Layout = ArtifactLayout.Flat };

        //Act
        var copy = options.Clone();
        copy.HostDirectory = "/elsewhere";

        //Assert
        options.HostDirectory.Should().Be("/out");
        copy.Layout.Should().Be(ArtifactLayout.Flat);
    }
}
