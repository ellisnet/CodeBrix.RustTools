using CodeBrix.RustTools.Docker.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class TransportedFileTests
{
    [Fact]
    public void is_executable_when_any_execute_bit_is_set()
    {
        //Assert
        new TransportedFile { Mode = 0b111_101_101 }.IsExecutable.Should().BeTrue();
        new TransportedFile { Mode = 0b110_100_100 }.IsExecutable.Should().BeFalse();
    }
}
