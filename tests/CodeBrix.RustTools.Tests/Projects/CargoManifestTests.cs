using System;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Projects;

public class CargoManifestTests
{
    private static CargoManifest Sample(int number, string relative = "Cargo.toml")
        => CargoManifest.Load(Path.Combine(RepoPaths.Sample(number), relative));

    [Fact]
    public void Load_reads_a_plain_package_with_registry_dependencies()
    {
        //Act
        var manifest = Sample(2);

        //Assert
        manifest.Package.Name.Should().Be("test_project_02");
        manifest.Package.Version.Should().Be("0.1.0");
        manifest.Package.Edition.Should().Be("2024");
        manifest.Package.Publish.Should().Be(false);
        manifest.Package.InheritedFields.Should().BeEmpty();
        manifest.IsWorkspaceRoot.Should().BeFalse();
        manifest.Dependencies.Select(d => d.Name).Should().ContainInOrder("anyhow", "clap", "csv", "serde", "serde_json");
        manifest.Dependencies.Should().OnlyContain(d => d.IsRegistry && d.Kind == CargoDependencyKind.Normal);
    }

    [Fact]
    public void Load_reads_inline_dependency_tables()
    {
        //Act
        var clap = Sample(2).Dependencies.Single(d => d.Name == "clap");

        //Assert
        clap.VersionRequirement.Should().Be("4.6");
        clap.Features.Should().ContainSingle().Which.Should().Be("derive");
        clap.DefaultFeatures.Should().BeTrue();
        clap.Optional.Should().BeFalse();
    }

    [Fact]
    public void Load_reads_a_virtual_workspace_root()
    {
        //Act
        var manifest = Sample(4);

        //Assert
        manifest.Package.Should().BeNull();
        manifest.IsVirtual.Should().BeTrue();
        manifest.Workspace.Members.Should().ContainInOrder("crates/unitconv_core", "crates/unitconv_report", "crates/unitconv_cli");
        manifest.Workspace.Resolver.Should().Be("3");
        manifest.Workspace.Package.Version.Should().Be("0.1.0");
        manifest.Workspace.Package.License.Should().Be("MIT");
        manifest.Workspace.Package.Publish.Should().Be(false);
        manifest.Workspace.Dependencies.Should().HaveCount(6);
        manifest.Workspace.Dependencies.Single(d => d.Name == "unitconv_core").Path.Should().Be("crates/unitconv_core");
        manifest.GetProfile("release").Lto.Should().Be("thin");
    }

    [Fact]
    public void Load_records_workspace_inheritance_markers()
    {
        //Act
        var core = Sample(4, "crates/unitconv_core/Cargo.toml");

        //Assert
        core.Package.InheritedFields.Should().ContainInOrder("version", "edition", "license", "publish");
        core.Package.IsInherited("license").Should().BeTrue();
        core.Package.License.Should().BeNull();
        core.Package.Version.Should().BeNull();
        core.Dependencies.Should().OnlyContain(d => d.IsWorkspaceInherited);
        core.GetDependencies(CargoDependencyKind.Dev).Single().Name.Should().Be("serde_json");
    }

    [Theory]
    [InlineData("crates/unitconv_core/Cargo.toml", null)]
    [InlineData("crates/unitconv_report/Cargo.toml", "GPL-3.0-only")]
    [InlineData("crates/unitconv_cli/Cargo.toml", "MIT OR Apache-2.0")]
    public void Load_reads_each_workspace_member_license(string relative, string license)
        => Sample(4, relative).Package.License.Should().Be(license);

    [Fact]
    public void WithInheritance_fills_inherited_fields_from_the_workspace_package()
    {
        //Arrange
        var root = Sample(4);
        var report = Sample(4, "crates/unitconv_report/Cargo.toml");

        //Act
        var core = Sample(4, "crates/unitconv_core/Cargo.toml").Package.WithInheritance(root.Workspace.Package);
        var effectiveReport = report.Package.WithInheritance(root.Workspace.Package);

        //Assert
        core.License.Should().Be("MIT");
        core.Version.Should().Be("0.1.0");
        core.Edition.Should().Be("2024");
        effectiveReport.License.Should().Be("GPL-3.0-only");
        effectiveReport.Publish.Should().Be(false);
    }

    [Fact]
    public void Load_reads_features_and_optional_dependencies()
    {
        //Act
        var manifest = Sample(5);

        //Assert
        manifest.Features.Select(f => f.Name).Should().ContainInOrder("default", "case", "json");
        manifest.GetFeature("default").IsDefault.Should().BeTrue();
        manifest.GetFeature("default").Enables.Should().ContainSingle().Which.Should().Be("case");
        manifest.GetFeature("json").Enables.Should().ContainInOrder("dep:serde", "dep:serde_json");
        manifest.GetFeature("case").Enables.Should().BeEmpty();
        manifest.Dependencies.Where(d => d.Optional).Select(d => d.Name).Should().ContainInOrder("serde", "serde_json");
    }

    [Fact]
    public void Load_reads_vendor_path_dependencies()
    {
        //Act
        var paths = Sample(5).Dependencies.Where(d => d.IsPath).ToList();

        //Assert
        paths.Select(d => d.Name).Should().ContainInOrder("licensed_by_file", "unknown_license_id", "no_license_at_all");
        paths[0].Path.Should().Be("vendor/licensed_by_file");
        paths.Should().OnlyContain(d => !d.IsRegistry && d.VersionRequirement == null);
    }

    [Fact]
    public void Load_reads_lib_bin_and_example_declarations()
    {
        //Act
        var manifest = Sample(5);

        //Assert
        manifest.Lib.Name.Should().Be("textkit");
        manifest.Lib.Path.Should().Be("src/lib.rs");
        manifest.Bins.Should().ContainSingle().Which.Path.Should().Be("src/main.rs");
        var example = manifest.Examples.Should().ContainSingle().Subject;
        example.Name.Should().Be("json_report");
        example.RequiredFeatures.Should().ContainSingle().Which.Should().Be("json");
        example.IsDeclared.Should().BeTrue();
    }

    [Fact]
    public void Load_reads_autobins_build_script_and_bins()
    {
        //Act
        var manifest = Sample(6);

        //Assert
        manifest.Package.AutoBins.Should().Be(false);
        manifest.Package.AutoExamples.Should().BeNull();
        manifest.Package.BuildScript.Should().Be("build.rs");
        manifest.Package.BuildScriptDisabled.Should().BeFalse();
        manifest.Package.Version.Should().Be("0.3.1");
        manifest.Bins.Select(b => b.Name).Should().ContainInOrder("buildinfo", "crc32sum");
        manifest.Bins.Select(b => b.Path).Should().ContainInOrder("src/programs/buildinfo.rs", "src/programs/crc32sum.rs");
        manifest.Dependencies.Should().BeEmpty();
    }

    [Fact]
    public void Load_reads_bench_declarations_and_dev_dependencies()
    {
        //Act
        var manifest = Sample(12);

        //Assert
        var bench = manifest.Benches.Should().ContainSingle().Subject;
        bench.Name.Should().Be("record_throughput");
        bench.Harness.Should().BeFalse();
        manifest.Lib.Name.Should().Be("batchjob");
        manifest.Lib.Path.Should().BeNull();
        manifest.GetDependencies(CargoDependencyKind.Dev).Single().Name.Should().Be("criterion");
        manifest.GetDependencies(CargoDependencyKind.Normal).Should().HaveCount(7);
    }

    [Fact]
    public void Load_reads_profiles_with_package_overrides()
    {
        //Act
        var dev = Sample(11).GetProfile("dev");

        //Assert
        dev.Should().NotBeNull();
        dev.PackageOverrides.Should().ContainSingle().Which.Should().Be("*");
        Sample(7).GetProfile("release").CodegenUnits.Should().Be(1);
    }

    [Fact]
    public void Load_reads_default_features_false()
    {
        //Act
        var reqwest = Sample(3).Dependencies.Single(d => d.Name == "reqwest");

        //Assert
        reqwest.DefaultFeatures.Should().BeFalse();
        reqwest.Features.Should().ContainInOrder("rustls", "json", "http2", "charset");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Load_parses_every_sample_manifest(int number)
    {
        //Act
        var manifest = Sample(number);

        //Assert
        manifest.FilePath.Should().Be(Path.Combine(RepoPaths.Sample(number), "Cargo.toml"));
        (manifest.Package != null || manifest.Workspace != null).Should().BeTrue();
    }

    [Fact]
    public void Parse_reads_target_specific_renamed_and_git_dependencies()
    {
        //Arrange
        const string toml = """
            [package]
            name = "demo"
            version = "1.2.3"
            license = "MIT"
            license-file = "LICENSE"
            rust-version = "1.80"
            build = false

            [dependencies]
            json = { package = "serde_json", version = "1" }
            tool = { git = "https://example.com/tool.git", branch = "main" }

            [target.'cfg(windows)'.dependencies]
            winapi = "0.3"

            [target.x86_64-unknown-linux-gnu.build-dependencies]
            cc = "1"
            """;

        //Act
        var manifest = CargoManifest.Parse(toml);

        //Assert
        manifest.FilePath.Should().BeNull();
        manifest.Package.RustVersion.Should().Be("1.80");
        manifest.Package.LicenseFile.Should().Be("LICENSE");
        manifest.Package.BuildScriptDisabled.Should().BeTrue();
        var json = manifest.Dependencies.Single(d => d.Name == "json");
        json.PackageName.Should().Be("serde_json");
        var tool = manifest.Dependencies.Single(d => d.Name == "tool");
        tool.IsGit.Should().BeTrue();
        tool.Branch.Should().Be("main");
        manifest.Dependencies.Single(d => d.Name == "winapi").Target.Should().Be("cfg(windows)");
        var cc = manifest.Dependencies.Single(d => d.Name == "cc");
        cc.Kind.Should().Be(CargoDependencyKind.Build);
        cc.Target.Should().Be("x86_64-unknown-linux-gnu");
    }

    [Fact]
    public void Parse_throws_for_invalid_toml()
    {
        //Arrange
        Action act = () => CargoManifest.Parse("[package\nname = ");

        //Assert
        act.Should().Throw<RustToolsException>();
    }

    [Fact]
    public void Load_throws_for_a_missing_file()
    {
        //Arrange
        Action act = () => CargoManifest.Load(Path.Combine(RepoPaths.RustSamplesDirectory, "no_such", "Cargo.toml"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }
}
