using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CrateCountEstimatorTests
{
    private static CargoMetadata BuildGraph()
        => CargoMetadata.Parse(RepoPaths.ReadFixture(Path.Combine("Projects", "metadata-workspace-build-graph.json")));

    private static CargoBuildOptions WithPackages(params string[] packages)
    {
        var options = new CargoBuildOptions();
        foreach (var package in packages)
        {
            options.Packages.Add(package);
        }

        return options;
    }

    [Fact]
    public void Estimate_counts_the_root_package_and_its_normal_and_build_dependencies()
    {
        //Arrange
        var metadata = BuildGraph();

        //Act
        var estimate = CrateCountEstimator.Estimate(metadata, CargoCommand.Build, new CargoBuildOptions());

        //Assert
        estimate.Should().Be(7);
        estimate.Should().BeLessThan(metadata.ResolveNodes.Count);
    }

    [Fact]
    public void Estimate_adds_the_roots_dev_dependencies_for_cargo_test()
        => CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Test, new CargoBuildOptions()).Should().Be(9);

    [Theory]
    [InlineData("--all-targets")]
    [InlineData("--tests")]
    [InlineData("--examples")]
    public void Estimate_adds_the_roots_dev_dependencies_for_extra_target_flags(string flag)
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.ExtraArguments.Add(flag);

        //Act
        var estimate = CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Check, options);

        //Assert
        estimate.Should().Be(9);
    }

    [Fact]
    public void Estimate_adds_the_roots_dev_dependencies_for_AllTargets_and_Examples()
    {
        //Arrange
        var allTargets = new CargoBuildOptions { AllTargets = true };
        var examples = new CargoBuildOptions();
        examples.Examples.Add("demo");

        //Act
        var withAllTargets = CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, allTargets);
        var withExamples = CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, examples);

        //Assert
        withAllTargets.Should().Be(9);
        withExamples.Should().Be(9);
    }

    [Theory]
    [InlineData("tool", 2)]
    [InlineData("core", 2)]
    [InlineData("serde@1.0.228", 2)]
    [InlineData("serde:1.0", 2)]
    [InlineData("registry+https://github.com/rust-lang/crates.io-index#serde@1.0.228", 2)]
    [InlineData("path+file:///w/tool#0.1.0", 2)]
    public void Estimate_starts_from_the_requested_packages(string spec, int expected)
        => CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, WithPackages(spec)).Should().Be(expected);

    [Fact]
    public void Estimate_joins_several_requested_packages()
        => CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, WithPackages("tool", "core")).Should().Be(4);

    [Fact]
    public void Estimate_is_null_when_a_requested_package_is_unknown()
        => CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, WithPackages("tool", "nope")).Should().BeNull();

    [Fact]
    public void Estimate_starts_from_every_member_for_workspace_builds()
    {
        //Arrange
        var options = new CargoBuildOptions();
        options.ExtraArguments.Add("--workspace");

        //Act
        var estimate = CrateCountEstimator.Estimate(BuildGraph(), CargoCommand.Build, options);

        //Assert
        estimate.Should().Be(9, "app, core and tool plus serde, serde_derive, cc, log, itoa and clap");
    }

    [Fact]
    public void Estimate_is_null_without_metadata_or_a_resolve_graph()
    {
        //Act
        var withoutMetadata = CrateCountEstimator.Estimate(null, CargoCommand.Build, new CargoBuildOptions());
        var withoutResolve = CrateCountEstimator.Estimate(new CargoMetadata(), CargoCommand.Build, new CargoBuildOptions());

        //Assert
        withoutMetadata.Should().BeNull();
        withoutResolve.Should().BeNull();
    }

    [Fact]
    public void SelectRoots_uses_the_manifest_package_when_it_is_a_member_below_the_root()
    {
        //Arrange
        var graph = BuildGraph();
        var core = graph.FindPackages("core").Single().Id;
        var metadata = new CargoMetadata
        {
            Packages = graph.Packages,
            ResolveNodes = graph.ResolveNodes,
            WorkspaceMembers = graph.WorkspaceMembers,
            WorkspaceDefaultMembers = graph.WorkspaceDefaultMembers,
            ResolveRoot = core,
            WorkspaceRoot = "/w/",
        };

        //Act
        var roots = CrateCountEstimator.SelectRoots(metadata, new CargoBuildOptions());

        //Assert
        roots.Should().Equal(core);
    }

    [Fact]
    public void SelectRoots_uses_the_default_members_of_a_virtual_workspace()
    {
        //Arrange
        var graph = BuildGraph();
        var defaults = new[] { graph.FindPackages("app").Single().Id, graph.FindPackages("tool").Single().Id };
        var metadata = new CargoMetadata
        {
            Packages = graph.Packages,
            ResolveNodes = graph.ResolveNodes,
            WorkspaceMembers = graph.WorkspaceMembers,
            WorkspaceDefaultMembers = defaults,
            WorkspaceRoot = "/w",
        };

        //Act
        var roots = CrateCountEstimator.SelectRoots(metadata, new CargoBuildOptions());
        var estimate = CrateCountEstimator.Estimate(metadata, CargoCommand.Build, new CargoBuildOptions());

        //Assert
        roots.Should().Equal(defaults);
        estimate.Should().Be(9);
    }

    [Fact]
    public void SelectRoots_falls_back_to_every_member_without_default_members_or_a_root()
    {
        //Arrange
        var graph = BuildGraph();
        var metadata = new CargoMetadata { Packages = graph.Packages, ResolveNodes = graph.ResolveNodes, WorkspaceMembers = graph.WorkspaceMembers, WorkspaceRoot = "/w" };

        //Act
        var roots = CrateCountEstimator.SelectRoots(metadata, new CargoBuildOptions());

        //Assert
        roots.Should().Equal(graph.WorkspaceMembers);
    }

    [Theory]
    [InlineData("x86_64-unknown-linux-gnu", "x86_64-unknown-linux-gnu")]
    [InlineData(" aarch64-apple-darwin ", "aarch64-apple-darwin")]
    [InlineData("targets/custom.json", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void FilterPlatformFor_passes_triples_and_skips_target_spec_files(string triple, string expected)
        => CrateCountEstimator.FilterPlatformFor(triple).Should().Be(expected);

    [Fact]
    public void ToMetadataOptions_carries_the_filter_platform_and_the_dependency_selection()
    {
        //Arrange
        var options = new CargoBuildOptions { Offline = true, Locked = true, NoDefaultFeatures = true };
        options.Features.Add(" json ");

        //Act
        var metadataOptions = RustWorkbench.ToMetadataOptions(options, "x86_64-pc-windows-gnullvm");

        //Assert
        metadataOptions.FilterPlatform.Should().Be("x86_64-pc-windows-gnullvm");
        metadataOptions.Offline.Should().BeTrue();
        metadataOptions.Locked.Should().BeTrue();
        metadataOptions.NoDefaultFeatures.Should().BeTrue();
        metadataOptions.Features.Should().Equal("json");
        RustWorkbench.ToMetadataOptions(options).FilterPlatform.Should().BeNull();
    }
}
