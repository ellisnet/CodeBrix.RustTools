using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Building.ManyLinux;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

[Collection("Docker")]
public class DockerBuildDriverTests : IClassFixture<DockerDriverFixture>
{
    private const int Sample = 13;
    private const string Export = "cb_add";

    private readonly DockerDriverFixture _docker;

    public DockerBuildDriverTests(DockerDriverFixture docker)
    {
        _docker = docker;
    }

    private void SkipUnlessX64Engine()
    {
        _docker.SkipIfUnavailable();
        if (!string.Equals(_docker.Engine.Architecture, "x86_64", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("These Docker tests build linux-x64 natively and need an x86-64 engine; this engine is " + _docker.Engine.Architecture + ".");
        }
    }

    // ---- unit ----

    [Fact]
    public void TargetOptions_gives_a_target_its_own_contribution_and_only_the_contributed_rustflags()
    {
        //Arrange
        CargoBuildOptions Options()
        {
            var options = new CargoBuildOptions();
            options.Environment["SHARED"] = "1";
            return options;
        }

        var windows = new BuildContribution();
        windows.Environment["CODEBRIX_WGPU_DIR"] = "/work/.codebrix-wgpu/win-x64";
        windows.RustFlags.Add("-L");
        windows.RustFlags.Add("native=/work/.codebrix-wgpu/win-x64");
        var linux = new BuildContribution();
        linux.Environment["CODEBRIX_WGPU_DIR"] = "/work/.codebrix-wgpu/linux-x64";

        //Act
        var forWindows = DockerBuildDriver.TargetOptions(Options(), windows, CrateTarget.WinX64);
        var forLinux = DockerBuildDriver.TargetOptions(Options(), linux, CrateTarget.LinuxX64);
        var plain = DockerBuildDriver.TargetOptions(Options(), null, CrateTarget.AndroidArm64);

        //Assert
        forWindows.Environment["CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_RUSTFLAGS"].Should().Be("-L native=/work/.codebrix-wgpu/win-x64");
        forWindows.Environment["CARGO_TARGET_X86_64_PC_WINDOWS_GNULLVM_RUSTFLAGS"].Should().NotContain("crt-static");
        forWindows.Environment["CODEBRIX_WGPU_DIR"].Should().Be("/work/.codebrix-wgpu/win-x64");
        forLinux.Environment["CODEBRIX_WGPU_DIR"].Should().Be("/work/.codebrix-wgpu/linux-x64");
        forLinux.Environment.Keys.Should().NotContain(k => k.EndsWith("_RUSTFLAGS", StringComparison.Ordinal));
        plain.Environment.Should().ContainSingle().Which.Key.Should().Be("SHARED");
    }

    [Fact]
    public void ContainerOptionsFor_links_android_at_the_host_modes_level()
    {
        //Assert
        DockerBuildDriver.ContainerOptionsFor(new CrateBuildRequest()).AndroidApiLevel.Should().Be(24);
        DockerBuildDriver.ContainerOptionsFor(new CrateBuildRequest { LinkToCodeBrixWebGpu = true }).AndroidApiLevel.Should().Be(HostBuildDriver.WebGpuAndroidApiLevel);
        HostBuildDriver.WebGpuAndroidApiLevel.Should().Be(33);
    }

    [Fact]
    public void CopyRootFor_is_the_staged_root_when_it_is_the_crate_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(Sample);
        var workspace = new StagedWorkspace { Directory = sandbox.Directory, ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml") };
        var crate = BuiltCrate.From(workspace);

        //Act
        var root = DockerBuildDriver.CopyRootFor(workspace, crate);

        //Assert
        root.Should().Be(sandbox.Directory);
        DockerBuildDriver.ContainerWorkingDirectoryFor(root, crate.ManifestPath).Should().BeNull();
    }

    [Fact]
    public void CopyRootFor_is_the_whole_staging_root_when_the_built_manifest_lies_below_it()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(Sample);
        var stagingRoot = Path.GetDirectoryName(sandbox.Directory);
        var workspace = new StagedWorkspace { Directory = stagingRoot, ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml") };
        var crate = BuiltCrate.From(workspace);

        //Act
        var root = DockerBuildDriver.CopyRootFor(workspace, crate);

        //Assert
        root.Should().Be(stagingRoot);
        DockerBuildDriver.ContainerWorkingDirectoryFor(root, crate.ManifestPath).Should().Be("/work/" + Path.GetFileName(sandbox.Directory));
    }

    [Fact]
    public void ContainerWorkingDirectoryFor_keeps_the_dot_of_crate_builders_hidden_folders()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var manifest = sandbox.WriteFile(".codebrix-facade/demo_facade/Cargo.toml", "[package]\nname = \"demo_facade\"\n");

        //Act
        var directory = DockerBuildDriver.ContainerWorkingDirectoryFor(sandbox.Directory, manifest);

        //Assert
        directory.Should().Be("/work/.codebrix-facade/demo_facade");
    }

    [Fact]
    public void CopyRootFor_falls_back_to_the_crate_folder_when_the_manifest_is_outside_the_staging_root()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(Sample);
        using var other = SampleSandbox.CreateEmpty();
        var workspace = new StagedWorkspace { Directory = other.Directory, ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml") };

        //Act
        var root = DockerBuildDriver.CopyRootFor(workspace, BuiltCrate.From(workspace));

        //Assert
        root.Should().Be(sandbox.Directory);
    }

    [Fact]
    public void the_workspace_token_is_the_container_folder_in_docker_mode_and_the_staging_root_in_host_mode()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(Sample);
        var workspace = new StagedWorkspace { Directory = sandbox.Directory, ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml") };
        var part = new BuildContribution();
        part.Environment["LINK_DIR"] = BuildContribution.WorkspaceToken + "/.codebrix-wgpu/linux-x64";
        part.RustFlags.Add("-L native=" + BuildContribution.WorkspaceToken + "/.codebrix-wgpu/linux-x64");
        var docker = new BuildContribution();
        var host = new BuildContribution();

        //Act
        ContributionMerger.Merge(docker, part, DockerBuildDriver.WorkspaceTokenValue);
        ContributionMerger.Merge(host, part, HostBuildDriver.WorkspaceTokenValue(workspace, BuiltCrate.From(workspace)));

        //Assert
        docker.Environment["LINK_DIR"].Should().Be("/work/.codebrix-wgpu/linux-x64");
        docker.RustFlags.Should().Equal("-L native=/work/.codebrix-wgpu/linux-x64");
        host.Environment["LINK_DIR"].Should().Be(sandbox.Directory + "/.codebrix-wgpu/linux-x64");
    }

    // ---- live (Docker engine) ----

    [Fact]
    public async Task BuildAsync_builds_linux_x64_and_windows_x64_in_one_rust_image_request()
    {
        //Arrange
        SkipUnlessX64Engine();
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_linux_x64_and_windows_x64_in_one_rust_image_request), Sample, dockerWorkbench: _docker.Workbench);
        var request = harness.Request(BuildMode.Docker, CrateTarget.LinuxX64, CrateTarget.WinX64);
        harness.Context.Readiness = TargetMatrixReadiness(request);

        //Act
        var results = await new DockerBuildDriver().BuildAsync(harness.Workspace, request, harness.Context, harness.Context.Progress, TestContext.Current.CancellationToken);

        //Assert
        results.Select(r => r.Target).Should().Equal(CrateTarget.LinuxX64, CrateTarget.WinX64);
        results.Should().OnlyContain(r => r.Outcome == CrateTargetOutcome.Compiled, string.Join(" / ", results.Select(r => r.ToString())));
        var linux = Path.Combine(harness.OutputDirectory, "runtimes", "linux-x64", "native", "libtest_project_13.so");
        var windows = Path.Combine(harness.OutputDirectory, "runtimes", "win-x64", "native", "test_project_13.dll");
        results[0].ArtifactPaths.Should().Equal(linux);
        results[1].ArtifactPaths.Should().Equal(windows);
        NativeArtifactReader.Read(linux).ExportedSymbols.Should().Contain(Export);
        var pe = NativeArtifactReader.Read(windows);
        pe.Architecture.Should().Be("x86_64");
        pe.ExportedSymbols.Should().Contain(Export);
        pe.NeededLibraries.Should().NotContain(n => n.Contains("unwind", StringComparison.OrdinalIgnoreCase));
        results.Should().OnlyContain(r => r.Provenance == ArtifactProvenance.Distributable);
        results.Should().OnlyContain(r => r.VerificationNotes.Any(n => n.StartsWith("Built in Docker image", StringComparison.Ordinal)));
        harness.Context.ToolchainDescription.Should().StartWith("Docker image ");
        harness.Context.ToolchainDescription.Should().Contain("base rust:latest");
        Directory.GetFiles(harness.OutputDirectory, "*", SearchOption.AllDirectories).Should().HaveCount(2);
    }

    [Fact]
    public async Task BuildAsync_builds_linux_x64_in_the_manylinux_image_within_its_glibc_floor_SLOW_IMAGE_BUILD()
    {
        //Arrange
        SkipUnlessX64Engine();
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_linux_x64_in_the_manylinux_image_within_its_glibc_floor_SLOW_IMAGE_BUILD), Sample, dockerWorkbench: _docker.Workbench);
        var request = harness.Request(BuildMode.Docker, CrateTarget.LinuxX64, CrateTarget.WinX64);
        request.LinuxBaseline = LinuxBaseline.ManyLinux;
        request.Layout = ArtifactLayout.NuGetRuntimes;
        harness.Context.Readiness = TargetMatrixReadiness(request);

        //Act
        var results = await new DockerBuildDriver().BuildAsync(harness.Workspace, request, harness.Context, harness.Context.Progress, TestContext.Current.CancellationToken);

        //Assert
        results[0].Outcome.Should().Be(CrateTargetOutcome.Compiled, results[0].Message);
        results[1].Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        results[1].Message.Should().Be(ManyLinuxImages.NonLinuxReason);
        var path = results[0].ArtifactPaths.Single();
        path.Should().Be(Path.Combine(harness.OutputDirectory, "runtimes", "linux-x64", "native", "libtest_project_13.so"));
        var glibc = DefaultArtifactVerifier.HighestGlibcVersion(NativeArtifactReader.Read(path));
        glibc.Should().NotBeNull();
        Version.Parse(glibc).Should().BeLessThanOrEqualTo(Version.Parse(ManyLinuxImages.X64GlibcFloor));
        results[0].VerificationNotes.Should().Contain(n => n.Contains("within the linux-x64 baseline's 2.28"));
        harness.Context.ToolchainDescription.Should().Contain("Containerfile image");
        harness.Context.ToolchainDescription.Should().Contain(ManyLinuxImages.X64BaseImage);
    }

    private CrateReadinessReport TargetMatrixReadiness(CrateBuildRequest request)
        => ReadinessReportBuilder.ForDocker(request.Targets.ToList(), _docker.Engine, request.LinuxBaseline);

    private static CrateBuilderContext ContextIn(string runDirectory, string cargoTargetDirectory = null)
        => new CrateBuilderContext(new CrateBuilderOptions { ScratchRootDirectory = runDirectory, CargoTargetDirectory = cargoTargetDirectory }, runDirectory, null, null, null);

    [Fact]
    public void GroupsFor_gives_manylinux_one_image_per_platform_with_the_foreign_ones_platform_set()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ContextIn(sandbox.Directory, Path.Combine(sandbox.Directory, "shared"));
        var request = new CrateBuildRequest { Mode = BuildMode.Docker, LinuxBaseline = LinuxBaseline.ManyLinux };
        var x64Engine = new DockerEngineInfo { IsAvailable = true, Architecture = "x86_64" };
        var targets = new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.LinuxRiscV64 };

        //Act
        var groups = DockerBuildDriver.GroupsFor(request, context, x64Engine, targets);

        //Assert
        groups.Should().HaveCount(3);
        groups.Select(g => g.Targets.Single()).Should().Equal(targets);
        groups.Select(g => g.ImageSpec.Platform).Should().Equal(null, "linux/arm64", "linux/riscv64");
        groups.Select(g => Path.GetFileName(g.ImageSpec.Containerfile)).Should().Equal("Containerfile.x86_64", "Containerfile.aarch64", "Containerfile.riscv64");
        groups.Select(g => Path.GetFileName(Path.GetDirectoryName(g.ImageSpec.Containerfile))).Should().Equal("manylinux-x86_64", "manylinux-aarch64", "manylinux-riscv64");
        groups.Select(g => g.SharedTargetDirectory).Should().Equal(
            Path.Combine(sandbox.Directory, "shared", "docker", "manylinux-x86_64"),
            Path.Combine(sandbox.Directory, "shared", "docker", "manylinux-aarch64"),
            Path.Combine(sandbox.Directory, "shared", "docker", "manylinux-riscv64"));
        groups.Select(g => g.SharedTargetDirectory).Distinct().Should().HaveCount(3, "the native builds of the three images all write release/, so they need folders of their own");
    }

    [Fact]
    public void GroupsFor_asks_for_linux_amd64_explicitly_when_the_engine_is_not_x86_64()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ContextIn(sandbox.Directory);
        var request = new CrateBuildRequest { Mode = BuildMode.Docker, LinuxBaseline = LinuxBaseline.ManyLinux };
        var arm64Engine = new DockerEngineInfo { IsAvailable = true, Architecture = "aarch64" };

        //Act
        var groups = DockerBuildDriver.GroupsFor(request, context, arm64Engine, new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64 });

        //Assert
        groups.Select(g => g.ImageSpec.Platform).Should().Equal("linux/amd64", "linux/arm64");
        groups.Should().OnlyContain(g => g.SharedTargetDirectory == null);
    }

    [Fact]
    public void GroupsFor_keeps_the_rust_image_in_one_container_with_the_cross_layers()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ContextIn(sandbox.Directory, Path.Combine(sandbox.Directory, "shared"));
        var request = new CrateBuildRequest { Mode = BuildMode.Docker };
        var x64Engine = new DockerEngineInfo { IsAvailable = true, Architecture = "x86_64" };

        //Act
        var group = DockerBuildDriver.GroupsFor(request, context, x64Engine, new[] { CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.WinX64 }).Single();

        //Assert
        group.Targets.Should().Equal(CrateTarget.LinuxX64, CrateTarget.LinuxArm64, CrateTarget.WinX64);
        group.ImageSpec.Platform.Should().BeNull();
        group.ImageSpec.UsesContainerfile.Should().BeFalse();
        group.ImageSpec.CrossTargets.Should().Contain(CrossTarget.LinuxArm64);
        group.SharedTargetDirectory.Should().Be(Path.Combine(sandbox.Directory, "shared", "docker"));
    }

    [Fact]
    public void DescribeImage_names_the_target_s_own_base_image_and_the_emulated_platform()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var spec = ManyLinuxImages.CreateSpec(CrateTarget.LinuxArm64, sandbox.Directory);
        var image = new RustBuildImage
        {
            ImageName = "repo",
            Tag = "containerfile-0123456789abcdef",
            ImageId = "sha256:aa",
            Spec = spec,
            Platform = "linux/arm64",
            ContainerfileImageReference = "repo:containerfile-0123456789abcdef",
            IsBaseImage = true,
        };
        var engine = new DockerEngineInfo { IsAvailable = true, Architecture = "x86_64", ServerVersion = "29.9.1" };

        //Act
        var description = DockerBuildDriver.DescribeImage(image, engine);

        //Assert
        description.Should().Be($"Docker image repo:containerfile-0123456789abcdef (id sha256:aa, from Containerfile image repo:containerfile-0123456789abcdef ({ManyLinuxImages.Arm64BaseImage}, Rust {ManyLinuxImages.RustVersion}), platform linux/arm64 under emulation) on Docker engine 29.9.1 (x86_64)");
    }
}
