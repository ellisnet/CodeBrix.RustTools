using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Staging;

public class WorkspaceRootFinderTests
{
    private static string Package(SampleSandbox sandbox, string folder, string extra = "")
        => sandbox.WriteFile(Path.Combine(folder, "Cargo.toml"), "[package]\nname = \"" + Path.GetFileName(folder) + "\"\nversion = \"0.1.0\"\nedition = \"2024\"\n" + extra);

    [Fact]
    public void a_glob_member_finds_the_workspace_root()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);

        //Act
        var root = WorkspaceRootFinder.FindRootManifest(Path.Combine(sandbox.Directory, "crates", "beta", "Cargo.toml"));

        //Assert
        root.Should().Be(Path.Combine(sandbox.Directory, "Cargo.toml"));
    }

    [Fact]
    public void an_excluded_crate_is_standalone()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(18);
        var gamma = Path.Combine(sandbox.Directory, "external", "gamma", "Cargo.toml");

        //Act + Assert
        WorkspaceRootFinder.FindRootManifest(gamma).Should().Be(gamma);
    }

    [Fact]
    public void a_path_dependency_of_a_member_inside_the_workspace_is_an_implicit_member()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"app\"]\nresolver = \"3\"\n");
        Package(sandbox, "app", "\n[dependencies]\nhelper = { path = \"../libs/helper\" }\n");
        var helper = Package(sandbox, Path.Combine("libs", "helper"));

        //Act + Assert
        WorkspaceRootFinder.FindRootManifest(helper).Should().Be(Path.Combine(sandbox.Directory, "Cargo.toml"));
    }

    [Fact]
    public void package_workspace_names_the_root_explicitly()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile(Path.Combine("ws", "Cargo.toml"), "[workspace]\nmembers = [\"../member\"]\n");
        var member = Package(sandbox, "member");
        File.WriteAllText(member, File.ReadAllText(member).Replace("edition = \"2024\"\n", "edition = \"2024\"\nworkspace = \"../ws\"\n"));

        //Act + Assert
        WorkspaceRootFinder.FindRootManifest(member).Should().Be(Path.Combine(sandbox.Directory, "ws", "Cargo.toml"));
    }

    [Fact]
    public void the_search_stops_at_the_boundary()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"clone/inner\"]\n");
        var inner = Package(sandbox, Path.Combine("clone", "inner"));

        //Act + Assert
        WorkspaceRootFinder.FindRootManifest(inner, Path.Combine(sandbox.Directory, "clone")).Should().Be(inner);
    }

    [Fact]
    public void a_non_member_inside_a_workspace_is_rejected_like_cargo_does()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"listed\"]\n");
        Package(sandbox, "listed");
        var stray = Package(sandbox, "stray");
        Action act = () => WorkspaceRootFinder.FindRootManifest(stray);

        //Act + Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*is not one of its members*");
    }
}
