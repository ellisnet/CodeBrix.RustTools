using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Artifacts;

public class RetrievedArtifactTests
{
    [Fact]
    public void ToString_names_kind_target_and_path()
        => new RetrievedArtifact { Kind = ArtifactKind.SharedLibrary, Target = CrossTarget.WindowsX64Gnullvm, HostPath = "/out/x.dll", Size = 3 }.ToString()
            .Should().Be("SharedLibrary for WindowsX64Gnullvm: /out/x.dll (3 bytes)");
}
