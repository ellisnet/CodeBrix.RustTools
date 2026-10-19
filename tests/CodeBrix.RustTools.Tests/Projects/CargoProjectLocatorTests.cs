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
    // The sample expectations are derived from the rust-samples folder itself, so a new sample
    // never breaks them.
    private static string[] SampleRootManifests()
        => Directory.GetDirectories(RepoPaths.RustSamplesDirectory)
            .Where(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, "Cargo.toml")))
            .Select(d => Path.Combine(d, "Cargo.toml"))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    private static string[] SampleManifests(int maxDepth)
    {
        var result = new System.Collections.Generic.List<string>();
        void Walk(string directory, int depth)
        {
            var manifest = Path.Combine(directory, "Cargo.toml");
            if (File.Exists(manifest))
            {
                result.Add(manifest);
            }

            if (depth >= maxDepth)
            {
                return;
            }

            foreach (var child in Directory.GetDirectories(directory))
            {
                var name = Path.GetFileName(child);
                if (!name.StartsWith('.') && name != "target" && name != "node_modules")
                {
                    Walk(child, depth + 1);
                }
            }
        }

        Walk(RepoPaths.RustSamplesDirectory, 0);
        return result.OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }

    [Fact]
    public void FindProjectRoots_finds_every_sample()
    {
        //Arrange
        var expected = SampleRootManifests();

        //Act
        var roots = CargoProjectLocator.FindProjectRoots(RepoPaths.RustSamplesDirectory);

        //Assert
        expected.Length.Should().BeGreaterThan(10);
        roots.Should().Equal(expected);
    }

    [Fact]
    public void FindProjectRoots_with_nested_adds_vendor_crates_but_not_workspace_members()
    {
        //Arrange
        var manifests = SampleManifests(CargoProjectLocator.DefaultMaxDepth);
        var members = SampleRootManifests()
            .Select(m => (Directory: Path.GetDirectoryName(m), Manifest: CargoManifest.Load(m)))
            .Where(p => p.Manifest.Workspace != null)
            .SelectMany(p => CargoProjectLocator.FindWorkspaceMembers(p.Directory, p.Manifest).Where(d => d != p.Directory))
            .Select(d => Path.Combine(d, "Cargo.toml"))
            .ToHashSet(StringComparer.Ordinal);

        //Act
        var roots = CargoProjectLocator.FindProjectRoots(RepoPaths.RustSamplesDirectory, includeNested: true);

        //Assert
        roots.Should().Equal(manifests.Where(m => !members.Contains(m)));
        roots.Should().Contain(SampleRootManifests());
        roots.Should().Contain(Path.Combine(RepoPaths.Sample(5), "vendor", "licensed_by_file", "Cargo.toml"));
        roots.Should().Contain(Path.Combine(RepoPaths.Sample(18), "external", "gamma", "Cargo.toml"));
        roots.Should().NotContain(r => r.Contains("unitconv_core", StringComparison.Ordinal));
        roots.Should().NotContain(r => r.Contains(Path.Combine("crates", "alpha"), StringComparison.Ordinal));
    }

    [Fact]
    public void FindManifests_lists_every_manifest_including_members_and_vendor()
    {
        //Act
        var manifests = CargoProjectLocator.FindManifests(RepoPaths.RustSamplesDirectory);

        //Assert
        manifests.Should().Equal(SampleManifests(CargoProjectLocator.DefaultMaxDepth));
        manifests.Should().Contain(Path.Combine(RepoPaths.Sample(4), "crates", "unitconv_cli", "Cargo.toml"));
        manifests.Should().Contain(Path.Combine(RepoPaths.Sample(18), "crates", "alpha", "Cargo.toml"));
    }

    [Fact]
    public void FindManifests_respects_max_depth()
    {
        //Act
        var manifests = CargoProjectLocator.FindManifests(RepoPaths.RustSamplesDirectory, maxDepth: 1);

        //Assert
        manifests.Should().Equal(SampleManifests(1));
        manifests.Should().Equal(SampleRootManifests());
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

    // ---- cargo's workspace rule (FindWorkspaceRootManifest / FindWorkspaceMembers) ----

    private static string Package(SampleSandbox sandbox, string folder, string extra = "")
        => sandbox.WriteFile(Path.Combine(folder, "Cargo.toml"), "[package]\nname = \"" + Path.GetFileName(folder) + "\"\nversion = \"0.1.0\"\nedition = \"2024\"\n" + extra);

    [Fact]
    public void FindWorkspaceRootManifest_takes_the_nearest_ancestor_workspace_not_the_outermost()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"mid/inner\"]\n");
        var mid = sandbox.WriteFile(Path.Combine("mid", "Cargo.toml"), "[workspace]\nmembers = [\"inner\"]\n");
        var inner = Package(sandbox, Path.Combine("mid", "inner"));

        //Act
        var root = CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(inner));
        var project = CargoProject.Open(inner);

        //Assert
        root.Should().Be(mid);
        project.WorkspaceRootManifestPath.Should().Be(mid);
    }

    [Fact]
    public void FindWorkspaceRootManifest_honours_package_workspace()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var ws = sandbox.WriteFile(Path.Combine("ws", "Cargo.toml"), "[workspace]\nmembers = [\"../member\"]\n\n[workspace.package]\nlicense = \"MIT\"\n");
        var member = sandbox.WriteFile(Path.Combine("member", "Cargo.toml"), "[package]\nname = \"member\"\nversion = \"0.1.0\"\nworkspace = \"../ws\"\nlicense.workspace = true\n");
        sandbox.WriteFile(Path.Combine("broken", "Cargo.toml"), "[package]\nname = \"broken\"\nversion = \"0.1.0\"\nworkspace = \"../nowhere\"\n");
        Action strict = () => CargoProjectLocator.FindWorkspaceRootManifest(Path.Combine(sandbox.Directory, "broken"), null, true);

        //Act
        var root = CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(member));
        var project = CargoProject.Open(member);

        //Assert
        CargoManifest.Load(member).Package.Workspace.Should().Be("../ws");
        root.Should().Be(ws);
        project.EffectivePackage.License.Should().Be("MIT");
        CargoProjectLocator.FindWorkspaceRootManifest(Path.Combine(sandbox.Directory, "broken")).Should().BeNull();
        strict.Should().Throw<InvalidDataException>().WithMessage("*package.workspace*");
    }

    [Fact]
    public void An_explicit_member_is_never_excluded()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var rootManifest = sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"crates/a\"]\nexclude = [\"crates\"]\n");
        var a = Package(sandbox, Path.Combine("crates", "a"));
        var b = Package(sandbox, Path.Combine("crates", "b"));
        var workspace = CargoManifest.Load(rootManifest).Workspace;

        //Act
        var members = CargoProjectLocator.ExpandWorkspaceMembers(sandbox.Directory, workspace);

        //Assert
        members.Should().Equal(Path.GetDirectoryName(a));
        CargoProjectLocator.IsExcludedFromWorkspace(sandbox.Directory, workspace, Path.GetDirectoryName(a)).Should().BeFalse();
        CargoProjectLocator.IsExcludedFromWorkspace(sandbox.Directory, workspace, Path.GetDirectoryName(b)).Should().BeTrue();
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(a)).Should().Be(rootManifest);
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(b)).Should().BeNull();
    }

    [Fact]
    public void Path_dependencies_inside_the_folder_are_implicit_members()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var rootManifest = sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"app\"]\n\n[workspace.dependencies]\nshared = { path = \"libs/shared\" }\n\n[workspace.package]\nedition = \"2024\"\n");
        Package(sandbox, "app", "\n[dependencies]\nhelper = { path = \"../libs/helper\" }\nshared = { workspace = true }\noutside = { path = \"../../outside\" }\n");
        var helper = sandbox.WriteFile(Path.Combine("libs", "helper", "Cargo.toml"), "[package]\nname = \"helper\"\nversion = \"0.1.0\"\nedition.workspace = true\n\n[dependencies]\ndeep = { path = \"../deep\" }\n");
        var shared = Package(sandbox, Path.Combine("libs", "shared"));
        var deep = Package(sandbox, Path.Combine("libs", "deep"));

        //Act
        var members = CargoProjectLocator.FindWorkspaceMembers(sandbox.Directory, CargoManifest.Load(rootManifest));
        var helperProject = CargoProject.Open(helper);

        //Assert
        members.Should().Equal(
            Path.Combine(sandbox.Directory, "app"),
            Path.GetDirectoryName(helper),
            Path.GetDirectoryName(shared),
            Path.GetDirectoryName(deep));
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(deep)).Should().Be(rootManifest);
        helperProject.WorkspaceRootManifestPath.Should().Be(rootManifest);
        helperProject.EffectivePackage.Edition.Should().Be("2024");
        CargoProject.Open(rootManifest).WorkspaceMemberDirectories.Should().HaveCount(4);
    }

    [Fact]
    public void An_excluded_non_member_is_standalone_and_a_stray_crate_is_refused_in_strict_mode()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var rootManifest = sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"crates/*\"]\nexclude = [\"crates/skip\", \"tools\"]\n");
        var kept = Package(sandbox, Path.Combine("crates", "kept"));
        var skip = Package(sandbox, Path.Combine("crates", "skip"));
        var tool = Package(sandbox, Path.Combine("tools", "gen"));
        var stray = Package(sandbox, "stray");
        Action strict = () => CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(stray), null, true);

        //Act
        var members = CargoProjectLocator.FindWorkspaceMembers(sandbox.Directory, CargoManifest.Load(rootManifest));

        //Assert
        members.Should().Equal(Path.GetDirectoryName(kept));
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(kept)).Should().Be(rootManifest);
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(skip)).Should().BeNull();
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(tool), null, true).Should().BeNull();
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(stray)).Should().BeNull();
        strict.Should().Throw<InvalidDataException>().WithMessage("*is not one of its members*");
    }

    [Fact]
    public void FindWorkspaceRootManifest_stops_at_the_boundary()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[workspace]\nmembers = [\"clone/inner\"]\n");
        var inner = Package(sandbox, Path.Combine("clone", "inner"));

        //Act + Assert
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(inner), Path.Combine(sandbox.Directory, "clone"), true).Should().BeNull();
        CargoProjectLocator.FindWorkspaceRootManifest(Path.GetDirectoryName(inner)).Should().Be(Path.Combine(sandbox.Directory, "Cargo.toml"));
    }
}
