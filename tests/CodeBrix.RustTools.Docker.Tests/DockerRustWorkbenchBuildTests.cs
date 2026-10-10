using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests;

public class DockerRustWorkbenchBuildTests
{
    private const string MissingSocket = "unix:///nonexistent/codebrix-rusttools/docker.sock";

    [Fact]
    public async Task BuildAsync_rejects_a_null_request()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();

        //Act
        var act = () => workbench.BuildAsync(null, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BuildAsync_validates_the_request_before_contacting_the_engine()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions { Endpoint = MissingSocket });
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = "/a",
            Targets = new List<CrossTarget> { CrossTarget.MacOSArm64, CrossTarget.MacOSX64 },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = "/out", Layout = ArtifactLayout.Flat },
        };

        //Act
        var act = () => workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BuildAsync_throws_DockerUnavailableException_with_guidance_when_the_engine_is_down()
    {
        //Arrange
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Unix domain socket endpoints are not used on Windows.");
        using var sandbox = SampleSandbox.FromSample(13);
        using var workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions { Endpoint = MissingSocket });
        var stages = new List<BuildStage>();
        var progress = new SynchronousProgress<ContainerBuildProgress>(p => stages.Add(p.Stage));

        //Act
        var act = () => workbench.BuildAsync(new ContainerBuildRequest { ProjectDirectory = sandbox.Directory }, progress, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<DockerUnavailableException>();
        thrown.Which.Engine.Issues.Should().Contain(DockerEngineIssue.NotReachable);
        thrown.Which.Guidance.Should().NotBeEmpty();
        stages.Should().Equal(BuildStage.CheckingEngine, BuildStage.Failed);
    }

    [Theory]
    [InlineData(null, "/work")]
    [InlineData("", "/work")]
    [InlineData("crates/cli", "/work/crates/cli")]
    [InlineData("./crates/cli/", "/work/crates/cli")]
    [InlineData("/opt/src", "/opt/src")]
    [InlineData("/", "/")]
    public void ResolveContainerDirectory_maps_relative_paths_below_work(string workingDirectory, string expected)
        => DockerRustWorkbench.ResolveContainerDirectory(workingDirectory).Should().Be(expected);

    [Fact]
    public void ResolveContainerDirectory_rejects_parent_segments()
    {
        //Act
        var act = () => DockerRustWorkbench.ResolveContainerDirectory("../etc");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Publish_raises_the_event_and_survives_a_throwing_handler()
    {
        //Arrange
        using var workbench = new DockerRustWorkbench();
        var seen = new List<string>();
        workbench.Progress += (_, p) => throw new InvalidOperationException("handler bug");
        var progress = new SynchronousProgress<ContainerBuildProgress>(p => seen.Add(p.Message));

        //Act
        workbench.Publish(new ContainerBuildProgress { Stage = BuildStage.CopyingSource, Message = "copying" }, progress);

        //Assert
        seen.Should().Equal("copying");
    }

    [Fact]
    public void ClearWorkCommand_keeps_the_target_volume_and_mount_points()
    {
        //Act
        var script = DockerRustWorkbench.ClearWorkCommand[2];

        //Assert
        DockerRustWorkbench.ClearWorkCommand[0].Should().Be("sh");
        script.Should().Contain("/work/target ] && continue");
        script.Should().Contain("mountpoint -q");
        script.Should().Contain("rm -rf");
    }
}
