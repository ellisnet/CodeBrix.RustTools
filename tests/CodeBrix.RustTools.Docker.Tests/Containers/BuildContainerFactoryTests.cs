using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Containers;

public class BuildContainerFactoryTests
{
    private static readonly RustBuildImage Image = new RustBuildImage
    {
        ImageName = "rust",
        Tag = "latest",
        ImageId = "sha256:0123456789abcdef",
        SpecHash = "fedcba9876543210",
    };

    private static readonly ResourceLabels Labels = new ResourceLabels("codebrix.rusttools.docker", "wb1");

    [Fact]
    public void BuildSpec_runs_sleep_infinity_in_work()
    {
        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions(), Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.Image.Should().Be("rust:latest");
        spec.Name.Should().Be("box");
        spec.Command.Should().Equal("sleep", "infinity");
        spec.WorkingDir.Should().Be("/work");
    }

    [Fact]
    public void BuildSpec_leaves_the_network_to_the_engine_by_default()
    {
        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions(), Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.NetworkMode.Should().BeNull();
        spec.NetworkName.Should().BeNull();
    }

    [Fact]
    public void BuildSpec_sets_network_mode_none_when_the_network_is_disabled()
    {
        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions { NetworkDisabled = true }, Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.NetworkMode.Should().Be("none");
        spec.Mounts.Select(m => m.Source).Should().Contain("cache");
    }

    [Fact]
    public void ComputeReuseKey_never_matches_a_networked_container_for_a_network_less_request()
    {
        //Act
        var networked = BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions(), "cache");
        var isolated = BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions { NetworkDisabled = true }, "cache");

        //Assert
        isolated.Should().NotBe(networked);
    }

    [Fact]
    public void BuildSpec_mounts_the_two_cache_volumes_and_the_target_volume()
    {
        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions(), Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.Mounts.Select(m => (m.Kind, m.Source, m.Target)).Should().Equal(
            (MountKind.Volume, "cache", "/usr/local/cargo/registry"),
            (MountKind.Volume, "cache-git", "/usr/local/cargo/git"),
            (MountKind.Volume, "box-target", "/work/target"));
    }

    [Fact]
    public void BuildSpec_appends_extra_mounts_and_limits()
    {
        //Arrange
        var limits = new ResourceLimits { Cpus = 2, MemoryBytes = ResourceLimits.Gigabytes(4) };
        var options = new BuildContainerOptions { Mounts = { MountSpec.Tmpfs("/tmp") }, ResourceLimits = limits };

        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, options, Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.Mounts.Should().HaveCount(4);
        spec.Mounts.Last().Target.Should().Be("/tmp");
        spec.Limits.Should().BeSameAs(limits);
    }

    [Fact]
    public void BuildSpec_labels_the_container()
    {
        //Act
        var spec = BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions(), Labels, "cache", "box", "box-target", "key1");

        //Assert
        spec.Labels["codebrix.rusttools.docker"].Should().Be("1");
        spec.Labels["codebrix.rusttools.docker.workbench"].Should().Be("wb1");
        spec.Labels["codebrix.rusttools.docker.kind"].Should().Be("container");
        spec.Labels["codebrix.rusttools.docker.reuse-key"].Should().Be("key1");
        spec.Labels["codebrix.rusttools.docker.spec-hash"].Should().Be("fedcba9876543210");
    }

    [Fact]
    public void BuildSpec_sets_the_standard_environment()
        => BuildContainerFactory.BuildSpec(Image, new BuildContainerOptions(), Labels, "cache", "box", "box-target", "key1")
            .Env.Should().Equal("CARGO_TERM_COLOR=never", "RUSTUP_AUTO_INSTALL=0");

    [Fact]
    public void MergeEnvironment_lets_the_caller_override_and_sorts()
    {
        //Act
        var merged = BuildContainerFactory.MergeEnvironment(new Dictionary<string, string> { ["CARGO_TERM_COLOR"] = "always", ["A_FIRST"] = null });

        //Assert
        merged.Select(p => p.Key + "=" + p.Value).Should().Equal("A_FIRST=", "CARGO_TERM_COLOR=always", "RUSTUP_AUTO_INSTALL=0");
    }

    [Theory]
    [InlineData("")]
    [InlineData("A=B")]
    public void MergeEnvironment_rejects_invalid_names(string name)
    {
        //Act
        var act = () => BuildContainerFactory.MergeEnvironment(new Dictionary<string, string> { [name] = "x" });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ComputeReuseKey_is_stable_for_equal_settings()
    {
        //Arrange
        BuildContainerOptions Make() => new BuildContainerOptions { Env = { ["B"] = "2", ["A"] = "1" }, ResourceLimits = new ResourceLimits { Cpus = 1.5 } };

        //Act
        var first = BuildContainerFactory.ComputeReuseKey(Image, Make(), "cache");
        var second = BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions { Env = { ["A"] = "1", ["B"] = "2" }, ResourceLimits = new ResourceLimits { Cpus = 1.5 } }, "cache");

        //Assert
        first.Should().MatchRegex("^[0-9a-f]{16}$");
        second.Should().Be(first);
        BuildContainerFactory.ComputeReuseKey(Image, Make(), "cache").Should().Be(first);
    }

    [Fact]
    public void ComputeReuseKey_changes_with_the_image_env_volume_or_limits()
    {
        //Arrange
        var baseline = BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions(), "cache");
        var otherImage = new RustBuildImage { ImageName = "rust", Tag = "latest", ImageId = "sha256:other" };

        //Assert
        BuildContainerFactory.ComputeReuseKey(otherImage, new BuildContainerOptions(), "cache").Should().NotBe(baseline);
        BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions { Env = { ["X"] = "1" } }, "cache").Should().NotBe(baseline);
        BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions(), "other-cache").Should().NotBe(baseline);
        BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions { ResourceLimits = new ResourceLimits { PidsLimit = 100 } }, "cache").Should().NotBe(baseline);
        BuildContainerFactory.ComputeReuseKey(Image, new BuildContainerOptions { Mounts = { MountSpec.Tmpfs("/tmp") } }, "cache").Should().NotBe(baseline);
    }

    [Fact]
    public void ResolveName_uses_the_given_name()
        => BuildContainerFactory.ResolveName(new DockerRustWorkbenchOptions(), new BuildContainerOptions { Name = " mine " }, "key1").Should().Be("mine");

    [Fact]
    public void ResolveName_uses_the_reuse_key_when_reusing()
        => BuildContainerFactory.ResolveName(new DockerRustWorkbenchOptions(), new BuildContainerOptions { ReuseExisting = true }, "0011223344556677")
            .Should().Be("codebrix-rusttools-0011223344556677");

    [Fact]
    public void ResolveName_is_random_otherwise()
    {
        //Act
        var first = BuildContainerFactory.ResolveName(new DockerRustWorkbenchOptions(), new BuildContainerOptions(), "key1");
        var second = BuildContainerFactory.ResolveName(new DockerRustWorkbenchOptions(), new BuildContainerOptions(), "key1");

        //Assert
        first.Should().MatchRegex("^codebrix-rusttools-[0-9a-f]{12}$");
        second.Should().NotBe(first);
    }

    [Fact]
    public void ResolveTargetVolume_defaults_to_the_container_name()
    {
        //Assert
        BuildContainerFactory.ResolveTargetVolume(new BuildContainerOptions(), "box").Should().Be("box-target");
        BuildContainerFactory.ResolveTargetVolume(new BuildContainerOptions { TargetVolumeName = "shared-target" }, "box").Should().Be("shared-target");
    }
}
