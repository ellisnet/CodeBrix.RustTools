using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

public class WebGpuLinkStagerTests
{
    private const string Version = "9.8.7.6";

    private sealed class Setup : IDisposable
    {
        private readonly SampleSandbox _sandbox = SampleSandbox.CreateEmpty();

        public Setup(string packageVersion = Version)
        {
            PackageDirectory = WebGpuTestPackage.CreateFake(Path.Combine(_sandbox.Directory, "package"), packageVersion, CrateTargets.All.ToArray());
            WorkspaceDirectory = _sandbox.CreateDirectory("staged");
            Workspace = new StagedWorkspace { Directory = WorkspaceDirectory, ManifestPath = Path.Combine(WorkspaceDirectory, "Cargo.toml"), CrateName = "probe" };
            Context = new CrateBuilderContext(new CrateBuilderOptions { WebGpuNativePackageDirectory = PackageDirectory }, _sandbox.CreateDirectory("run"), null, null, null);
            Stager = new WebGpuLinkStager(new NativePackageLocator(Version, _ => null, null, null));
        }

        public string PackageDirectory { get; }

        public string WorkspaceDirectory { get; }

        public StagedWorkspace Workspace { get; }

        public CrateBuilderContext Context { get; }

        public WebGpuLinkStager Stager { get; }

        public string Link(params string[] parts) => Path.Combine(new[] { WorkspaceDirectory, ".codebrix-wgpu" }.Concat(parts).ToArray());

        public Task<CodeBrix.RustTools.CrateBuilder.Building.BuildContribution> PrepareAsync(CrateTarget target, BuildMode mode = BuildMode.Host)
            => Stager.PrepareAsync(Workspace, target, mode, Context, TestContext.Current.CancellationToken);

        public void Dispose() => _sandbox.Dispose();
    }

    [Fact]
    public async Task PrepareAsync_stages_the_include_files_and_the_target_library_and_returns_the_variables_and_the_link_flag()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var contribution = await setup.PrepareAsync(CrateTarget.LinuxX64);

        //Assert
        foreach (var file in NativePackageLocator.RequiredIncludeFiles)
        {
            File.ReadAllText(setup.Link("include", file)).Should().Be(File.ReadAllText(Path.Combine(setup.PackageDirectory, "include", file)));
        }

        File.ReadAllText(setup.Link("linux-x64", "libcodebrix_wgpu.so")).Should().Be(File.ReadAllText(Path.Combine(setup.PackageDirectory, "runtimes", "linux-x64", "native", "libcodebrix_wgpu.so")));
        File.Exists(setup.Link("linux-x64", "LICENSE-WgpuNative.txt")).Should().BeTrue();
        contribution.Environment.Should().HaveCount(2);
        contribution.Environment["CODEBRIX_WGPU_INCLUDE_DIR"].Should().Be("{workspace}/.codebrix-wgpu/include");
        contribution.Environment["CODEBRIX_WGPU_LIB_DIR"].Should().Be("{workspace}/.codebrix-wgpu/linux-x64");
        contribution.RustFlags.Should().Equal("-L native={workspace}/.codebrix-wgpu/linux-x64");
        contribution.Notes.Should().Contain(n => n.Contains(Version) && n.Contains(".codebrix-wgpu/linux-x64/libcodebrix_wgpu.so"));
        setup.Context.WebGpuNativeVersion.Should().Be(Version);
        setup.Context.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task PrepareAsync_for_windows_sets_the_variables_but_no_link_flag_because_raw_dylib_needs_none()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var contribution = await setup.PrepareAsync(CrateTarget.WinArm64);

        //Assert
        File.Exists(setup.Link("win-arm64", "codebrix_wgpu.dll")).Should().BeTrue();
        contribution.Environment["CODEBRIX_WGPU_LIB_DIR"].Should().Be("{workspace}/.codebrix-wgpu/win-arm64");
        contribution.Environment["CODEBRIX_WGPU_INCLUDE_DIR"].Should().Be("{workspace}/.codebrix-wgpu/include");
        contribution.RustFlags.Should().BeEmpty();
        contribution.Notes.Should().Contain(n => n.Contains("raw-dylib"));
    }

    [Fact]
    public async Task PrepareAsync_for_macos_stages_the_dylib_and_adds_the_link_flag()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var contribution = await setup.PrepareAsync(CrateTarget.OsxArm64);

        //Assert
        File.Exists(setup.Link("osx-arm64", "libcodebrix_wgpu.dylib")).Should().BeTrue();
        contribution.RustFlags.Should().Equal("-L native={workspace}/.codebrix-wgpu/osx-arm64");
    }

    [Fact]
    public async Task PrepareAsync_for_android_notes_the_api_33_floor()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var contribution = await setup.PrepareAsync(CrateTarget.AndroidArm64);

        //Assert
        File.Exists(setup.Link("android-arm64", "libcodebrix_wgpu.so")).Should().BeTrue();
        contribution.RustFlags.Should().Equal("-L native={workspace}/.codebrix-wgpu/android-arm64");
        contribution.Notes.Should().Contain(n => n.Contains("API 33"));
    }

    [Fact]
    public async Task PrepareAsync_gives_the_same_tokenised_values_in_docker_mode()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var host = await setup.PrepareAsync(CrateTarget.LinuxArm64, BuildMode.Host);
        var docker = await setup.PrepareAsync(CrateTarget.LinuxArm64, BuildMode.Docker);

        //Assert
        docker.Environment.Should().BeEquivalentTo(host.Environment);
        docker.RustFlags.Should().Equal(host.RustFlags);
    }

    [Fact]
    public async Task PrepareAsync_twice_rewrites_nothing_and_stages_each_target_beside_the_others()
    {
        //Arrange
        using var setup = new Setup();
        await setup.PrepareAsync(CrateTarget.LinuxX64);
        var files = Directory.GetFiles(setup.Link(), "*", SearchOption.AllDirectories);
        var past = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var file in files)
        {
            File.SetLastWriteTimeUtc(file, past);
        }

        //Act
        await setup.PrepareAsync(CrateTarget.LinuxX64);
        await setup.PrepareAsync(CrateTarget.LinuxArm64);

        //Assert
        foreach (var file in files)
        {
            File.GetLastWriteTimeUtc(file).Should().Be(past, file);
        }

        File.Exists(setup.Link("linux-arm64", "libcodebrix_wgpu.so")).Should().BeTrue();
        File.Exists(setup.Link("linux-x64", "libcodebrix_wgpu.so")).Should().BeTrue();
    }

    [Fact]
    public async Task PrepareAsync_replaces_a_staged_file_whose_bytes_differ()
    {
        //Arrange
        using var setup = new Setup();
        await setup.PrepareAsync(CrateTarget.LinuxX64);
        File.WriteAllText(setup.Link("include", "codebrix_wgpu_procs.rs"), "stale\n");

        //Act
        await setup.PrepareAsync(CrateTarget.LinuxX64);

        //Assert
        File.ReadAllText(setup.Link("include", "codebrix_wgpu_procs.rs")).Should().Be(File.ReadAllText(Path.Combine(setup.PackageDirectory, "include", "codebrix_wgpu_procs.rs")));
    }

    [Fact]
    public async Task PrepareAsync_writes_the_how_to_link_recipe_with_the_exact_rust_lines()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        await setup.PrepareAsync(CrateTarget.LinuxX64);

        //Assert
        var text = File.ReadAllText(setup.Link("HOW-TO-LINK.txt"));
        text.Should().Contain(Version);
        text.Should().Contain("include!(concat!(env!(\"CODEBRIX_WGPU_INCLUDE_DIR\"), \"/codebrix_wgpu_procs.rs\"));");
        text.Should().Contain("println!(\"cargo::rustc-cfg=codebrix_wgpu_direct\");");
        text.Should().Contain("println!(\"cargo::rustc-link-search=native={}\", dir.to_string_lossy());");
        text.Should().Contain("wgpu_ffi::direct::wgpuCodebrixGetApiVersion()");
        text.Should().Contain("EnsureLoaded");
        text.Should().NotContain("\r");
    }

    [Fact]
    public void The_recipe_lines_match_the_fixture_crate()
    {
        //Arrange
        var lib = File.ReadAllText(Path.Combine(RepoPaths.Sample(16), "src", "lib.rs"));
        var build = File.ReadAllText(Path.Combine(RepoPaths.Sample(16), "build.rs"));

        //Act + Assert
        foreach (var line in WebGpuLinkRecipe.ModuleLines)
        {
            lib.Should().Contain(line);
        }

        foreach (var line in WebGpuLinkRecipe.BuildScriptLines)
        {
            build.Should().Contain(line);
        }
    }

    [Fact]
    public async Task PrepareAsync_with_a_package_of_another_version_warns_and_records_that_version()
    {
        //Arrange
        using var setup = new Setup("1.2.3.4");

        //Act
        var contribution = await setup.PrepareAsync(CrateTarget.LinuxX64);

        //Assert
        setup.Context.WebGpuNativeVersion.Should().Be("1.2.3.4");
        setup.Context.Warnings.Should().ContainSingle(w => w.Contains("1.2.3.4") && w.Contains(Version));
        contribution.Notes.Should().Contain(n => n.Contains("references " + Version));
    }

    [Fact]
    public async Task PrepareAsync_without_the_targets_library_throws_a_building_stage_error()
    {
        //Arrange
        using var setup = new Setup();
        File.Delete(Path.Combine(setup.PackageDirectory, "runtimes", "linux-riscv64", "native", "libcodebrix_wgpu.so"));

        //Act
        var act = () => setup.PrepareAsync(CrateTarget.LinuxRiscV64);

        //Assert
        var thrown = (await act.Should().ThrowAsync<CrateBuilderException>()).Which;
        thrown.Stage.Should().Be(CrateBuildStage.Building);
        thrown.Message.Should().Contain("linux-riscv64");
    }

    [Fact]
    public async Task PrepareAsync_requires_a_workspace_folder()
    {
        //Arrange
        using var setup = new Setup();

        //Act
        var act = () => setup.Stager.PrepareAsync(new StagedWorkspace(), CrateTarget.LinuxX64, BuildMode.Host, null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
