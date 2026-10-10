using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(AndroidCollection.Name)]
public class AndroidLiveTests
{
    private const ushort MachineAArch64 = 0xB7;

    private const ushort MachineX86_64 = 0x3E;

    private readonly AndroidFixture _fixture;

    public AndroidLiveTests(AndroidFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BuildAsync_cross_compiles_sample_13_for_both_android_abis_into_the_nuget_layout()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = new List<CrossTarget> { CrossTarget.AndroidArm64, CrossTarget.AndroidX64 },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.NuGetRuntimes, IncludeExecutables = false },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} android build 13 (arm64 + x64): {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Image.ImageId.Should().Be(_fixture.Image.ImageId);
        result.Results.Keys.Should().Equal(CrossTarget.AndroidArm64, CrossTarget.AndroidX64);
        var arm = Path.Combine(output.Directory, "runtimes", "android-arm64", "native", "libtest_project_13.so");
        var x64 = Path.Combine(output.Directory, "runtimes", "android-x64", "native", "libtest_project_13.so");
        result.Retrieval.Artifacts.Select(a => a.HostPath).Should().Equal(arm, x64);
        result.Retrieval.Artifacts.Should().OnlyContain(a => a.Kind == ArtifactKind.SharedLibrary);
        (await ReadElfMachineAsync(arm)).Should().Be(MachineAArch64);
        (await ReadElfMachineAsync(x64)).Should().Be(MachineX86_64);
    }

    /// <summary>Checks the ELF magic and returns e_machine (bytes 18-19, little-endian).</summary>
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
}
