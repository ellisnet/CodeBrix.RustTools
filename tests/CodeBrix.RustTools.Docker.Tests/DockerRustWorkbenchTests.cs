using System;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests;

public class DockerRustWorkbenchTests
{
    private const string MissingSocket = "unix:///nonexistent/codebrix-rusttools/docker.sock";

    [Fact]
    public void can_create_with_default_options()
    {
        //Act
        using var workbench = new DockerRustWorkbench();

        //Assert
        workbench.Options.DefaultImage.Should().Be("rust");
        workbench.InstanceId.Should().MatchRegex("^[0-9a-f]{32}$");
    }

    [Fact]
    public void each_workbench_has_its_own_instance_id()
    {
        //Arrange
        using var first = new DockerRustWorkbench();
        using var second = new DockerRustWorkbench();

        //Assert
        second.InstanceId.Should().NotBe(first.InstanceId);
    }

    [Fact]
    public void Options_are_a_private_copy()
    {
        //Arrange
        var options = new DockerRustWorkbenchOptions { CacheVolumeName = "first" };
        using var workbench = new DockerRustWorkbench(options);

        //Act
        options.CacheVolumeName = "second";

        //Assert
        workbench.Options.CacheVolumeName.Should().Be("first");
        workbench.Options.Should().NotBeSameAs(options);
    }

    [Fact]
    public void constructor_rejects_blank_required_options()
    {
        //Act
        var act = () => new DockerRustWorkbench(new DockerRustWorkbenchOptions { CacheVolumeName = "" });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task CheckEngineAsync_reports_an_unsupported_endpoint_without_throwing()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions { Endpoint = "https://docker.example:2376" });

        //Act
        var info = await workbench.CheckEngineAsync(TestContext.Current.CancellationToken);

        //Assert
        info.IsAvailable.Should().BeFalse();
        info.Issues.Should().Contain(DockerEngineIssue.EndpointNotSupported);
        workbench.GetDockerGuidance(info).Should().Contain(g => g.Topic == DockerGuidanceTopic.CheckEndpoint);
    }

    [Fact]
    public async Task EnsureImageAsync_throws_with_guidance_when_the_engine_is_unreachable()
    {
        //Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix domain socket endpoints are not used on Windows.");
        using var workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions { Endpoint = MissingSocket });

        //Act
        var act = () => workbench.EnsureImageAsync(null, null, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<DockerUnavailableException>();
        thrown.Which.Engine.Issues.Should().Contain(DockerEngineIssue.NotReachable);
        thrown.Which.Guidance.Should().Contain(g => g.Topic == DockerGuidanceTopic.InstallDocker);
    }

    [Fact]
    public async Task EnsureImageAsync_rejects_an_invalid_spec_before_contacting_the_engine()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions { Endpoint = "https://docker.example:2376" });
        var spec = new BuildImageSpec { CrossTargets = { CrossTarget.MacOSArm64 }, AptPackages = { "not a package" } };

        //Act
        var act = () => workbench.EnsureImageAsync(spec, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StartContainerAsync_requires_an_image()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();

        //Act
        var act = () => workbench.StartContainerAsync(null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecAsync_requires_a_container()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();

        //Act
        var act = () => workbench.ExecAsync(null, new[] { "true" }, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ExecAsync_requires_a_command()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();
        var container = new BuildContainer(null, "id", "name", new RustBuildImage(), "cache", "target", "key", keepOnDispose: true, wasReused: false);

        //Act
        var act = () => workbench.ExecAsync(container, Array.Empty<string>(), TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task calls_after_dispose_throw()
    {
        //Arrange
        var workbench = new DockerRustWorkbench();
        workbench.Dispose();

        //Act
        var act = () => workbench.CheckEngineAsync(TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_can_be_called_twice()
    {
        //Arrange
        var workbench = new DockerRustWorkbench();
        workbench.Dispose();

        //Act
        var act = () => workbench.Dispose();

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void GetDockerGuidance_is_empty_for_a_healthy_engine()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();

        //Act
        var guidance = workbench.GetDockerGuidance(new DockerEngineInfo { IsAvailable = true, HostOperatingSystem = OperatingSystemKind.DebianLinux });

        //Assert
        guidance.Should().BeEmpty();
    }
}
