using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Artifacts;

public class ArtifactRetrievalResultTests
{
    [Fact]
    public void sums_sizes_and_combines_parts()
    {
        //Arrange
        var first = new ArtifactRetrievalResult { HostDirectory = "/out", Artifacts = new[] { new RetrievedArtifact { Size = 10, Target = CrossTarget.AndroidArm64 } } };
        var second = new ArtifactRetrievalResult { HostDirectory = "/out", Artifacts = new[] { new RetrievedArtifact { Size = 5, Target = CrossTarget.AndroidX64 } } };

        //Act
        var combined = ArtifactRetrievalResult.Combine("/out", new[] { first, null, second });

        //Assert
        combined.Artifacts.Should().HaveCount(2);
        combined.TotalBytes.Should().Be(15);
        combined.HostDirectory.Should().Be("/out");
        combined.ToString().Should().Be("2 file(s), 15 byte(s) in /out");
    }
}
