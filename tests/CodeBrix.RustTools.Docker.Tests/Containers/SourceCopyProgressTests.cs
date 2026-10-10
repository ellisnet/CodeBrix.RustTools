using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class SourceCopyProgressTests
{
    [Fact]
    public void ToString_shows_the_counts_and_the_file()
        => new SourceCopyProgress { FilesCopied = 3, BytesCopied = 1200, CurrentPath = "src/main.rs" }.ToString()
            .Should().Be("3 file(s), 1200 byte(s): src/main.rs");
}
