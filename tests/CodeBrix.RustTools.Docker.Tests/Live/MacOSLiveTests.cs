using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(MacOSCollection.Name)]
public class MacOSLiveTests
{
    private readonly MacOSFixture _fixture;

    public MacOSLiveTests(MacOSFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task BuildAsync_cross_compiles_sample_13_for_both_macos_targets_into_the_nuget_layout()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = new List<CrossTarget> { CrossTarget.MacOSArm64, CrossTarget.MacOSX64 },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.NuGetRuntimes, IncludeExecutables = false },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} macos build 13 (arm64 + x64): {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Image.ImageId.Should().Be(_fixture.Image.ImageId);
        result.Results.Keys.Should().Equal(CrossTarget.MacOSArm64, CrossTarget.MacOSX64);
        var arm64 = Path.Combine(output.Directory, "runtimes", "osx-arm64", "native", "libtest_project_13.dylib");
        var x64 = Path.Combine(output.Directory, "runtimes", "osx-x64", "native", "libtest_project_13.dylib");
        result.Retrieval.Artifacts.Select(a => a.HostPath).Should().Equal(arm64, x64);
        result.Retrieval.Artifacts.Should().OnlyContain(a => a.Kind == ArtifactKind.SharedLibrary);
        MacOSUniversalBinary.ReadSlices(arm64).Should().ContainSingle().Which.Architecture.Should().Be("arm64");
        MacOSUniversalBinary.ReadSlices(x64).Should().ContainSingle().Which.Architecture.Should().Be("x86_64");
    }

    [Fact]
    public async Task BuildAsync_joins_the_two_macos_slices_into_universal_files()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = new List<CrossTarget> { CrossTarget.MacOSUniversal },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.NuGetRuntimes },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} macos universal build 13: {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Results.Keys.Should().Equal(CrossTarget.MacOSArm64, CrossTarget.MacOSX64);
        var native = Path.Combine(output.Directory, "runtimes", "osx", "native");
        result.Retrieval.Artifacts.Select(a => a.HostPath).Should().BeEquivalentTo(new[]
        {
            Path.Combine(native, "libtest_project_13.dylib"),
            Path.Combine(native, "test_project_13"),
        });
        result.Retrieval.Artifacts.Should().OnlyContain(a => a.Target == CrossTarget.MacOSUniversal);
        Directory.Exists(Path.Combine(output.Directory, "runtimes", "osx-arm64")).Should().BeFalse("only the universal files were asked for");
        foreach (var artifact in result.Retrieval.Artifacts)
        {
            MacOSUniversalBinary.ReadSlices(artifact.HostPath).Select(s => s.Architecture).Should().Equal("x86_64", "arm64");
        }

        var program = result.Retrieval.Artifacts.Single(a => a.Kind == ArtifactKind.Executable);
        program.IsExecutable.Should().BeTrue();
        if (OperatingSystem.IsMacOS())
        {
            var run = Process.Start(new ProcessStartInfo(program.HostPath) { RedirectStandardOutput = true, RedirectStandardError = true });
            var stdout = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await run.WaitForExitAsync(TestContext.Current.CancellationToken);
            run.ExitCode.Should().Be(0);
            stdout.Should().Contain("cb_add(2, 3) = 5");
        }
    }

    [Fact]
    public async Task BuildAsync_links_a_cdylib_that_uses_float_math_for_both_macos_targets()
    {
        //Arrange: float methods are inlined into the crate and lowered by LLVM to C library calls (pow, log,
        //__sincos_stret, fmod) that no std rlib imports; the stub must name them or the link fails with
        //"undefined symbol: _pow".
        _fixture.SkipIfUnsupported();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[package]\nname = \"float_math_probe\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[lib]\ncrate-type = [\"cdylib\"]\n\n[workspace]\n");
        sandbox.WriteFile("src/lib.rs",
            "#[no_mangle]\npub extern \"C\" fn cb_math(x: f64, y: f64) -> f64 {\n"
            + "    let (s, c) = x.sin_cos();\n"
            + "    x.powf(y) + x.ln() + s * c + (x % y) + 10f64.powf(y) + ((x as f32).exp() as f64).cbrt()\n}\n");
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Targets = new List<CrossTarget> { CrossTarget.MacOSArm64, CrossTarget.MacOSX64 },
            Options = new CargoBuildOptions { Profile = CargoProfile.Release },
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.ByTarget },
        };

        //Act
        var result = await _fixture.Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} macos float-math cdylib (arm64 + x64): {result.Duration}");

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        var arm64 = Path.Combine(output.Directory, "aarch64-apple-darwin", "libfloat_math_probe.dylib");
        var x64 = Path.Combine(output.Directory, "x86_64-apple-darwin", "libfloat_math_probe.dylib");
        result.Retrieval.Artifacts.Select(a => a.HostPath).Should().Equal(arm64, x64);
        MacOSUniversalBinary.ReadSlices(arm64).Should().ContainSingle().Which.Architecture.Should().Be("arm64");
        MacOSUniversalBinary.ReadSlices(x64).Should().ContainSingle().Which.Architecture.Should().Be("x86_64");
    }

    [Fact]
    public async Task Container_and_host_builds_of_sample_13_are_byte_for_byte_identical_on_a_mac()
    {
        //Arrange
        _fixture.SkipIfUnsupported();
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Skip("The host-side half of this test needs a Mac.");
        }

        var host = new RustWorkbench();
        var toolchain = await host.DetectToolchainAsync(cancellationToken: TestContext.Current.CancellationToken);
        if (!toolchain.HasCargoAndRustc)
        {
            Assert.Skip("cargo and rustc are not installed on this Mac.");
        }

        CrossTargetInfo.TryFromTriple(toolchain.HostTriple, out var hostTarget).Should().BeTrue(toolchain.HostTriple);
        using var containerSandbox = SampleSandbox.FromSample(13);
        using var hostSandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        var installName = MacOSLinkProfile.DefaultInstallName(host.OpenProject(hostSandbox.Directory));

        //Act: the container half
        await using var container = await _fixture.Workbench.StartContainerAsync(_fixture.Image, null, TestContext.Current.CancellationToken);
        await _fixture.Workbench.CopySourceAsync(container, containerSandbox.Directory, null, null, TestContext.Current.CancellationToken);
        var containerBuild = await _fixture.Workbench.RunCargoAsync(
            container, CargoCommand.Build, new CargoBuildOptions { Profile = CargoProfile.Release, MacOSInstallName = installName }, hostTarget.Target, null, TestContext.Current.CancellationToken);
        containerBuild.Success.Should().BeTrue(containerBuild.StandardError);
        var retrieved = await _fixture.Workbench.RetrieveArtifactsAsync(
            container, containerBuild.Artifacts, new ArtifactRetrievalOptions { HostDirectory = Path.Combine(output.Directory, "container") }, hostTarget.Target, TestContext.Current.CancellationToken);
        var sysroot = await _fixture.Workbench.ExportMacOSSysrootAsync(container, Path.Combine(output.Directory, "sysroot"), TestContext.Current.CancellationToken);

        //Act: the host half, with the same linker, stub and flags
        var profile = MacOSLinkProfile.ForToolchain(toolchain, sysroot);
        var options = new CargoBuildOptions { Profile = CargoProfile.Release };
        profile.ApplyTo(options, hostTarget.Triple, installName);
        var hostBuild = await host.BuildAsync(host.OpenProject(hostSandbox.Directory), options, null, TestContext.Current.CancellationToken);
        DockerEngineFixture.Log($"{DateTime.Now:O} macos identity 13 ({hostTarget.Triple}): container {containerBuild.Duration}, host {hostBuild.Duration}");

        //Assert
        hostBuild.Success.Should().BeTrue(hostBuild.StandardError);
        File.Exists(Path.Combine(sysroot, "usr", "lib", "libSystem.tbd")).Should().BeTrue();
        File.Exists(Path.Combine(sysroot, "usr", "lib", "libc.tbd")).Should().BeTrue();
        File.ReadAllText(Path.Combine(sysroot, "usr", "lib", "libSystem.tbd")).Should().Contain(" _pow,", "the exported stub names the C math functions");
        var hostProgram = hostBuild.RunnableArtifacts.Single().Executable;
        var hostLibrary = hostBuild.Artifacts.SelectMany(a => a.Filenames).Single(f => f.EndsWith(".dylib", StringComparison.Ordinal));
        var containerProgram = retrieved.Artifacts.Single(a => a.Kind == ArtifactKind.Executable).HostPath;
        var containerLibrary = retrieved.Artifacts.Single(a => a.Kind == ArtifactKind.SharedLibrary).HostPath;
        Sha256(containerProgram).Should().Be(Sha256(hostProgram), "the program built in the container and on this Mac must be identical");
        Sha256(containerLibrary).Should().Be(Sha256(hostLibrary), "the library built in the container and on this Mac must be identical");

        var run = Process.Start(new ProcessStartInfo(containerProgram) { RedirectStandardOutput = true, RedirectStandardError = true });
        var stdout = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        await run.WaitForExitAsync(TestContext.Current.CancellationToken);
        run.ExitCode.Should().Be(0);
        stdout.Should().Contain("cb_add(2, 3) = 5");
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
