using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Facade;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Rendering;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// End-to-end Docker-mode builds through the public <see cref="CrateBuilder"/> with an isolated
/// workbench (<see cref="DockerEndToEndFixture"/>: label prefix
/// codebrix.rusttools.cratebuilder.tests.e2e, its own cache volume and container prefix).
/// Skips by name without a usable x86-64 engine.
/// </summary>
[Collection("Docker")]
public class DockerEndToEndTests : IClassFixture<DockerEndToEndFixture>
{
    private readonly DockerEndToEndFixture _docker;

    public DockerEndToEndTests(DockerEndToEndFixture docker)
    {
        _docker = docker;
    }

    [Fact]
    public async Task an_rlib_crate_is_built_with_its_facade_for_linux_and_windows_in_the_rust_image_and_wrapped()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        DotnetCli.SkipIfMissing();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.LinuxX64, CrateTarget.WinX64 },
            OutputDirectory = Path.Combine(root, "out", "native"),
            Wrapper = new WrapperOptions { AssemblyName = "E2E.DockerData", RootNamespace = "E2E.DockerData" },
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        var wrapperBuild = await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build());

        //Assert
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.ToolchainDescription.Should().StartWith("Docker image ");
        var linux = NativeArtifactReader.Read(Path.Combine(request.OutputDirectory, "runtimes", "linux-x64", "native", "libdatacrate.so"));
        var windows = NativeArtifactReader.Read(Path.Combine(request.OutputDirectory, "runtimes", "win-x64", "native", "datacrate.dll"));
        windows.Format.Should().Be(NativeArtifactFormat.Pe);
        windows.Architecture.Should().Be("x86_64");
        foreach (var export in new[] { "datacrate_greet", "datacrate_encoder_new", "datacrate_last_error_message" })
        {
            linux.ExportedSymbols.Should().Contain(export);
            windows.ExportedSymbols.Should().Contain(export);
        }

        File.Exists(Path.Combine(result.Wrapper.ProjectDirectory, "runtimes", "linux-x64", "native", "libdatacrate.so")).Should().BeTrue();
        File.Exists(Path.Combine(result.Wrapper.ProjectDirectory, "runtimes", "win-x64", "native", "datacrate.dll")).Should().BeTrue();
        wrapperBuild.Should().Contain("0 Warning(s)");
        if (WebGpu.WebGpuTestPackage.HostTarget() == CrateTarget.LinuxX64)
        {
            var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath,
                "using System;\nusing E2E.DockerData;\n\nConsole.WriteLine(Datacrate.Greet(\"Docker\"));\n");
            EndToEndKit.Lines(output).Should().Equal("Hello, Docker!");
        }

        EndToEndKit.Log(nameof(an_rlib_crate_is_built_with_its_facade_for_linux_and_windows_in_the_rust_image_and_wrapped), stopwatch);
    }

    [Fact]
    public async Task an_rlib_crate_is_built_for_linux_x64_in_the_manylinux_image_within_its_glibc_floor_SLOW_IMAGE_BUILD()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            LinuxBaseline = LinuxBaseline.ManyLinux,
            Targets = { CrateTarget.LinuxX64 },
            OutputDirectory = Path.Combine(root, "out"),
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        var path = result.Targets.Single().ArtifactPaths.Single();
        var glibc = DefaultArtifactVerifier.HighestGlibcVersion(NativeArtifactReader.Read(path));
        Version.Parse(glibc).Should().BeLessThanOrEqualTo(Version.Parse(ManyLinuxImages.X64GlibcFloor));
        result.Targets.Single().VerificationNotes.Should().Contain(n => n.Contains("within the linux-x64 baseline's 2.28"));
        result.Record.ToolchainDescription.Should().NotContain("platform", "the linux-x64 image keeps the engine's own platform");
        result.Record.Facade.Exports.Should().Contain("datacrate_greet");
        result.Record.ToolchainDescription.Should().Contain(ManyLinuxImages.X64BaseImage);
        EndToEndKit.Log(nameof(an_rlib_crate_is_built_for_linux_x64_in_the_manylinux_image_within_its_glibc_floor_SLOW_IMAGE_BUILD), stopwatch);
    }

    [Fact]
    public async Task readiness_in_docker_mode_names_every_target_for_the_rust_image_and_the_three_linux_targets_for_manylinux()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            OutputDirectory = Path.Combine(sandbox.Directory, "out"),
        };
        foreach (var target in CrateTargets.All)
        {
            request.Targets.Add(target);
        }

        var manyLinux = request.Clone();
        manyLinux.LinuxBaseline = LinuxBaseline.ManyLinux;
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var rustImage = await builder.CheckReadinessAsync(request, TestContext.Current.CancellationToken);
        var manyLinuxReport = await builder.CheckReadinessAsync(manyLinux, TestContext.Current.CancellationToken);

        //Assert
        rustImage.Mode.Should().Be(BuildMode.Docker);
        rustImage.IsReady.Should().BeTrue(rustImage.ToString());
        rustImage.Engine.Architecture.Should().Be("x86_64");
        rustImage.BuildableTargets.Should().BeEquivalentTo(CrateTargets.All);
        rustImage.UnbuildableTargets.Should().BeEmpty();
        manyLinuxReport.BuildableTargets.Should().BeEquivalentTo(new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64 });
        manyLinuxReport.UnbuildableTargets.Should().HaveCount(CrateTargets.All.Count - 3);
        manyLinuxReport.Issues.Where(i => i.Code == "docker.manylinux-emulated").Select(i => i.Target).Should().BeEquivalentTo(new CrateTarget?[] { CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64 });
        EndToEndKit.Log(nameof(readiness_in_docker_mode_names_every_target_for_the_rust_image_and_the_three_linux_targets_for_manylinux), stopwatch,
            "rust image: " + string.Join(" ", rustImage.BuildableTargets.Select(CrateTargets.Rid)) + "; manylinux: " + string.Join(" ", manyLinuxReport.BuildableTargets.Select(CrateTargets.Rid)));
    }

    [Fact]
    public async Task docker_mode_extraction_forced_into_the_container_reads_the_api_and_the_facade_builds()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.LinuxX64 },
            OutputDirectory = Path.Combine(root, "out"),
        };
        var stages = CrateBuilderStages.CreateDefault();
        var extractor = new ApiExtractor(new ApiExtractorOptions { PreferHostToolchain = false, DockerRoute = ApiExtractorDockerRoute.RunInContainer });
        stages.FacadeGenerator = new FacadeGenerator(extractor, new MappingPlanner(), new FacadeRenderer());
        using var builder = new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = EndToEndKit.ScratchRoot, DockerOptions = DockerEndToEndFixture.Options() }, stages);
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();

        //Act
        var route = await extractor.ChooseRouteAsync(request, null, TestContext.Current.CancellationToken);
        var result = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(p => messages.Enqueue(p.Message ?? string.Empty)), TestContext.Current.CancellationToken);

        //Assert
        route.Should().Be(ApiExtractor.ExtractionRoute.InContainer);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        messages.Should().Contain("Copying the staged crate into the container");
        messages.Should().Contain(m => m.StartsWith("Building the API extractor tool in the container", StringComparison.Ordinal));
        messages.Should().NotContain(m => m.Contains("running it inside the container", StringComparison.Ordinal));
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.Facade.Exports.Should().Contain("datacrate_greet");
        var linux = NativeArtifactReader.Read(Path.Combine(request.OutputDirectory, "runtimes", "linux-x64", "native", "libdatacrate.so"));
        linux.ExportedSymbols.Should().Contain("datacrate_encoder_new");
        EndToEndKit.Log(nameof(docker_mode_extraction_forced_into_the_container_reads_the_api_and_the_facade_builds), stopwatch);
    }

    [Fact]
    public async Task a_shared_cargo_target_directory_is_bind_mounted_for_the_build_and_the_extractor_and_handed_back_to_its_owner()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var shared = Path.Combine(root, "shared-target");
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.LinuxX64 },
            OutputDirectory = Path.Combine(root, "out"),
        };
        var stages = CrateBuilderStages.CreateDefault();
        var extractor = new ApiExtractor(new ApiExtractorOptions { PreferHostToolchain = false, DockerRoute = ApiExtractorDockerRoute.RunInContainer });
        stages.FacadeGenerator = new FacadeGenerator(extractor, new MappingPlanner(), new FacadeRenderer());
        using var builder = new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = EndToEndKit.ScratchRoot, DockerOptions = DockerEndToEndFixture.Options(), CargoTargetDirectory = shared }, stages);
        var hash = ApiExtractorToolSource.ContentHash.Substring(0, ApiExtractorToolSource.FolderHashLength);

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        var builtInShared = Directory.Exists(Path.Combine(shared, "docker"))
            ? Directory.GetFiles(Path.Combine(shared, "docker"), "libdatacrate.so", SearchOption.AllDirectories)
            : Array.Empty<string>();
        var toolInShared = File.Exists(Path.Combine(shared, "api-extractor-docker", hash, "release", ApiExtractorToolSource.ToolName));
        Exception deleteError = null;
        try
        {
            Directory.Delete(shared, true);
        }
        catch (Exception ex)
        {
            deleteError = ex;
        }

        //Assert
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        result.Warnings.Should().NotContain(w => w.Contains("shared cargo target folder", StringComparison.Ordinal));
        builtInShared.Should().NotBeEmpty("the container's cargo output lands in the bind-mounted docker/ folder");
        toolInShared.Should().BeTrue("the in-container extractor build uses api-extractor-docker/<hash>/");
        result.Targets.Single().CargoResult.Artifacts.SelectMany(a => a.Filenames).Should().OnlyContain(f => f.StartsWith("/codebrix-cargo-target/", StringComparison.Ordinal));
        deleteError.Should().BeNull("every file the container wrote was handed back to the folder's owner, so the host user can delete it");
        File.Exists(Path.Combine(request.OutputDirectory, "runtimes", "linux-x64", "native", "libdatacrate.so")).Should().BeTrue();
        EndToEndKit.Log(nameof(a_shared_cargo_target_directory_is_bind_mounted_for_the_build_and_the_extractor_and_handed_back_to_its_owner), stopwatch);
    }

    [Fact]
    public async Task a_webgpu_linked_cdylib_is_built_for_linux_windows_and_android_in_docker_at_the_webgpu_api_level_and_called()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        DotnetCli.SkipIfMissing();
        var package = WebGpuTestPackage.LocateOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(16);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.LinuxX64, CrateTarget.WinX64, CrateTarget.AndroidArm64 },
            LinkToCodeBrixWebGpu = true,
            OutputDirectory = Path.Combine(root, "out", "native"),
            Wrapper = new WrapperOptions { AssemblyName = "E2E.DockerProbe", RootNamespace = "E2E.DockerProbe" },
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));

        //Assert
        result.Record.WebGpuNativeVersion.Should().Be(package.Version);
        result.Warnings.Should().NotContain(w => w.Contains("does not reference the engine"));
        foreach (var built in result.Targets)
        {
            built.Outcome.Should().Be(CrateTargetOutcome.Compiled, built.ToString());
            built.VerificationNotes.Should().Contain(n => n.StartsWith("WebGPU link: links ", StringComparison.Ordinal), string.Join(" | ", built.VerificationNotes));
        }

        var android = result.Targets.Single(t => t.Target == CrateTarget.AndroidArm64);
        android.VerificationNotes.Should().Contain("libwgpu_link_probe.so: " + DefaultArtifactVerifier.AndroidApiLevelNote + " 33 (it loads on that Android version and later).");
        NativeArtifactReader.Read(android.ArtifactPaths.Single()).AndroidApiLevel.Should().Be(33);
        if (WebGpuTestPackage.HostTarget() == CrateTarget.LinuxX64)
        {
            var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath,
                "using System;\nusing E2E.DockerProbe;\n\nConsole.WriteLine(\"api=\" + WgpuLinkProbe.ProbeApiVersion());\nConsole.WriteLine(\"built=\" + WgpuLinkProbe.ProbeBuiltApiVersion());\n");
            EndToEndKit.Lines(output).Should().Equal("api=2", "built=2");
        }

        EndToEndKit.Log(nameof(a_webgpu_linked_cdylib_is_built_for_linux_windows_and_android_in_docker_at_the_webgpu_api_level_and_called), stopwatch);
    }

    [Fact]
    public async Task a_cdylib_built_for_android_in_docker_without_webgpu_is_linked_at_the_default_api_level()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            Targets = { CrateTarget.AndroidArm64, CrateTarget.AndroidX64 },
            OutputDirectory = Path.Combine(root, "out"),
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        foreach (var built in result.Targets)
        {
            var info = NativeArtifactReader.Read(built.ArtifactPaths.Single());
            info.AndroidApiLevel.Should().Be(24);
            info.ExportedSymbols.Should().Contain("cb_add");
            built.VerificationNotes.Should().Contain("libtest_project_13.so: " + DefaultArtifactVerifier.AndroidApiLevelNote + " 24 (it loads on that Android version and later).");
        }

        EndToEndKit.Log(nameof(a_cdylib_built_for_android_in_docker_without_webgpu_is_linked_at_the_default_api_level), stopwatch);
    }

    [Fact]
    public async Task a_cdylib_is_built_for_linux_arm64_in_the_manylinux_image_under_qemu_and_wrapped_SLOW_EMULATED()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        DockerEndToEndFixture.SkipUnlessBinfmtHandler("qemu-aarch64");
        DotnetCli.SkipIfMissing();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            LinuxBaseline = LinuxBaseline.ManyLinux,
            Targets = { CrateTarget.LinuxArm64 },
            OutputDirectory = Path.Combine(root, "out", "native"),
            Wrapper = new WrapperOptions { AssemblyName = "E2E.ManyLinuxArm64", RootNamespace = "E2E.ManyLinuxArm64" },
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());
        var messages = new System.Collections.Concurrent.ConcurrentQueue<string>();

        //Act
        var result = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(p => messages.Enqueue(p.Message ?? string.Empty)), TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        var wrapperBuild = await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build());

        //Assert
        var built = result.Targets.Single();
        built.Outcome.Should().Be(CrateTargetOutcome.Compiled, built.ToString());
        built.Provenance.Should().Be(ArtifactProvenance.Distributable);
        var path = built.ArtifactPaths.Single();
        path.Should().Be(Path.Combine(request.OutputDirectory, "runtimes", "linux-arm64", "native", "libtest_project_13.so"));
        var info = NativeArtifactReader.Read(path);
        info.Format.Should().Be(NativeArtifactFormat.Elf);
        info.Architecture.Should().Be("aarch64");
        info.ExportedSymbols.Should().Contain("cb_add");
        built.CargoResult.Artifacts.SelectMany(a => a.Filenames).Should().Contain(f => f.EndsWith("/release/libtest_project_13.so", StringComparison.Ordinal));
        built.CargoResult.Artifacts.SelectMany(a => a.Filenames).Should().NotContain(f => f.Contains("aarch64-unknown-linux-gnu", StringComparison.Ordinal), "the arm64 container builds its own architecture natively, without --target");
        Version.Parse(DefaultArtifactVerifier.HighestGlibcVersion(info)).Should().BeLessThanOrEqualTo(Version.Parse(ManyLinuxImages.Arm64GlibcFloor));
        built.VerificationNotes.Should().Contain(n => n.Contains("within the linux-arm64 baseline's 2.28"), string.Join(" | ", built.VerificationNotes));
        result.Record.ToolchainDescription.Should().Contain(ManyLinuxImages.Arm64BaseImage);
        result.Record.ToolchainDescription.Should().Contain("platform linux/arm64 under emulation");
        messages.Should().Contain(m => m.Contains("linux/arm64 containers run on this engine", StringComparison.Ordinal));
        var fileOutput = await FileCommandAsync(path);
        if (fileOutput != null)
        {
            fileOutput.Should().Contain("ELF 64-bit LSB shared object");
            fileOutput.Should().Contain("ARM aarch64");
        }

        var wrapped = Directory.GetFiles(Path.Combine(result.Wrapper.ProjectDirectory, "runtimes", "linux-arm64", "native"), "*.so");
        wrapped.Should().ContainSingle();
        NativeArtifactReader.Read(wrapped.Single()).Architecture.Should().Be("aarch64");
        wrapperBuild.Should().Contain("0 Warning(s)");
        EndToEndKit.Log(nameof(a_cdylib_is_built_for_linux_arm64_in_the_manylinux_image_under_qemu_and_wrapped_SLOW_EMULATED), stopwatch, fileOutput);
    }

    [Fact]
    public async Task a_cdylib_is_built_for_linux_riscv64_in_the_manylinux_image_under_qemu_within_its_2_39_floor_SLOW_EMULATED()
    {
        //Arrange
        _docker.SkipUnlessX64Engine();
        DockerEndToEndFixture.SkipUnlessBinfmtHandler("qemu-riscv64");
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "Cargo.toml")),
            Mode = BuildMode.Docker,
            LinuxBaseline = LinuxBaseline.ManyLinux,
            Targets = { CrateTarget.LinuxRiscV64 },
            OutputDirectory = Path.Combine(root, "out"),
        };
        using var builder = EndToEndKit.NewBuilder(DockerEndToEndFixture.Options());

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        var built = result.Targets.Single();
        built.Provenance.Should().Be(ArtifactProvenance.Distributable);
        var info = NativeArtifactReader.Read(built.ArtifactPaths.Single());
        info.Architecture.Should().Be("riscv64");
        info.ExportedSymbols.Should().Contain("cb_add");
        Version.Parse(DefaultArtifactVerifier.HighestGlibcVersion(info)).Should().BeLessThanOrEqualTo(Version.Parse(ManyLinuxImages.RiscV64GlibcFloor));
        built.VerificationNotes.Should().Contain(n => n.Contains("within the linux-riscv64 baseline's 2.39"), string.Join(" | ", built.VerificationNotes));
        result.Record.ToolchainDescription.Should().Contain(ManyLinuxImages.RiscV64BaseImage);
        var fileOutput = await FileCommandAsync(built.ArtifactPaths.Single());
        if (fileOutput != null)
        {
            fileOutput.Should().Contain("UCB RISC-V");
        }

        EndToEndKit.Log(nameof(a_cdylib_is_built_for_linux_riscv64_in_the_manylinux_image_under_qemu_within_its_2_39_floor_SLOW_EMULATED), stopwatch, fileOutput);
    }

    /// <summary>The output of the host's <c>file</c> command for a path, or <c>null</c> when
    /// the command is not on PATH (the check is a sanity cross-check of the ELF reader).</summary>
    private static async Task<string> FileCommandAsync(string path)
    {
        var start = new ProcessStartInfo("file") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        start.ArgumentList.Add("-b");
        start.ArgumentList.Add(path);
        try
        {
            using var process = Process.Start(start);
            var output = await process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return process.ExitCode == 0 ? output.Trim() : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
