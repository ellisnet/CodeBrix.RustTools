using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(DockerEngineCollection.Name)]
public class DockerRustWorkbenchLiveTests
{
    private const string Prefix = DockerRustWorkbenchOptions.DefaultLabelPrefix;

    private readonly DockerEngineFixture _fixture;

    public DockerRustWorkbenchLiveTests(DockerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    private DockerRustWorkbench Workbench => _fixture.Workbench;

    private DockerClient Client => _fixture.Workbench.Client;

    [Fact]
    public async Task CheckEngineAsync_reports_an_available_linux_daemon()
    {
        //Act
        var info = await Workbench.CheckEngineAsync(TestContext.Current.CancellationToken);

        //Assert
        info.IsAvailable.Should().BeTrue();
        info.IsLinuxDaemon.Should().BeTrue();
        info.OsType.Should().Be("linux");
        info.ServerVersion.Should().NotBeNullOrWhiteSpace();
        info.Endpoint.Should().NotBeNullOrWhiteSpace();
        info.NativeLinuxTriple.Should().NotBeNull();
        info.Issues.Should().NotContain(DockerEngineIssue.NotReachable);
        info.Issues.Should().NotContain(DockerEngineIssue.PermissionDenied);
        Workbench.GetDockerGuidance(info).Where(g => g.Topic != DockerGuidanceTopic.InstallDockerCli).Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureImageAsync_uses_the_base_image_for_the_default_spec()
    {
        //Act
        var image = await Workbench.EnsureImageAsync(null, null, TestContext.Current.CancellationToken);

        //Assert
        image.IsBaseImage.Should().BeTrue();
        image.WasBuilt.Should().BeFalse();
        image.WasReused.Should().BeTrue();
        image.Reference.Should().Be("rust:latest");
        image.ImageId.Should().StartWith("sha256:");
        image.ImageId.Should().Be(_fixture.BaseImage.ImageId);
        image.Dockerfile.Should().StartWith(DockerfileGenerator.HeaderLine);
    }

    [Fact]
    public async Task EnsureImageAsync_builds_a_derived_image_once_and_then_reuses_it()
    {
        //Arrange
        var spec = new BuildImageSpec { InstallClippyRustfmt = true };
        var expectedTag = DockerfileGenerator.ComputeSpecHash(DockerfileGenerator.Generate(spec));
        await RemoveImageIfPresentAsync(DockerEngineFixture.TestImageRepository + ":" + expectedTag);
        var lines = new List<string>();
        var progress = new SynchronousProgress<string>(lines.Add);

        //Act
        var built = await Workbench.EnsureImageAsync(spec, progress, TestContext.Current.CancellationToken);
        var again = await Workbench.EnsureImageAsync(new BuildImageSpec { InstallClippyRustfmt = true }, null, TestContext.Current.CancellationToken);

        //Assert
        built.WasBuilt.Should().BeTrue();
        built.IsBaseImage.Should().BeFalse();
        built.ImageName.Should().Be(DockerEngineFixture.TestImageRepository);
        built.Tag.Should().Be(expectedTag);
        built.BaseImageReference.Should().Be("rust:latest");
        lines.Should().NotBeEmpty();
        again.WasBuilt.Should().BeFalse();
        again.WasReused.Should().BeTrue();
        again.ImageId.Should().Be(built.ImageId);

        var inspect = await Client.Images.InspectAsync(built.Reference, TestContext.Current.CancellationToken);
        inspect.Config.Labels[Prefix].Should().Be("1");
        inspect.Config.Labels[Prefix + ".spec-hash"].Should().Be(expectedTag);
        inspect.Config.Labels[Prefix + ".kind"].Should().Be("image");
    }

    [Fact]
    public async Task EnsureImageAsync_builds_a_containerfile_image_once_and_uses_it_whole_or_as_the_base_of_layers()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var marker = Guid.NewGuid().ToString("N");
        var containerfile = sandbox.WriteFile("Containerfile", $"FROM {_fixture.BaseImage.Reference}\nARG NOTE=unset\nCOPY marker.txt /opt/marker.txt\nRUN echo \"$NOTE\" > /opt/note.txt\n");
        sandbox.WriteFile("marker.txt", marker + "\n");
        BuildImageSpec Spec(params string[] extraLines)
        {
            var spec = new BuildImageSpec { Containerfile = containerfile, ContainerfileBuildArgs = { ["NOTE"] = "from-args" } };
            foreach (var line in extraLines)
            {
                spec.ExtraDockerfileLines.Add(line);
            }

            return spec;
        }

        //Act
        var whole = await Workbench.EnsureImageAsync(Spec(), null, TestContext.Current.CancellationToken);
        var again = await Workbench.EnsureImageAsync(Spec(), null, TestContext.Current.CancellationToken);
        var layered = await Workbench.EnsureImageAsync(Spec("RUN echo layered > /opt/layer.txt"), null, TestContext.Current.CancellationToken);
        await using var container = await Workbench.StartContainerAsync(layered, null, TestContext.Current.CancellationToken);
        var files = await Workbench.ExecAsync(container, new[] { "sh", "-c", "cat /opt/marker.txt /opt/note.txt /opt/layer.txt" }, TestContext.Current.CancellationToken);
        var cargo = await Workbench.ExecAsync(container, new[] { "cargo", "--version" }, TestContext.Current.CancellationToken);

        //Assert
        whole.WasContainerfileImageBuilt.Should().BeTrue();
        whole.WasBuilt.Should().BeTrue();
        whole.IsBaseImage.Should().BeTrue();
        whole.ImageName.Should().Be(DockerEngineFixture.TestImageRepository);
        whole.Tag.Should().StartWith("containerfile-");
        whole.ContainerfileImageReference.Should().Be(whole.Reference);
        again.WasContainerfileImageBuilt.Should().BeFalse();
        again.WasReused.Should().BeTrue();
        again.ImageId.Should().Be(whole.ImageId);
        layered.IsBaseImage.Should().BeFalse();
        layered.WasContainerfileImageBuilt.Should().BeFalse();
        layered.BaseImageReference.Should().Be(whole.Reference);
        layered.ContainerfileImageReference.Should().Be(whole.Reference);
        files.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal(marker, "from-args", "layered");
        cargo.Should().StartWith("cargo ");

        var inspect = await Client.Images.InspectAsync(whole.Reference, TestContext.Current.CancellationToken);
        inspect.Config.Labels[Prefix].Should().Be("1");
        inspect.Config.Labels[Prefix + ".kind"].Should().Be("image");
        inspect.Config.Labels[Prefix + ".spec-hash"].Should().Be(whole.Tag.Substring("containerfile-".Length));
    }

    [Fact]
    public async Task derived_image_has_clippy_and_rustfmt()
    {
        //Arrange
        var image = await Workbench.EnsureImageAsync(new BuildImageSpec { InstallClippyRustfmt = true }, null, TestContext.Current.CancellationToken);
        await using var container = await Workbench.StartContainerAsync(image, null, TestContext.Current.CancellationToken);

        //Act
        var clippy = await Workbench.ExecAsync(container, new[] { "cargo", "clippy", "--version" }, TestContext.Current.CancellationToken);
        var rustfmt = await Workbench.ExecAsync(container, new[] { "rustfmt", "--version" }, TestContext.Current.CancellationToken);

        //Assert
        ToolVersion.Parse(clippy).ToolName.Should().Be("clippy");
        ToolVersion.Parse(rustfmt).ToolName.Should().Be("rustfmt");
    }

    [Fact]
    public async Task StartContainerAsync_starts_a_running_container_with_the_three_mounts()
    {
        //Arrange
        var container = _fixture.SharedContainer;

        //Act
        var inspect = await Client.Containers.InspectAsync(container.Id, TestContext.Current.CancellationToken);

        //Assert
        inspect.IsRunning.Should().BeTrue();
        container.IsRunning.Should().BeTrue();
        container.WasReused.Should().BeFalse();
        inspect.Config.WorkingDir.Should().Be("/work");
        inspect.Config.Cmd.Should().Equal("sleep", "infinity");
        inspect.Config.Labels[Prefix].Should().Be("1");
        inspect.Config.Labels[Prefix + ".workbench"].Should().Be(Workbench.InstanceId);
        inspect.Mounts.Where(m => m.Type == "volume").Select(m => (m.Name, m.Destination)).Should().BeEquivalentTo(new[]
        {
            (DockerEngineFixture.TestCacheVolumeName, "/usr/local/cargo/registry"),
            (DockerEngineFixture.TestCacheVolumeName + "-git", "/usr/local/cargo/git"),
            (container.TargetVolumeName, "/work/target"),
        });
    }

    [Fact]
    public async Task StartContainerAsync_labels_the_volumes_it_creates()
    {
        //Act
        var cache = await Client.Volumes.InspectAsync(DockerEngineFixture.TestCacheVolumeName, TestContext.Current.CancellationToken);
        var target = await Client.Volumes.InspectAsync(_fixture.SharedContainer.TargetVolumeName, TestContext.Current.CancellationToken);

        //Assert
        cache.Labels[Prefix].Should().Be("1");
        cache.Labels[Prefix + ".kind"].Should().Be("cargo-cache");
        target.Labels[Prefix].Should().Be("1");
        target.Labels[Prefix + ".kind"].Should().Be("target");
        target.Labels[Prefix + ".workbench"].Should().Be(Workbench.InstanceId);
    }

    [Fact]
    public async Task ExecAsync_runs_cargo_inside_the_container()
    {
        //Act
        var output = await Workbench.ExecAsync(_fixture.SharedContainer, new[] { "cargo", "--version" }, TestContext.Current.CancellationToken);
        var version = ToolVersion.Parse(output);

        //Assert
        version.ToolName.Should().Be("cargo");
        version.Major.Should().Be(1);
        version.Minor.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ExecAsync_lists_the_native_linux_target_as_installed()
    {
        //Act
        var output = await Workbench.ExecAsync(_fixture.SharedContainer, new[] { "rustup", "target", "list", "--installed" }, TestContext.Current.CancellationToken);

        //Assert
        output.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Should().Contain(_fixture.Engine.NativeLinuxTriple);
    }

    [Fact]
    public async Task ExecAsync_runs_in_work_with_the_standard_environment()
    {
        //Act
        var directory = await Workbench.ExecAsync(_fixture.SharedContainer, new[] { "pwd" }, TestContext.Current.CancellationToken);
        var environment = await Workbench.ExecAsync(_fixture.SharedContainer, new[] { "printenv" }, TestContext.Current.CancellationToken);

        //Assert
        directory.Trim().Should().Be("/work");
        environment.Should().Contain("CARGO_TERM_COLOR=never");
        environment.Should().Contain("RUSTUP_AUTO_INSTALL=0");
    }

    [Fact]
    public async Task ExecAsync_throws_with_the_exit_code_and_stderr_on_failure()
    {
        //Act
        var act = () => Workbench.ExecAsync(_fixture.SharedContainer, new[] { "sh", "-c", "echo partial; echo oops >&2; exit 3" }, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<ContainerBuildException>();
        thrown.Which.ExitCode.Should().Be(3);
        thrown.Which.StandardError.Should().Contain("oops");
        thrown.Which.StandardOutput.Should().Contain("partial");
        thrown.Which.Command.Should().Equal("sh", "-c", "echo partial; echo oops >&2; exit 3");
    }

    [Fact]
    public async Task RunStreamingAsync_reports_each_stream_line_by_line()
    {
        //Arrange
        var stdout = new List<string>();
        var stderr = new List<string>();

        //Act
        var result = await ContainerCommandRunner.RunStreamingAsync(
            Client,
            _fixture.SharedContainer.Id,
            new[] { "sh", "-c", "echo one; echo two >&2; printf 'three'; exit 4" },
            BuildContainer.WorkDirectory,
            new[] { "EXTRA=1" },
            stdout.Add,
            stderr.Add,
            TestContext.Current.CancellationToken);

        //Assert
        stdout.Should().Equal("one", "three");
        stderr.Should().Equal("two");
        result.ExitCode.Should().Be(4);
        result.StandardOutput.Should().Be("one\nthree");
        result.Succeeded.Should().BeFalse();
    }

    [Theory]
    [InlineData(5000, 200, false)]
    [InlineData(5000, 200, true)]
    [InlineData(100, 65536, false)]
    public async Task RunStreamingAsync_delivers_every_line_of_a_large_output_in_order(int lineCount, int padLength, bool interleaveStandardError)
    {
        //Arrange
        var stdout = new List<string>();
        var stderr = new List<string>();
        var pad = new string('x', padLength);
        var errorLine = interleaveStandardError ? "echo \"err $i\" >&2; " : string.Empty;
        var script = $"p={pad}; i=0; while [ $i -lt {lineCount} ]; do echo \"{{\\\"line\\\":$i,\\\"pad\\\":\\\"$p\\\"}}\"; " + errorLine + "i=$((i+1)); done";

        //Act
        var result = await ContainerCommandRunner.RunStreamingAsync(
            Client,
            _fixture.SharedContainer.Id,
            new[] { "sh", "-c", script },
            BuildContainer.WorkDirectory,
            null,
            stdout.Add,
            stderr.Add,
            TestContext.Current.CancellationToken);

        //Assert
        result.ExitCode.Should().Be(0);
        stdout.Should().HaveCount(lineCount);
        Enumerable.Range(0, lineCount).All(i => stdout[i] == "{\"line\":" + i + ",\"pad\":\"" + pad + "\"}").Should().BeTrue("every standard output line must arrive intact and in order");
        stderr.Should().Equal(interleaveStandardError ? Enumerable.Range(0, lineCount).Select(i => "err " + i) : Enumerable.Empty<string>());
    }

    [Fact]
    public async Task StartContainerAsync_reuses_a_matching_container()
    {
        //Arrange
        var options = new BuildContainerOptions { ReuseExisting = true, Env = { ["CODEBRIX_TEST_RUN"] = Guid.NewGuid().ToString("N") } };

        //Act
        await using var first = await Workbench.StartContainerAsync(_fixture.BaseImage, options, TestContext.Current.CancellationToken);
        await using var second = await Workbench.StartContainerAsync(_fixture.BaseImage, options, TestContext.Current.CancellationToken);

        //Assert
        first.WasReused.Should().BeFalse();
        second.WasReused.Should().BeTrue();
        second.Id.Should().Be(first.Id);
        second.Name.Should().Be(first.Name);
        second.TargetVolumeName.Should().Be(first.TargetVolumeName);
        second.ReuseKey.Should().Be(first.ReuseKey);
    }

    [Fact]
    public async Task StopAndRemoveAsync_removes_the_container_and_its_target_volume()
    {
        //Arrange
        var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);

        //Act
        await container.StopAndRemoveAsync(cancellationToken: TestContext.Current.CancellationToken);
        await container.StopAndRemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        container.IsRemoved.Should().BeTrue();
        container.IsRunning.Should().BeFalse();
        (await container.RefreshStateAsync(TestContext.Current.CancellationToken)).Should().BeFalse();
        var inspectContainer = () => Client.Containers.InspectAsync(container.Id, TestContext.Current.CancellationToken);
        await inspectContainer.Should().ThrowAsync<DockerContainerNotFoundException>();
        var inspectVolume = () => Client.Volumes.InspectAsync(container.TargetVolumeName, TestContext.Current.CancellationToken);
        (await inspectVolume.Should().ThrowAsync<DockerApiException>()).Which.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ExecAsync_refuses_a_removed_container()
    {
        //Arrange
        var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await container.StopAndRemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Act
        var act = () => Workbench.ExecAsync(container, new[] { "true" }, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ContainerBuildException>();
    }

    [Fact]
    public async Task DisposeAsync_leaves_a_kept_container_running()
    {
        //Arrange
        var container = await Workbench.StartContainerAsync(_fixture.BaseImage, new BuildContainerOptions { KeepOnDispose = true }, TestContext.Current.CancellationToken);

        //Act
        await container.DisposeAsync();
        var running = await container.RefreshStateAsync(TestContext.Current.CancellationToken);
        await container.StopAndRemoveAsync(cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        running.Should().BeTrue();
        container.IsRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_removes_a_container_that_is_not_kept()
    {
        //Arrange
        var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);

        //Act
        await container.DisposeAsync();

        //Assert
        container.IsRemoved.Should().BeTrue();
        (await Client.Containers.ListAsync(true, new Dictionary<string, string> { [Prefix + ".reuse-key"] = container.ReuseKey }, TestContext.Current.CancellationToken))
            .Should().NotContain(c => c.Id == container.Id);
    }

    [Fact]
    public async Task CleanupAsync_removes_labelled_leftovers_of_one_workbench()
    {
        //Arrange
        using var other = new DockerRustWorkbench(DockerEngineFixture.CreateOptions());
        var leftover = await other.StartContainerAsync(_fixture.BaseImage, new BuildContainerOptions { KeepOnDispose = true }, TestContext.Current.CancellationToken);
        await leftover.DisposeAsync();
        var filter = new Dictionary<string, string> { [Prefix] = "1", [Prefix + ".workbench"] = other.InstanceId };

        //Act
        var result = await other.CleanupAsync(new CleanupOptions { RemoveVolumes = true, OnlyThisWorkbench = true }, TestContext.Current.CancellationToken);

        //Assert
        result.Succeeded.Should().BeTrue();
        result.RemovedContainers.Should().Contain(leftover.Id);
        result.RemovedVolumes.Should().Contain(leftover.TargetVolumeName);
        result.RemovedVolumes.Should().NotContain(DockerEngineFixture.TestCacheVolumeName);
        (await Client.Containers.ListAsync(true, filter, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await Client.Volumes.ListAsync(filter, TestContext.Current.CancellationToken)).Should().BeEmpty();
        (await _fixture.SharedContainer.RefreshStateAsync(TestContext.Current.CancellationToken)).Should().BeTrue();
    }

    private async Task RemoveImageIfPresentAsync(string reference)
    {
        try
        {
            await Client.Images.RemoveAsync(reference, force: true, TestContext.Current.CancellationToken);
        }
        catch (DockerImageNotFoundException)
        {
            // Not there: nothing to remove.
        }
    }
}
