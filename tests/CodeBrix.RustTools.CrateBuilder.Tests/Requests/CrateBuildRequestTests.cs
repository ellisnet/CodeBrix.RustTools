using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Requests;

public class CrateBuildRequestTests
{
    private static CrateBuildRequest Valid() => new CrateBuildRequest
    {
        Source = new LocalManifestSource("/src/my-crate/Cargo.toml"),
        Targets = new List<CrateTarget> { CrateTarget.LinuxX64 },
        OutputDirectory = "/out",
    };

    [Fact]
    public void defaults_are_host_release_nuget_runtimes_without_a_wrapper()
    {
        //Act
        var request = new CrateBuildRequest();

        //Assert
        request.Mode.Should().Be(BuildMode.Host);
        request.Profile.Should().Be(CrateBuildProfile.Release);
        request.Layout.Should().Be(ArtifactLayout.NuGetRuntimes);
        request.LinuxBaseline.Should().Be(LinuxBaseline.RustImage);
        request.Targets.Should().BeEmpty();
        request.Wrapper.Should().BeNull();
        request.LinkToCodeBrixWebGpu.Should().BeFalse();
        request.Timeout.Should().BeNull();
    }

    [Fact]
    public void Validate_accepts_a_minimal_request()
    {
        //Act
        var act = () => Valid().Validate();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Validate_requires_a_source() => ((Action)(() => { var r = Valid(); r.Source = null; r.Validate(); })).Should().Throw<ArgumentException>().WithMessage("*Source is required*");

    [Fact]
    public void Validate_requires_a_local_manifest_path() => ((Action)(() => { var r = Valid(); r.Source = new LocalManifestSource(); r.Validate(); })).Should().Throw<ArgumentException>().WithMessage("*ManifestPath is required*");

    [Fact]
    public void Validate_requires_targets_without_repeats()
    {
        //Act
        var none = () => { var r = Valid(); r.Targets.Clear(); r.Validate(); };
        var twice = () => { var r = Valid(); r.Targets.Add(CrateTarget.LinuxX64); r.Validate(); };
        var nullList = () => { var r = Valid(); r.Targets = null; r.Validate(); };

        //Assert
        none.Should().Throw<ArgumentException>().WithMessage("*at least one*");
        twice.Should().Throw<ArgumentException>().WithMessage("*twice*");
        nullList.Should().Throw<ArgumentException>().WithMessage("*at least one*");
    }

    [Fact]
    public void Validate_requires_an_output_directory() => ((Action)(() => { var r = Valid(); r.OutputDirectory = " "; r.Validate(); })).Should().Throw<ArgumentException>().WithMessage("*OutputDirectory is required*");

    [Fact]
    public void Validate_rejects_the_by_target_layout() => ((Action)(() => { var r = Valid(); r.Layout = ArtifactLayout.ByTarget; r.Validate(); })).Should().Throw<ArgumentException>().WithMessage("*ByTarget*");

    [Fact]
    public void Validate_allows_the_flat_layout_for_one_target_only()
    {
        //Act
        var one = () => { var r = Valid(); r.Layout = ArtifactLayout.Flat; r.Validate(); };
        var two = () => { var r = Valid(); r.Layout = ArtifactLayout.Flat; r.Targets.Add(CrateTarget.LinuxArm64); r.Validate(); };

        //Assert
        one.Should().NotThrow();
        two.Should().Throw<ArgumentException>().WithMessage("*Flat allows one target only*");
    }

    [Fact]
    public void Validate_rejects_manylinux_in_host_mode_and_accepts_it_in_docker_mode()
    {
        //Act
        var host = () => { var r = Valid(); r.LinuxBaseline = LinuxBaseline.ManyLinux; r.Validate(); };
        var docker = () => { var r = Valid(); r.Mode = BuildMode.Docker; r.LinuxBaseline = LinuxBaseline.ManyLinux; r.Validate(); };

        //Assert
        host.Should().Throw<ArgumentException>().WithMessage("*Docker mode only*");
        docker.Should().NotThrow();
    }

    [Fact]
    public void Validate_rejects_blank_features_and_a_non_positive_timeout()
    {
        //Act
        var feature = () => { var r = Valid(); r.Features.Add(""); r.Validate(); };
        var timeout = () => { var r = Valid(); r.Timeout = TimeSpan.Zero; r.Validate(); };

        //Assert
        feature.Should().Throw<ArgumentException>().WithMessage("*blank*");
        timeout.Should().Throw<ArgumentException>().WithMessage("*Timeout must be positive*");
    }

    [Fact]
    public void Validate_checks_the_git_source()
    {
        //Act
        var noUrl = () => { var r = Valid(); r.Source = new GitRepositorySource(); r.Validate(); };
        var twoReferences = () => { var r = Valid(); r.Source = new GitRepositorySource("https://example.com/a.git") { Commit = "abc", Tag = "v1" }; r.Validate(); };
        var rooted = () => { var r = Valid(); r.Source = new GitRepositorySource("https://example.com/a.git", "/etc/Cargo.toml"); r.Validate(); };
        var escaping = () => { var r = Valid(); r.Source = new GitRepositorySource("https://example.com/a.git", "crates/../../Cargo.toml"); r.Validate(); };
        var fine = () => { var r = Valid(); r.Source = new GitRepositorySource("https://example.com/a.git", "crates/core/Cargo.toml") { Branch = "main" }; r.Validate(); };

        //Assert
        noUrl.Should().Throw<ArgumentException>().WithMessage("*Url is required*");
        twoReferences.Should().Throw<ArgumentException>().WithMessage("*at most one of Commit, Branch and Tag*");
        rooted.Should().Throw<ArgumentException>().WithMessage("*relative*");
        escaping.Should().Throw<ArgumentException>().WithMessage("*'..'*");
        fine.Should().NotThrow();
    }

    [Fact]
    public void Validate_checks_the_wrapper() => ((Action)(() => { var r = Valid(); r.Wrapper = new WrapperOptions { AssemblyName = "My.Wrapper" }; r.Validate(); })).Should().Throw<ArgumentException>().WithMessage("*RootNamespace is required*");

    [Fact]
    public void git_source_defaults_to_the_root_manifest_and_no_cleanup()
    {
        //Act
        var source = new GitRepositorySource("https://example.com/a.git");

        //Assert
        source.ManifestPath.Should().Be("Cargo.toml");
        source.CleanUpAfterBuild.Should().BeFalse();
        source.RecurseSubmodules.Should().BeFalse();
        source.CredentialsProvider.Should().BeNull();
        source.CloneDirectory.Should().BeNull();
    }

    [Fact]
    public void Clone_copies_deeply()
    {
        //Arrange
        var original = Valid();
        original.Source = new GitRepositorySource("https://example.com/a.git") { Tag = "v1" };
        original.Features.Add("json");
        original.LicensePolicy = LicensePolicy.Permissive();
        original.Wrapper = new WrapperOptions { AssemblyName = "A", RootNamespace = "A", Plan = new WrapPlan { Notes = "n" } };

        //Act
        var copy = original.Clone();
        copy.Targets.Add(CrateTarget.OsxArm64);
        copy.Features.Add("extra");
        ((GitRepositorySource)copy.Source).Tag = "v2";
        copy.Wrapper.Plan.IncludeTypes.Add("X");

        //Assert
        original.Targets.Should().Equal(CrateTarget.LinuxX64);
        original.Features.Should().Equal("json");
        ((GitRepositorySource)original.Source).Tag.Should().Be("v1");
        original.Wrapper.Plan.IncludeTypes.Should().BeEmpty();
        copy.LicensePolicy.Should().NotBeSameAs(original.LicensePolicy);
        copy.Wrapper.Plan.Notes.Should().Be("n");
        copy.OutputDirectory.Should().Be("/out");
    }
}
