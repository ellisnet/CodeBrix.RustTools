using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.TomlParse;
using CodeBrix.TomlParse.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

public class ManifestDependencyScannerTests
{
    [Fact]
    public void Scan_finds_path_and_git_entries_in_every_dependency_table()
    {
        //Arrange
        var document = TomlSerializer.Deserialize<TomlTable>("""
            [dependencies]
            a = { path = "../a" }
            registry = "1"
            [dev-dependencies]
            b = { path = "../b" }
            [build_dependencies]
            c = { path = "../c" }
            [target.'cfg(windows)'.build-dependencies]
            d = { path = "../d" }
            [workspace.dependencies]
            e = { path = "crates/e" }
            g = { git = "https://example.invalid/g", branch = "dev" }
            [patch.crates-io]
            f = { path = "patches/f" }
            [replace]
            "h:1.0.0" = { path = "patches/h" }
            """);

        //Act
        var entries = ManifestDependencyScanner.Scan(document);

        //Assert
        entries.Select(e => string.Join("/", e.TableKeys)).Should().Equal(
            "dependencies/a", "dev-dependencies/b", "build_dependencies/c", "target/cfg(windows)/build-dependencies/d",
            "workspace/dependencies/e", "workspace/dependencies/g", "patch/crates-io/f", "replace/h:1.0.0");
        var git = entries.Single(e => e.Name == "g");
        git.Path.Should().BeNull();
        ManifestDependencyScanner.DescribeGit(git).Should().Be("g @ https://example.invalid/g#dev");
    }
}
