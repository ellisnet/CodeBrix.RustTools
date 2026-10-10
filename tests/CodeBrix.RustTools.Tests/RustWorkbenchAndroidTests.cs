using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

public class RustWorkbenchAndroidTests
{
    private static async Task<RustToolchainInfo> AndroidReadyToolchainAsync(RustWorkbench workbench)
    {
        CargoAvailability.SkipIfMissing();
        var toolchain = await workbench.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (toolchain.AndroidNdk == null)
        {
            Assert.Skip("No Android NDK was found (set ANDROID_NDK_HOME or ANDROID_HOME): " + (AndroidNdkLocator.HostSupportMessage ?? "none in the environment."));
        }

        var missing = AndroidLinkProfile.Triples.Where(t => !toolchain.InstalledTargets.Contains(t)).ToList();
        if (missing.Count > 0)
        {
            Assert.Skip("Android targets are not installed: rustup target add " + string.Join(" ", missing));
        }

        return toolchain;
    }

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
    public async Task BuildAndroidAsync_builds_sample_13_for_both_abis_into_jniLibs()
    {
        //Arrange
        var workbench = new RustWorkbench();
        var toolchain = await AndroidReadyToolchainAsync(workbench);
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();

        //Act
        var result = await workbench.BuildAndroidAsync(
            workbench.OpenProject(sandbox.Directory), output.Directory,
            new CargoBuildOptions { Profile = CargoProfile.Release }, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Builds.Keys.Should().Equal("aarch64-linux-android", "x86_64-linux-android");
        result.Files.Select(f => Path.GetRelativePath(output.Directory, f.Path).Replace('\\', '/')).Should().Equal(
            "jniLibs/arm64-v8a/libtest_project_13.so",
            "jniLibs/x86_64/libtest_project_13.so");
        result.Files.Should().OnlyContain(f => !f.IsExecutable);
        (await ReadElfMachineAsync(result.Files[0].Path)).Should().Be(0xB7, "arm64-v8a is AArch64");
        (await ReadElfMachineAsync(result.Files[1].Path)).Should().Be(0x3E, "x86_64 is x86-64");
        toolchain.AndroidNdk.ApiLevels.Should().Contain(AndroidLinkProfile.DefaultApiLevel);
    }

    [Fact]
    public async Task BuildAndroidAsync_lays_out_the_nuget_runtimes_folders_with_programs_when_asked()
    {
        //Arrange
        var workbench = new RustWorkbench();
        await AndroidReadyToolchainAsync(workbench);
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var androidOptions = new AndroidBuildOptions { Layout = AndroidOutputLayout.NuGetRuntimes, IncludeExecutables = true };
        androidOptions.Triples.Clear();
        androidOptions.Triples.Add("aarch64-linux-android");

        //Act
        var result = await workbench.BuildAndroidAsync(workbench.OpenProject(sandbox.Directory), output.Directory, null, androidOptions, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Builds.Keys.Should().Equal("aarch64-linux-android");
        result.Files.Select(f => Path.GetRelativePath(output.Directory, f.Path).Replace('\\', '/')).Should().BeEquivalentTo(new[]
        {
            "runtimes/android-arm64/native/libtest_project_13.so",
            "runtimes/android-arm64/native/test_project_13",
        });
        result.Files.Single(f => f.IsExecutable).FileName.Should().Be("test_project_13");
        result.Files.Should().OnlyContain(f => f.Abi == "arm64-v8a" && f.RuntimeIdentifier == "android-arm64");
    }

    [Fact]
    public async Task BuildAndroidAsync_throws_with_ndk_guidance_when_the_ndk_path_is_wrong()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(13);
        using var bogus = SampleSandbox.CreateEmpty();
        using var output = SampleSandbox.CreateEmpty();
        var workbench = new RustWorkbench(new RustWorkbenchOptions { AndroidNdkPath = bogus.Directory });

        //Act
        var act = () => workbench.BuildAndroidAsync(workbench.OpenProject(sandbox.Directory), output.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<ToolchainNotFoundException>();
        thrown.Which.Readiness.AndroidNdkMissing.Should().BeTrue();
        thrown.Which.Guidance.Should().Contain(g => g.Topic == InstallGuidanceTopic.InstallAndroidNdk && g.Commands.Any(c => c.Contains("sdkmanager")));
        (await workbench.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken)).Warnings.Should().Contain(w => w.Contains("Android NDK"));
    }

    [Fact]
    public async Task BuildAndroidAsync_rejects_a_non_android_triple()
    {
        //Arrange
        var workbench = new RustWorkbench();
        using var sandbox = SampleSandbox.FromSample(13);
        var androidOptions = new AndroidBuildOptions();
        androidOptions.Triples.Add("aarch64-apple-darwin");

        //Act
        var act = () => workbench.BuildAndroidAsync(workbench.OpenProject(sandbox.Directory), "/out", null, androidOptions, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*aarch64-apple-darwin*");
    }
}
