using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

public class CargoProjectTests
{
    [Fact]
    public void Open_accepts_a_directory_or_a_manifest_path()
    {
        //Arrange
        var directory = RepoPaths.Sample(2);

        //Act
        var fromDirectory = CargoProject.Open(directory);
        var fromFile = CargoProject.Open(Path.Combine(directory, "Cargo.toml"));

        //Assert
        fromDirectory.ManifestPath.Should().Be(fromFile.ManifestPath);
        fromDirectory.Directory.Should().Be(directory);
        fromDirectory.Name.Should().Be("test_project_02");
        fromDirectory.LockFile.Should().NotBeNull();
        fromDirectory.WorkspaceRootManifestPath.Should().BeNull();
        fromDirectory.IsWorkspaceRoot.Should().BeFalse();
    }

    [Fact]
    public void Open_throws_when_there_is_no_manifest()
    {
        //Arrange
        Action act = () => CargoProject.Open(RepoPaths.RustSamplesDirectory);

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Open_reads_a_virtual_workspace_and_its_members()
    {
        //Act
        var project = CargoProject.Open(RepoPaths.Sample(4));

        //Assert
        project.IsWorkspaceRoot.Should().BeTrue();
        project.IsVirtual.Should().BeTrue();
        project.Name.Should().BeNull();
        project.EffectivePackage.Should().BeNull();
        project.WorkspaceRootManifestPath.Should().Be(project.ManifestPath);
        project.WorkspaceMemberDirectories.Select(Path.GetFileName).Should().ContainInOrder("unitconv_core", "unitconv_report", "unitconv_cli");
        project.GetWorkspaceMembers().Select(m => m.Name).Should().ContainInOrder("unitconv_core", "unitconv_report", "unitconv_cli");
    }

    [Fact]
    public void Open_on_a_member_finds_the_workspace_root_lock_file_and_inherited_license()
    {
        //Act
        var core = CargoProject.Open(Path.Combine(RepoPaths.Sample(4), "crates", "unitconv_core"));

        //Assert
        core.WorkspaceRootManifestPath.Should().Be(Path.Combine(RepoPaths.Sample(4), "Cargo.toml"));
        core.WorkspaceRootManifest.IsVirtual.Should().BeTrue();
        core.LockFile.FilePath.Should().Be(Path.Combine(RepoPaths.Sample(4), "Cargo.lock"));
        core.Manifest.Package.License.Should().BeNull();
        core.EffectivePackage.License.Should().Be("MIT");
        core.EffectivePackage.Version.Should().Be("0.1.0");
        core.EffectivePackage.IsInherited("license").Should().BeTrue();
    }

    [Fact]
    public void WorkspaceMemberDirectories_expands_globs_and_excludes()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[package]\nname = \"root\"\nversion = \"0.1.0\"\n\n[workspace]\nmembers = [\"crates/*\", \"tools/cli\"]\nexclude = [\"crates/old\"]\n");
        sandbox.WriteFile("crates/alpha/Cargo.toml", "[package]\nname = \"alpha\"\nversion = \"0.1.0\"\n");
        sandbox.WriteFile("crates/beta/Cargo.toml", "[package]\nname = \"beta\"\nversion = \"0.1.0\"\n");
        sandbox.WriteFile("crates/old/Cargo.toml", "[package]\nname = \"old\"\nversion = \"0.1.0\"\n");
        sandbox.CreateDirectory("crates/not_a_crate");
        sandbox.WriteFile("tools/cli/Cargo.toml", "[package]\nname = \"cli\"\nversion = \"0.1.0\"\n");

        //Act
        var project = CargoProject.Open(sandbox.Directory);

        //Assert
        project.IsVirtual.Should().BeFalse();
        project.WorkspaceMemberDirectories.Select(d => Path.GetRelativePath(sandbox.Directory, d).Replace('\\', '/'))
            .Should().ContainInOrder(".", "crates/alpha", "crates/beta", "tools/cli");
        project.WorkspaceMemberDirectories.Should().HaveCount(4);
        CargoProject.Open(Path.Combine(sandbox.Directory, "crates", "beta")).WorkspaceRootManifestPath.Should().Be(project.ManifestPath);
        CargoProject.Open(Path.Combine(sandbox.Directory, "crates", "old")).WorkspaceRootManifestPath.Should().BeNull();
    }

    [Fact]
    public void GetDeclaredTargets_merges_declared_and_conventional_targets()
    {
        //Act
        var targets = CargoProject.Open(RepoPaths.Sample(5)).GetDeclaredTargets();

        //Assert
        targets.Single(t => t.Kind == CargoTargetKind.Lib).Name.Should().Be("textkit");
        targets.Single(t => t.Kind == CargoTargetKind.Bin).Path.Should().Be("src/main.rs");
        targets.Where(t => t.Kind == CargoTargetKind.Example).Select(t => t.Name).Should().ContainInOrder("json_report", "basic_stats");
        targets.Single(t => t.Name == "json_report").IsDeclared.Should().BeTrue();
        targets.Single(t => t.Name == "json_report").Path.Should().Be("examples/json_report.rs");
        targets.Single(t => t.Name == "basic_stats").IsDeclared.Should().BeFalse();
        targets.Where(t => t.Kind == CargoTargetKind.Test).Select(t => t.Name).Should().ContainInOrder("binary_runs", "library_api");
        targets.Should().NotContain(t => t.Kind == CargoTargetKind.CustomBuild);
    }

    [Fact]
    public void GetDeclaredTargets_honours_autobins_false_and_the_build_script()
    {
        //Act
        var targets = CargoProject.Open(RepoPaths.Sample(6)).GetDeclaredTargets();

        //Assert
        targets.Where(t => t.Kind == CargoTargetKind.Bin).Select(t => t.Name).Should().ContainInOrder("buildinfo", "crc32sum");
        targets.Count(t => t.Kind == CargoTargetKind.Bin).Should().Be(2);
        targets.Should().NotContain(t => t.Kind == CargoTargetKind.Lib);
        var build = targets.Single(t => t.Kind == CargoTargetKind.CustomBuild);
        build.Path.Should().Be("build.rs");
        build.Name.Should().Be("build-script-build");
        build.IsDeclared.Should().BeTrue();
    }

    [Fact]
    public void GetDeclaredTargets_lists_benches_and_a_named_lib()
    {
        //Act
        var targets = CargoProject.Open(RepoPaths.Sample(12)).GetDeclaredTargets();

        //Assert
        var lib = targets.Single(t => t.Kind == CargoTargetKind.Lib);
        lib.Name.Should().Be("batchjob");
        lib.Path.Should().Be("src/lib.rs");
        targets.Single(t => t.Kind == CargoTargetKind.Bin).Name.Should().Be("test_project_12");
        var bench = targets.Single(t => t.Kind == CargoTargetKind.Bench);
        bench.Harness.Should().BeFalse();
        bench.Path.Should().Be("benches/record_throughput.rs");
        targets.Single(t => t.Kind == CargoTargetKind.Example).Name.Should().Be("single_record");
        targets.Single(t => t.Kind == CargoTargetKind.Test).Name.Should().Be("round_trip");
    }

    [Fact]
    public void GetDeclaredTargets_finds_an_undeclared_build_script()
    {
        //Act
        var targets = CargoProject.Open(RepoPaths.Sample(10)).GetDeclaredTargets();

        //Assert
        targets.Single(t => t.Kind == CargoTargetKind.Bin).Name.Should().Be("test_project_10");
        targets.Single(t => t.Kind == CargoTargetKind.CustomBuild).Path.Should().Be("build.rs");
    }

    [Fact]
    public void GetDeclaredTargets_is_empty_for_a_virtual_manifest()
        => CargoProject.Open(RepoPaths.Sample(4)).GetDeclaredTargets().Should().BeEmpty();

    [Fact]
    public void BuildMetadataArguments_maps_every_option()
    {
        //Arrange
        var options = new CargoMetadataOptions
        {
            Offline = true,
            Locked = true,
            NoDependencies = true,
            FilterPlatform = "x86_64-unknown-linux-gnu",
            AllFeatures = true,
        };
        options.Features.Add("json");
        options.Features.Add("case");

        //Act
        var arguments = CargoProject.BuildMetadataArguments("/p/Cargo.toml", options);

        //Assert
        arguments.Should().ContainInOrder("metadata", "--format-version", "1", "--manifest-path", "/p/Cargo.toml", "--offline", "--locked", "--no-deps", "--filter-platform", "x86_64-unknown-linux-gnu", "--all-features", "--features", "json,case");
    }

    [Fact]
    public async Task GetMetadataAsync_reads_a_sandboxed_workspace()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);

        //Act
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Assert
        metadata.WorkspaceMembers.Should().HaveCount(3);
        metadata.WorkspaceRoot.Should().Be(sandbox.Directory);
        metadata.TargetDirectory.Should().Be(Path.Combine(sandbox.Directory, "target"));
        metadata.HasResolve.Should().BeTrue();
        var report = metadata.FindPackages("unitconv_report").Single();
        report.License.Should().Be("GPL-3.0-only");
        report.IsLocal.Should().BeTrue();
        metadata.FindPackages("unitconv_core").Single().License.Should().Be("MIT");
        metadata.FindPackages("serde").Single().IsCratesIo.Should().BeTrue();
        var cli = metadata.FindPackages("unitconv_cli").Single();
        cli.Targets.Single().Name.Should().Be("unitconv");
        cli.Targets.Single().Kind.Should().Be(CargoTargetKind.Bin);
    }

    [Fact]
    public async Task GetMetadataAsync_reads_resolve_edges_with_kinds()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);

        //Act
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Assert
        var core = metadata.FindPackages("unitconv_core").Single();
        var node = metadata.GetNode(core.Id);
        node.Deps.Select(d => d.Name).Should().Contain("serde");
        node.Deps.Single(d => d.Name == "serde_json").IsDevOnly.Should().BeTrue();
        node.Deps.Single(d => d.Name == "serde").HasKind(CargoDependencyKind.Normal).Should().BeTrue();
        metadata.GetPackage(node.Deps.Single(d => d.Name == "thiserror").PackageId).Name.Should().Be("thiserror");
    }

    [Fact]
    public async Task GetMetadataAsync_reports_build_scripts_and_targets()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(6);

        //Act
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Assert
        var package = metadata.Packages.Single();
        package.Name.Should().Be("test_project_06");
        package.Targets.Select(t => t.Name).Should().Contain("buildinfo");
        package.Targets.Should().Contain(t => t.Kind == CargoTargetKind.CustomBuild);
        metadata.ResolveRoot.Should().Be(package.Id);
    }

    [Fact]
    public async Task GetMetadataAsync_with_no_deps_returns_only_members()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var project = CargoProject.Open(sandbox.Directory);

        //Act
        var metadata = await project.GetMetadataAsync(new CargoMetadataOptions { NoDependencies = true, Offline = true }, TestContext.Current.CancellationToken);

        //Assert
        metadata.HasResolve.Should().BeFalse();
        metadata.Packages.Should().ContainSingle().Which.Name.Should().Be("test_project_05");
        metadata.Packages[0].Features.Keys.Should().Contain("json");
    }

    [Fact]
    public async Task GetMetadataAsync_reads_the_warnings_sample()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(10);

        //Act
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Assert
        metadata.GetWorkspacePackages().Single().Name.Should().Be("test_project_10");
    }

    [Fact]
    public async Task GetMetadataAsync_throws_a_cargo_error_for_a_broken_manifest()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[package]\nname = \"broken\"\nversion = \"not-a-version\"\nedition = \"2024\"\n");
        sandbox.WriteFile("src/main.rs", "fn main() {}\n");
        var project = CargoProject.Open(sandbox.Directory);

        //Act
        Func<Task> act = () => project.GetMetadataAsync(new CargoMetadataOptions { Offline = true }, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<CargoCommandException>();
    }

    [Fact]
    public async Task GetMetadataAsync_throws_when_cargo_is_not_found()
    {
        //Arrange
        var options = new RustWorkbenchOptions { CargoPath = Path.Combine(RepoPaths.RustSamplesDirectory, "no-such-cargo") };
        var project = CargoProject.Open(RepoPaths.Sample(6), options);

        //Act
        Func<Task> act = () => project.GetMetadataAsync(null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ToolchainNotFoundException>();
    }
}
