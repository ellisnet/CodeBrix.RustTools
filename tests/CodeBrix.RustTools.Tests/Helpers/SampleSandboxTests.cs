using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Helpers;

public class SampleSandboxTests
{
    [Fact]
    public void FromSample_copies_a_sample_outside_rust_samples_and_cleans_up()
    {
        //Arrange
        string directory;

        //Act
        using (var sandbox = SampleSandbox.FromSample(5))
        {
            directory = sandbox.Directory;

            //Assert
            File.Exists(Path.Combine(directory, "Cargo.toml")).Should().BeTrue();
            directory.Should().NotStartWith(RepoPaths.RustSamplesDirectory);
            Directory.Exists(Path.Combine(directory, "target")).Should().BeFalse();
        }

        Directory.Exists(directory).Should().BeFalse();
    }
}
