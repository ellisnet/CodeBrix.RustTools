using System;
using System.IO;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

/// <summary>
/// Foreign-platform images and containers (linux/arm64 under qemu-user on an x86-64 engine)
/// and the platform probe. Each test skips by name when the engine is arm64 itself (nothing
/// is foreign then) or the host has no qemu-aarch64 binfmt handler. No Rust image is built
/// here: rustup under emulation is slow, and CrateBuilder's ManyLinux tests cover the real
/// Rust build.
/// </summary>
[Collection(DockerEngineCollection.Name)]
public class ForeignPlatformLiveTests
{
    private const string Arm64BinfmtHandler = "/proc/sys/fs/binfmt_misc/qemu-aarch64";

    private readonly DockerEngineFixture _fixture;

    public ForeignPlatformLiveTests(DockerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    private DockerRustWorkbench Workbench => _fixture.Workbench;

    private void SkipUnlessArm64IsForeignAndEmulated()
    {
        if (_fixture.Engine.IsNativePlatform(DockerPlatforms.LinuxArm64))
        {
            Assert.Skip("The engine is arm64 itself, so linux/arm64 is not a foreign platform here.");
        }

        if (!File.Exists(Arm64BinfmtHandler))
        {
            Assert.Skip($"No qemu-user binfmt handler for aarch64 ({Arm64BinfmtHandler} is absent), so linux/arm64 containers cannot run on this engine.");
        }
    }

    [Fact]
    public async Task a_containerfile_image_built_for_linux_arm64_is_an_arm64_image_and_its_container_runs_aarch64()
    {
        //Arrange
        SkipUnlessArm64IsForeignAndEmulated();
        using var sandbox = SampleSandbox.CreateEmpty();
        var marker = Guid.NewGuid().ToString("N");
        var containerfile = sandbox.WriteFile("Containerfile", "FROM alpine:latest\nRUN uname -m > /opt-machine.txt\nRUN echo " + marker + " > /opt-marker.txt\n");
        var spec = new BuildImageSpec { Containerfile = containerfile, Platform = DockerPlatforms.LinuxArm64 };
        var hostSpec = spec.Clone();
        hostSpec.Platform = null;

        //Act
        var image = await Workbench.EnsureImageAsync(spec, null, TestContext.Current.CancellationToken);
        var again = await Workbench.EnsureImageAsync(spec, null, TestContext.Current.CancellationToken);
        var inspect = await Workbench.Client.Images.InspectAsync(image.Reference, TestContext.Current.CancellationToken);
        await using var container = await Workbench.StartContainerAsync(image, null, TestContext.Current.CancellationToken);
        var machine = await Workbench.ExecAsync(container, new[] { "uname", "-m" }, TestContext.Current.CancellationToken);
        var built = await Workbench.ExecAsync(container, new[] { "cat", "/opt-machine.txt", "/opt-marker.txt" }, TestContext.Current.CancellationToken);

        //Assert
        image.Platform.Should().Be(DockerPlatforms.LinuxArm64);
        image.WasContainerfileImageBuilt.Should().BeTrue();
        image.Tag.Should().NotBe(ContainerfileImage.TagFor(hostSpec), "the platform is part of the image identity");
        again.WasReused.Should().BeTrue();
        again.ImageId.Should().Be(image.ImageId);
        inspect.Architecture.Should().Be("arm64");
        container.Platform.Should().Be(DockerPlatforms.LinuxArm64);
        machine.Trim().Should().Be("aarch64");
        built.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal("aarch64", marker);
    }

    [Fact]
    public async Task the_probe_finds_that_linux_arm64_runs_under_emulation_and_caches_the_answer()
    {
        //Arrange
        SkipUnlessArm64IsForeignAndEmulated();

        //Act
        var first = await Workbench.ProbePlatformAsync(DockerPlatforms.LinuxArm64, TestContext.Current.CancellationToken);
        var second = await Workbench.ProbePlatformAsync("Linux/ARM64", TestContext.Current.CancellationToken);
        var canRun = await Workbench.CanRunPlatformAsync(DockerPlatforms.LinuxArm64, TestContext.Current.CancellationToken);
        var native = await Workbench.ProbePlatformAsync(_fixture.Engine.NativePlatform, TestContext.Current.CancellationToken);
        var leftovers = await Workbench.Client.Containers.ListAsync(all: true, Workbench.Labels.WorkbenchFilter(), TestContext.Current.CancellationToken);

        //Assert
        first.Outcome.Should().Be(PlatformProbeOutcome.Runs, first.ToString());
        first.CanRun.Should().BeTrue();
        first.IsEmulated.Should().BeTrue();
        first.ReportedMachine.Should().Be("aarch64");
        first.ProbeImage.Should().Be(DockerRustWorkbench.PlatformProbeImage);
        first.Guidance.Should().BeNull();
        second.Should().BeSameAs(first);
        canRun.Should().BeTrue();
        native.Outcome.Should().Be(PlatformProbeOutcome.Native);
        leftovers.Should().NotContain(c => c.DisplayName.Contains("platform-probe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_probe_reports_a_platform_the_probe_image_does_not_publish_as_refused()
    {
        //Arrange (every common architecture has a qemu-user handler on the machines this suite
        //runs on, so the "no handler" case cannot be produced live without changing the host;
        //PlatformProbeTests covers its classification. This is the other negative answer.)
        SkipUnlessArm64IsForeignAndEmulated();

        //Act
        var result = await Workbench.ProbePlatformAsync("linux/sparc64", TestContext.Current.CancellationToken);

        //Assert
        result.Outcome.Should().Be(PlatformProbeOutcome.Refused, result.ToString());
        result.CanRun.Should().BeFalse();
        result.Message.Should().Contain("linux/sparc64");
    }
}
