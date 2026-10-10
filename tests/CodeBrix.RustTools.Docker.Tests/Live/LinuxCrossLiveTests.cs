using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(LinuxCrossCollection.Name)]
public class LinuxCrossLiveTests
{
    private static readonly IReadOnlyDictionary<CrossTarget, ushort> ElfMachines = new Dictionary<CrossTarget, ushort>
    {
        [CrossTarget.LinuxX64] = 0x3E,
        [CrossTarget.LinuxArm64] = 0xB7,
        [CrossTarget.LinuxRiscV64] = 0xF3,
    };

    private readonly LinuxCrossFixture _fixture;

    public LinuxCrossLiveTests(LinuxCrossFixture fixture)
    {
        _fixture = fixture;
    }

    private IReadOnlyList<CrossTarget> ForeignTargets
        => CrossTargetInfo.All.Where(i => i.IsLinux && !i.IsNativeOn(_fixture.Engine)).Select(i => i.Target).ToList();

    private static async Task<ushort> ReadElfMachineAsync(string path)
    {
        var header = new byte[20];
        await using (var stream = File.OpenRead(path))
        {
            await stream.ReadExactlyAsync(header, TestContext.Current.CancellationToken);
        }

        header.Take(4).Should().Equal((byte)0x7F, (byte)'E', (byte)'L', (byte)'F');
        return BitConverter.ToUInt16(header, 18);
    }

    [Fact]
    public void The_image_carries_a_cross_layer_for_every_linux_architecture_but_the_engine_s_own()
    {
        //Assert
        _fixture.SkipIfUnsupported();
        ForeignTargets.Should().HaveCount(2);
        _fixture.Image.Spec.CrossTargets.Should().BeEquivalentTo(ForeignTargets);
        _fixture.Image.Spec.CrossTargets.Should().NotContain(_fixture.NativeTarget());
        foreach (var target in ForeignTargets)
        {
            _fixture.Image.Dockerfile.Should().Contain("rustup target add " + CrossTargetInfo.Get(target).Triple);
        }
    }

    [Fact]
    public async Task BuildAsync_cross_compiles_sample_13_for_every_foreign_linux_architecture_into_the_nuget_layout()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = ForeignTargets.ToList(),
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.NuGetRuntimes },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} linux-cross build 13 ({string.Join(" + ", ForeignTargets)}): {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Image.ImageId.Should().Be(_fixture.Image.ImageId);
        result.Results.Keys.Should().Equal(ForeignTargets);
        foreach (var target in ForeignTargets)
        {
            var info = CrossTargetInfo.Get(target);
            var native = Path.Combine(output.Directory, "runtimes", info.RuntimeIdentifier, "native");
            var files = result.Retrieval.Artifacts.Where(a => a.Target == target).ToList();
            files.Select(a => a.HostPath).Should().BeEquivalentTo(new[]
            {
                Path.Combine(native, "libtest_project_13.so"),
                Path.Combine(native, "test_project_13"),
            });
            files.Single(a => a.Kind == ArtifactKind.Executable).IsExecutable.Should().BeTrue();
            foreach (var file in files)
            {
                (await ReadElfMachineAsync(file.HostPath)).Should().Be(ElfMachines[target], $"{file.HostPath} must be {info.Triple}");
            }

            result.Results[target].CommandLine.Should().Contain("--target " + info.Triple);
        }

        Directory.Exists(Path.Combine(output.Directory, "runtimes", CrossTargetInfo.Get(_fixture.NativeTarget()).RuntimeIdentifier)).Should().BeFalse("the engine's own target was not requested");
    }

    [Fact]
    public async Task RunCargoAsync_builds_the_native_target_in_the_cross_image_without_a_target_flag()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        await using var container = await _fixture.Workbench.StartContainerAsync(_fixture.Image, null, TestContext.Current.CancellationToken);
        await _fixture.Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        var native = await _fixture.Workbench.RunCargoAsync(container, CargoCommand.Build, null, CrossTarget.LinuxNative, null, TestContext.Current.CancellationToken);
        var foreign = await _fixture.Workbench.RunCargoAsync(container, CargoCommand.Build, null, ForeignTargets[0], null, TestContext.Current.CancellationToken);

        //Assert
        native.Success.Should().BeTrue(native.StandardError);
        native.CommandLine.Should().NotContain("--target");
        native.RunnableArtifacts.Single().Executable.Should().Be("/work/target/debug/test_project_13");
        foreign.Success.Should().BeTrue(foreign.StandardError);
        foreign.RunnableArtifacts.Single().Executable.Should().Be("/work/target/" + CrossTargetInfo.Get(ForeignTargets[0]).Triple + "/debug/test_project_13");
    }
}
