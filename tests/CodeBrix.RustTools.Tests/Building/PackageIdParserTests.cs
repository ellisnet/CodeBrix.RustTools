using CodeBrix.RustTools.Building;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class PackageIdParserTests
{
    [Theory]
    [InlineData("path+file:///work/test_project_06#0.3.1", "test_project_06", "0.3.1")]
    [InlineData("path+file:///work/broken05#test_project_05@0.1.0", "test_project_05", "0.1.0")]
    [InlineData("registry+https://github.com/rust-lang/crates.io-index#anyhow@1.0.100", "anyhow", "1.0.100")]
    [InlineData("git+https://github.com/owner/my-crate.git?branch=main#0.2.0", "my-crate", "0.2.0")]
    [InlineData("git+https://github.com/owner/repo.git#inner@0.2.0", "inner", "0.2.0")]
    [InlineData("serde 1.0.0 (registry+https://github.com/rust-lang/crates.io-index)", "serde", "1.0.0")]
    public void Parse_splits_name_and_version(string packageId, string name, string version)
    {
        //Act
        var (parsedName, parsedVersion) = PackageIdParser.Parse(packageId);

        //Assert
        parsedName.Should().Be(name);
        parsedVersion.Should().Be(version);
    }

    [Fact]
    public void Parse_returns_nulls_for_blank_input()
        => PackageIdParser.Parse(" ").Should().Be(((string)null, (string)null));
}
