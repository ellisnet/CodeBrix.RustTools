using System;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

public class CargoProjectLocatorTests
{
    [Fact]
    public void FindProjectRoots_finds_the_thirteen_samples()
    {
        //Act
        var roots = CargoProjectLocator.FindProjectRoots(RepoPaths.RustSamplesDirectory);

        //Assert
        roots.Should().HaveCount(13);
        roots.Select(r => Path.GetFileName(Path.GetDirectoryName(r))).Should().ContainInOrder(Enumerable.Range(1, 13).Select(RepoPaths.SampleName).ToArray());
    }

    [Fact]
    public void FindProjectRoots_with_nested_adds_vendor_crates_but_not_workspace_members()
    {
        //Act
        var roots = CargoProjectLocator.FindProjectRoots(RepoPaths.RustSamplesDirectory, includeNested: true);

        //Assert
        roots.Should().HaveCount(16);
        roots.Should().Contain(Path.Combine(RepoPaths.Sample(5), "vendor", "licensed_by_file", "Cargo.toml"));
        roots.Should().NotContain(r => r.Contains("unitconv_core", StringComparison.Ordinal));
    }

    [Fact]
    public void FindManifests_lists_every_manifest_including_members_and_vendor()
    {
        //Act
        var manifests = CargoProjectLocator.FindManifests(RepoPaths.RustSamplesDirectory);

        //Assert
        manifests.Should().HaveCount(19);
        manifests.Should().Contain(Path.Combine(RepoPaths.Sample(4), "crates", "unitconv_cli", "Cargo.toml"));
    }

    [Fact]
    public void FindManifests_respects_max_depth()
    {
        //Act
        var manifests = CargoProjectLocator.FindManifests(RepoPaths.RustSamplesDirectory, maxDepth: 1);

        //Assert
        manifests.Should().HaveCount(13);
    }

    [Fact]
    public void FindManifests_skips_target_and_hidden_folders()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[package]\nname = \"a\"\nversion = \"0.1.0\"\n");
        sandbox.WriteFile("target/package/a-0.1.0/Cargo.toml", "[package]\nname = \"a\"\nversion = \"0.1.0\"\n");
        sandbox.WriteFile(".git/x/Cargo.toml", "[package]\nname = \"x\"\nversion = \"0.1.0\"\n");
        sandbox.WriteFile("node_modules/y/Cargo.toml", "[package]\nname = \"y\"\nversion = \"0.1.0\"\n");

        //Act
        var manifests = CargoProjectLocator.FindManifests(sandbox.Directory);

        //Assert
        manifests.Should().ContainSingle().Which.Should().Be(Path.Combine(sandbox.Directory, "Cargo.toml"));
    }

    [Fact]
    public void FindRoot_prefers_the_workspace_root_for_a_member_file()
    {
        //Arrange
        var memberSource = Path.Combine(RepoPaths.Sample(4), "crates", "unitconv_cli", "src");

        //Act
        var root = CargoProjectLocator.FindRoot(memberSource);

        //Assert
        root.Should().Be(Path.Combine(RepoPaths.Sample(4), "Cargo.toml"));
    }

    [Fact]
    public void FindRoot_returns_the_package_for_a_vendored_crate_outside_any_workspace()
        => CargoProjectLocator.FindRoot(Path.Combine(RepoPaths.Sample(5), "vendor", "no_license_at_all", "lib.rs"))
            .Should().Be(Path.Combine(RepoPaths.Sample(5), "vendor", "no_license_at_all", "Cargo.toml"));

    [Fact]
    public void FindRoot_returns_null_without_a_manifest()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var root = CargoProjectLocator.FindRoot(sandbox.CreateDirectory("empty"));

        //Assert
        root.Should().BeNull();
    }

    [Fact]
    public void FindManifests_throws_for_a_missing_folder()
    {
        //Arrange
        Action act = () => CargoProjectLocator.FindManifests(Path.Combine(RepoPaths.RustSamplesDirectory, "no_such_folder"));

        //Assert
        act.Should().Throw<DirectoryNotFoundException>();
    }
}
