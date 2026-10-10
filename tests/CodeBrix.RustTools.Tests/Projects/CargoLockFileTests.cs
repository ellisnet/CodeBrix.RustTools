using System;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

public class CargoLockFileTests
{
    [Fact]
    public void Load_reads_registry_and_local_packages()
    {
        //Act
        var lockFile = CargoLockFile.Load(Path.Combine(RepoPaths.Sample(5), "Cargo.lock"));

        //Assert
        lockFile.FormatVersion.Should().Be(4);
        var itoa = lockFile.Find("itoa").Should().ContainSingle().Subject;
        itoa.IsRegistry.Should().BeTrue();
        itoa.Source.Should().Be(CargoMetadata.CratesIoSource);
        itoa.Checksum.Should().NotBeNullOrEmpty();
        var local = lockFile.Find("licensed_by_file").Single();
        local.IsLocal.Should().BeTrue();
        local.Checksum.Should().BeNull();
    }

    [Fact]
    public void Load_reads_dependency_lists()
    {
        //Act
        var root = CargoLockFile.Load(Path.Combine(RepoPaths.Sample(5), "Cargo.lock")).Find("test_project_05").Single();

        //Assert
        root.GetDependencyNames().Should().Contain("licensed_by_file");
        root.GetDependencyNames().Should().Contain("serde");
    }

    [Fact]
    public void Parse_keeps_versioned_dependency_entries()
    {
        //Arrange
        const string text = """
            version = 3

            [[package]]
            name = "app"
            version = "0.1.0"
            dependencies = [
             "rand 0.7.3",
             "rand 0.8.5 (registry+https://github.com/rust-lang/crates.io-index)",
            ]

            [[package]]
            name = "rand"
            version = "0.7.3"
            source = "git+https://example.com/rand.git#abc123"
            """;

        //Act
        var lockFile = CargoLockFile.Parse(text);

        //Assert
        lockFile.FormatVersion.Should().Be(3);
        lockFile.Packages.Should().HaveCount(2);
        lockFile.Packages[0].Dependencies.Should().HaveCount(2);
        lockFile.Packages[0].GetDependencyNames().Should().OnlyContain(n => n == "rand");
        lockFile.Packages[1].IsGit.Should().BeTrue();
        lockFile.FilePath.Should().BeNull();
    }

    [Fact]
    public void Load_reads_the_workspace_lock_with_members()
    {
        //Act
        var lockFile = CargoLockFile.Load(Path.Combine(RepoPaths.Sample(4), "Cargo.lock"));

        //Assert
        lockFile.Packages.Where(p => p.IsLocal).Select(p => p.Name).Should().Contain("unitconv_report");
        lockFile.Packages.Should().OnlyContain(p => p.IsLocal || p.IsRegistry);
    }

    [Fact]
    public void Load_throws_for_a_missing_file()
    {
        //Arrange
        Action act = () => CargoLockFile.Load(Path.Combine(RepoPaths.Sample(6), "missing", "Cargo.lock"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }
}
