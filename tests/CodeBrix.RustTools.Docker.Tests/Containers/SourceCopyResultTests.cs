using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class SourceCopyResultTests
{
    [Fact]
    public void ToString_shows_the_counts_and_folders()
        => new SourceCopyResult { FileCount = 4, TotalBytes = 99, HostDirectory = "/src/app", ContainerDirectory = "/work" }.ToString()
            .Should().Be("4 file(s), 99 byte(s) from /src/app to /work");
}
