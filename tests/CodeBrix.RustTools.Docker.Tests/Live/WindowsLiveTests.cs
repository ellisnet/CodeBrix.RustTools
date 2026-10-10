using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(WindowsCollection.Name)]
public class WindowsLiveTests
{
    private static readonly IReadOnlyDictionary<CrossTarget, ushort> PeMachines = new Dictionary<CrossTarget, ushort>
    {
        [CrossTarget.WindowsX64Gnullvm] = 0x8664,
        [CrossTarget.WindowsArm64Gnullvm] = 0xAA64,
    };

    private readonly WindowsFixture _fixture;

    public WindowsLiveTests(WindowsFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Reads the PE header: the MZ stub, the PE signature and the COFF machine field.</summary>
    private static async Task<ushort> ReadPeMachineAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        bytes.Take(2).Should().Equal((byte)'M', (byte)'Z');
        var peOffset = BitConverter.ToInt32(bytes, 0x3C);
        bytes.Skip(peOffset).Take(4).Should().Equal((byte)'P', (byte)'E', (byte)0, (byte)0);
        return BitConverter.ToUInt16(bytes, peOffset + 4);
    }

    private static async Task<string> AsciiAsync(string path)
        => Encoding.ASCII.GetString(await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

    [Fact]
    public async Task BuildAsync_cross_compiles_sample_13_for_x64_and_arm64_windows_into_the_nuget_layout()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = new List<CrossTarget> { CrossTarget.WindowsX64Gnullvm, CrossTarget.WindowsArm64Gnullvm },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.NuGetRuntimes },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} windows build 13 (x64 + arm64): {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Image.ImageId.Should().Be(_fixture.Image.ImageId);
        result.Results.Keys.Should().Equal(CrossTarget.WindowsX64Gnullvm, CrossTarget.WindowsArm64Gnullvm);
        foreach (var (target, rid) in new[] { (CrossTarget.WindowsX64Gnullvm, "win-x64"), (CrossTarget.WindowsArm64Gnullvm, "win-arm64") })
        {
            var native = Path.Combine(output.Directory, "runtimes", rid, "native");
            var files = result.Retrieval.Artifacts.Where(a => a.Target == target).ToList();
            files.Select(a => a.HostPath).Should().BeEquivalentTo(new[]
            {
                Path.Combine(native, "test_project_13.exe"),
                Path.Combine(native, "test_project_13.dll"),
            });
            files.Single(a => a.HostPath.EndsWith(".exe", StringComparison.Ordinal)).Kind.Should().Be(ArtifactKind.Executable);
            files.Single(a => a.HostPath.EndsWith(".dll", StringComparison.Ordinal)).Kind.Should().Be(ArtifactKind.SharedLibrary);
            foreach (var file in files)
            {
                (await ReadPeMachineAsync(file.HostPath)).Should().Be(PeMachines[target], $"{file.HostPath} must be {CrossTargetInfo.Get(target).Triple}");
                var text = await AsciiAsync(file.HostPath);
                text.Should().NotContain("libunwind.dll", "the unwinder is linked statically");
                text.Should().Contain("api-ms-win-crt-", "the output links against the Universal C Runtime");
            }
        }

        if (OperatingSystem.IsWindows() && RuntimeInformation.OSArchitecture == Architecture.X64)
        {
            var exe = result.Retrieval.Artifacts.Single(a => a.Target == CrossTarget.WindowsX64Gnullvm && a.Kind == ArtifactKind.Executable).HostPath;
            var run = Process.Start(new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true });
            var stdout = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await run.WaitForExitAsync(TestContext.Current.CancellationToken);
            run.ExitCode.Should().Be(0);
            stdout.Should().Contain("cb_add(2, 3) = 5");
        }
    }

    [Fact]
    public void The_image_carries_llvm_mingw_and_both_windows_targets()
    {
        //Assert
        _fixture.SkipIfUnsupported();
        _fixture.Image.Dockerfile.Should().Contain("rustup target add x86_64-pc-windows-gnullvm aarch64-pc-windows-gnullvm");
        _fixture.Image.Dockerfile.Should().Contain(DockerfileGenerator.LlvmMingwVersion);
        _fixture.Image.Dockerfile.Should().NotContain("install -y --no-install-recommends mingw-w64").And.NotContain("mingw32-gcc");
    }
}
