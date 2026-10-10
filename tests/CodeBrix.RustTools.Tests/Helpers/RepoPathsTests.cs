using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Helpers;

public class RepoPathsTests
{
    [Fact]
    public void FromCallerFilePath_finds_the_repository_from_the_helper_source_file()
    {
        //Act
        var root = RepoPaths.FromCallerFilePath();

        //Assert
        root.Should().NotBeNull();
        File.Exists(Path.Combine(root, RepoPaths.SolutionFileName)).Should().BeTrue();
        File.Exists(Path.Combine(root, "tests", "CodeBrix.RustTools.Tests", "Helpers", "RepoPaths.cs")).Should().BeTrue();
    }

    [Fact]
    public void SourceFilePath_is_this_helpers_source_file()
        => Path.GetFileName(RepoPaths.SourceFilePath).Should().Be("RepoPaths.cs");

    [Fact]
    public void WalkUp_returns_null_for_a_missing_folder()
        => RepoPaths.WalkUp(Path.Combine(Path.GetTempPath(), "codebrix-rusttools-no-such-folder")).Should().BeNull();

    [Fact]
    public void RepoRoot_contains_the_rust_samples()
        => Directory.Exists(RepoPaths.Sample(5)).Should().BeTrue();
}
