using System;
using System.IO;
using System.Linq;
using System.Text;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Extraction;

public class ApiExtractorToolSourceTests
{
    private static string RepoToolDirectory
        => Path.Combine(RepoPaths.RepoRoot, "src", "CodeBrix.RustTools.CrateBuilder", "Facade", "Extraction", "Tool");

    [Fact]
    public void Files_hold_the_tool_source_lock_file_and_schema()
        => ApiExtractorToolSource.Files.Select(f => f.RelativePath).Should().Contain(new[]
        {
            "Cargo.toml", "Cargo.lock", "SCHEMA.md", "src/main.rs", "src/manifest.rs", "src/model.rs", "src/resolve.rs", "src/types.rs", "src/walk.rs",
        });

    [Fact]
    public void Files_are_the_repository_tool_folder_byte_for_byte()
    {
        //Arrange
        var onDisk = Directory.GetFiles(RepoToolDirectory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetRelativePath(RepoToolDirectory, p).Replace('\\', '/'))
            .Where(p => !p.StartsWith("target/", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();

        //Act
        var embedded = ApiExtractorToolSource.Files;

        //Assert
        embedded.Select(f => f.RelativePath).Should().Equal(onDisk);
        foreach (var file in embedded)
        {
            file.Content.Should().Equal(File.ReadAllBytes(Path.Combine(RepoToolDirectory, file.RelativePath)));
        }
    }

    [Fact]
    public void ContentHash_is_stable_sha256_hex()
    {
        //Act
        var first = ApiExtractorToolSource.ContentHash;
        var second = ApiExtractorToolSource.ComputeHash(ApiExtractorToolSource.Files.Reverse());

        //Assert
        first.Should().MatchRegex("^[0-9a-f]{64}$");
        second.Should().Be(first);
    }

    [Fact]
    public void ComputeHash_changes_when_a_byte_or_a_path_changes()
    {
        //Arrange
        var a = new[] { new ToolSourceFile("src/main.rs", Encoding.UTF8.GetBytes("fn main() {}")) };
        var b = new[] { new ToolSourceFile("src/main.rs", Encoding.UTF8.GetBytes("fn main() { }")) };
        var c = new[] { new ToolSourceFile("src/lib.rs", Encoding.UTF8.GetBytes("fn main() {}")) };

        //Act
        var hashes = new[] { ApiExtractorToolSource.ComputeHash(a), ApiExtractorToolSource.ComputeHash(b), ApiExtractorToolSource.ComputeHash(c) };

        //Assert
        hashes.Distinct().Should().HaveCount(3);
    }

    [Fact]
    public void Materialise_writes_a_content_addressed_folder_once()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var first = ApiExtractorToolSource.Materialise(sandbox.Directory);
        var mainRs = Path.Combine(first, "src", "main.rs");
        var writeTime = File.GetLastWriteTimeUtc(mainRs);
        var second = ApiExtractorToolSource.Materialise(sandbox.Directory);

        //Assert
        second.Should().Be(first);
        first.Should().Be(Path.Combine(sandbox.Directory, "api-extractor", ApiExtractorToolSource.ContentHash.Substring(0, 16)));
        ApiExtractorToolSource.IsComplete(first).Should().BeTrue();
        File.GetLastWriteTimeUtc(mainRs).Should().Be(writeTime);
        File.Exists(Path.Combine(first, "Cargo.lock")).Should().BeTrue();
        Directory.GetDirectories(Path.Combine(sandbox.Directory, "api-extractor")).Should().ContainSingle();
    }

    [Fact]
    public void Materialised_files_hash_to_the_content_hash()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = ApiExtractorToolSource.Materialise(sandbox.Directory);

        //Act
        var files = ApiExtractorToolSource.Files.Select(f => new ToolSourceFile(f.RelativePath, File.ReadAllBytes(Path.Combine(folder, f.RelativePath))));

        //Assert
        ApiExtractorToolSource.ComputeHash(files).Should().Be(ApiExtractorToolSource.ContentHash);
    }

    [Fact]
    public void Materialise_replaces_an_incomplete_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var folder = ApiExtractorToolSource.GetToolDirectory(sandbox.Directory);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "partial.txt"), "left over");

        //Act
        var result = ApiExtractorToolSource.Materialise(sandbox.Directory);

        //Assert
        result.Should().Be(folder);
        File.Exists(Path.Combine(folder, "partial.txt")).Should().BeFalse();
        ApiExtractorToolSource.IsComplete(folder).Should().BeTrue();
    }
}
