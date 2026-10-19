using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

public class SourceHasherTests
{
    [Fact]
    public void Compute_hashes_relative_paths_and_contents_in_ordinal_order()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile(Path.Combine("b", "x.rs"), "fn x() {}\n");
        sandbox.WriteFile("B.toml", "upper");
        sandbox.WriteFile("a.txt", "alpha");
        var expectedInput = Encoding.UTF8.GetBytes("B.toml\0upper\0a.txt\0alpha\0b/x.rs\0fn x() {}\n\0");
        var expected = Convert.ToHexStringLower(SHA256.HashData(expectedInput));

        //Act
        var hash = SourceHasher.Compute(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        hash.Should().Be(expected);
    }

    [Fact]
    public void Compute_of_an_empty_folder_is_the_hash_of_nothing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var hash = SourceHasher.Compute(sandbox.Directory, TestContext.Current.CancellationToken);

        //Assert
        hash.Should().Be("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855");
    }
}
