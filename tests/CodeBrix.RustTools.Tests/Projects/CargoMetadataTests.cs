using System;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

public class CargoMetadataTests
{
    private const string Json = """
        {
          "packages": [
            {
              "name": "app", "version": "0.1.0", "id": "path+file:///w/app#0.1.0",
              "license": null, "license_file": "LICENSE.txt", "description": "demo", "source": null,
              "dependencies": [
                { "name": "itoa", "source": "registry+https://github.com/rust-lang/crates.io-index", "req": "^1", "kind": null,
                  "rename": null, "optional": false, "uses_default_features": false, "features": ["std"], "target": "cfg(unix)" },
                { "name": "cc", "source": "registry+https://github.com/rust-lang/crates.io-index", "req": "^1", "kind": "build",
                  "rename": "cc_crate", "optional": true, "uses_default_features": true, "features": [], "target": null }
              ],
              "targets": [
                { "kind": ["lib"], "crate_types": ["rlib", "cdylib"], "name": "app", "src_path": "/w/app/src/lib.rs", "edition": "2024" },
                { "kind": ["custom-build"], "crate_types": ["bin"], "name": "build-script-build", "src_path": "/w/app/build.rs", "edition": "2024" },
                { "kind": ["example"], "crate_types": ["bin"], "name": "demo", "src_path": "/w/app/examples/demo.rs", "edition": "2024", "required-features": ["json"] }
              ],
              "features": { "default": ["json"], "json": [] },
              "manifest_path": "/w/app/Cargo.toml", "edition": "2024", "repository": "https://example.com/app", "rust_version": "1.85"
            },
            {
              "name": "itoa", "version": "1.0.18", "id": "registry+https://github.com/rust-lang/crates.io-index#itoa@1.0.18",
              "license": "MIT OR Apache-2.0", "license_file": null, "source": "registry+https://github.com/rust-lang/crates.io-index",
              "dependencies": [], "targets": [], "features": {}, "manifest_path": "/r/itoa-1.0.18/Cargo.toml", "edition": "2018"
            }
          ],
          "workspace_members": ["path+file:///w/app#0.1.0"],
          "workspace_default_members": ["path+file:///w/app#0.1.0"],
          "resolve": {
            "nodes": [
              { "id": "path+file:///w/app#0.1.0",
                "dependencies": ["registry+https://github.com/rust-lang/crates.io-index#itoa@1.0.18"],
                "deps": [ { "name": "itoa", "pkg": "registry+https://github.com/rust-lang/crates.io-index#itoa@1.0.18",
                            "dep_kinds": [ { "kind": null, "target": "cfg(unix)" }, { "kind": "dev", "target": null } ] } ],
                "features": ["default", "json"] },
              { "id": "registry+https://github.com/rust-lang/crates.io-index#itoa@1.0.18", "dependencies": [], "deps": [], "features": [] }
            ],
            "root": "path+file:///w/app#0.1.0"
          },
          "target_directory": "/w/target",
          "version": 1,
          "workspace_root": "/w",
          "metadata": null
        }
        """;

    [Fact]
    public void Parse_reads_packages_and_workspace_fields()
    {
        //Act
        var metadata = CargoMetadata.Parse(Json);

        //Assert
        metadata.FormatVersion.Should().Be(1);
        metadata.WorkspaceRoot.Should().Be("/w");
        metadata.TargetDirectory.Should().Be("/w/target");
        metadata.ResolveRoot.Should().Be("path+file:///w/app#0.1.0");
        metadata.IsWorkspaceMember("path+file:///w/app#0.1.0").Should().BeTrue();
        metadata.GetWorkspacePackages().Single().Name.Should().Be("app");
        var app = metadata.GetPackage("path+file:///w/app#0.1.0");
        app.License.Should().BeNull();
        app.LicenseFile.Should().Be("LICENSE.txt");
        app.IsLocal.Should().BeTrue();
        app.RustVersion.Should().Be("1.85");
        app.Repository.Should().Be("https://example.com/app");
        app.Features["default"].Should().ContainSingle().Which.Should().Be("json");
        metadata.FindPackages("itoa").Single().IsCratesIo.Should().BeTrue();
    }

    [Fact]
    public void Parse_reads_targets_and_kinds()
    {
        //Act
        var targets = CargoMetadata.Parse(Json).FindPackages("app").Single().Targets;

        //Assert
        targets[0].Kind.Should().Be(CargoTargetKind.Lib);
        targets[0].CrateTypes.Should().ContainInOrder(CargoTargetKind.Rlib, CargoTargetKind.Cdylib);
        targets[1].Kind.Should().Be(CargoTargetKind.CustomBuild);
        targets[2].HasKind(CargoTargetKind.Example).Should().BeTrue();
        targets[2].RequiredFeatures.Should().ContainSingle().Which.Should().Be("json");
    }

    [Fact]
    public void Parse_reads_declared_dependencies()
    {
        //Act
        var dependencies = CargoMetadata.Parse(Json).FindPackages("app").Single().Dependencies;

        //Assert
        dependencies[0].Kind.Should().Be(CargoDependencyKind.Normal);
        dependencies[0].UsesDefaultFeatures.Should().BeFalse();
        dependencies[0].Target.Should().Be("cfg(unix)");
        dependencies[1].Kind.Should().Be(CargoDependencyKind.Build);
        dependencies[1].Optional.Should().BeTrue();
        dependencies[1].Rename.Should().Be("cc_crate");
    }

    [Fact]
    public void Parse_reads_resolve_nodes_and_dep_kinds()
    {
        //Act
        var node = CargoMetadata.Parse(Json).GetNode("path+file:///w/app#0.1.0");

        //Assert
        node.Features.Should().ContainInOrder("default", "json");
        var edge = node.Deps.Single();
        edge.Name.Should().Be("itoa");
        edge.DepKinds.Should().HaveCount(2);
        edge.DepKinds[0].Target.Should().Be("cfg(unix)");
        edge.HasKind(CargoDependencyKind.Dev).Should().BeTrue();
        edge.IsDevOnly.Should().BeFalse();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\": 1}")]
    [InlineData("[]")]
    public void Parse_throws_for_non_metadata_text(string text)
    {
        //Arrange
        Action act = () => CargoMetadata.Parse(text);

        //Assert
        act.Should().Throw<RustToolsException>();
    }

    [Theory]
    [InlineData("proc-macro", CargoTargetKind.ProcMacro)]
    [InlineData("STATICLIB", CargoTargetKind.Staticlib)]
    [InlineData("custom-build", CargoTargetKind.CustomBuild)]
    [InlineData("whatever", CargoTargetKind.Unknown)]
    public void CargoTargetKinds_Parse_maps_cargo_strings(string text, CargoTargetKind expected)
        => CargoTargetKinds.Parse(text).Should().Be(expected);

    private static CargoMetadata BuildGraph()
        => CargoMetadata.Parse(RepoPaths.ReadFixture(Path.Combine("Projects", "metadata-workspace-build-graph.json")));

    private static string Id(CargoMetadata metadata, string name) => metadata.FindPackages(name).Single().Id;

    [Fact]
    public void GetBuildClosure_follows_normal_and_build_edges_and_skips_dev_edges()
    {
        //Arrange
        var metadata = BuildGraph();

        //Act
        var closure = metadata.GetBuildClosure(new[] { Id(metadata, "app") });

        //Assert
        closure.Select(id => metadata.GetPackage(id).Name).Should().BeEquivalentTo("app", "core", "serde", "serde_derive", "cc", "log", "itoa");
        closure[0].Should().Be(Id(metadata, "app"));
        metadata.ResolveNodes.Should().HaveCount(13);
    }

    [Fact]
    public void GetBuildClosure_follows_only_the_roots_dev_edges_when_asked()
    {
        //Arrange
        var metadata = BuildGraph();

        //Act
        var closure = metadata.GetBuildClosure(new[] { Id(metadata, "app") }, includeRootDevDependencies: true);

        //Assert
        closure.Select(id => metadata.GetPackage(id).Name).Should().Contain(new[] { "proptest", "rand" });
        closure.Select(id => metadata.GetPackage(id).Name).Should().NotContain("tempfile", "the dev-dependency of a non-root package is not built");
        closure.Should().HaveCount(9);
    }

    [Fact]
    public void GetBuildClosure_counts_an_edge_without_dep_kinds_as_normal_and_each_package_once()
    {
        //Arrange
        var metadata = BuildGraph();

        //Act
        var closure = metadata.GetBuildClosure(new[] { Id(metadata, "tool"), Id(metadata, "tool"), "not-in-the-graph" });

        //Assert
        closure.Select(id => metadata.GetPackage(id).Name).Should().Equal("tool", "clap");
    }

    [Fact]
    public void GetBuildClosure_is_empty_without_a_resolve_graph()
        => new CargoMetadata().GetBuildClosure(new[] { "x" }).Should().BeEmpty();

    [Fact]
    public void Union_joins_packages_and_nodes_by_id()
    {
        //Arrange
        var metadata = BuildGraph();
        var first = new CargoMetadata
        {
            Packages = metadata.Packages.Take(3).ToList(),
            ResolveNodes = metadata.ResolveNodes.Take(3).ToList(),
            WorkspaceMembers = metadata.WorkspaceMembers,
            WorkspaceRoot = "/w",
        };
        var second = new CargoMetadata { Packages = metadata.Packages.Skip(2).ToList(), ResolveNodes = metadata.ResolveNodes.Skip(2).ToList(), WorkspaceRoot = "/other" };

        //Act
        var union = CargoMetadata.Union(new[] { first, second });

        //Assert
        union.Packages.Should().HaveCount(metadata.Packages.Count);
        union.ResolveNodes.Should().HaveCount(metadata.ResolveNodes.Count);
        union.WorkspaceRoot.Should().Be("/w");
        union.WorkspaceMembers.Should().Equal(metadata.WorkspaceMembers);
        CargoMetadata.Union(new[] { first }).Should().BeSameAs(first);
    }
}
