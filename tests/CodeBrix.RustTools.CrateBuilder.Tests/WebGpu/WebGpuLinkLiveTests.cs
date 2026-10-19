using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using CodeBrix.RustTools.Toolchain;
using CodeBrix.WebGpu.Native;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;

/// <summary>
/// LIVE: builds rust-samples/test_project_16 (in a sandbox copy) with the WebGPU link
/// contribution applied, verifies the artifact, and proves on the host that the module
/// reuses the engine .NET loaded. Skips by name without cargo or without the package in the
/// NuGet cache; the cross builds skip by name without their toolchains.
/// </summary>
public class WebGpuLinkLiveTests
{
    private const int FixtureNumber = 16;
    private const string CrateName = "wgpu_link_probe";

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint VersionFunction();

    private sealed class FixtureBuild : IDisposable
    {
        public SampleSandbox Sandbox { get; init; }

        public CargoCommandResult Result { get; init; }

        public string ArtifactPath { get; init; }

        public StagedWorkspace Workspace { get; init; }

        public void Dispose() => Sandbox?.Dispose();
    }

    private static async Task<FixtureBuild> BuildFixtureAsync(CrateTarget target, bool hostTriple, Action<CargoBuildOptions> applyProfile)
    {
        var sandbox = SampleSandbox.FromSample(FixtureNumber);
        try
        {
            var workspace = new StagedWorkspace
            {
                Directory = sandbox.Directory,
                ManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml"),
                UpstreamManifestPath = Path.Combine(sandbox.Directory, "Cargo.toml"),
                CrateName = CrateName,
            };
            var contribution = await new WebGpuLinkStager().PrepareAsync(workspace, target, BuildMode.Host, null, TestContext.Current.CancellationToken);

            string Resolve(string value) => value.Replace(WebGpuLinkRecipe.WorkspaceToken, sandbox.Directory.Replace('\\', '/'), StringComparison.Ordinal);
            var options = new CargoBuildOptions { Locked = true };
            applyProfile?.Invoke(options);
            if (hostTriple)
            {
                options.TargetTriple = null;
            }
            else
            {
                options.TargetTriple = CrateTargets.Triple(target);
            }

            foreach (var pair in contribution.Environment)
            {
                options.Environment[pair.Key] = Resolve(pair.Value);
            }

            if (contribution.RustFlags.Count > 0)
            {
                options.Environment["RUSTFLAGS"] = string.Join(" ", contribution.RustFlags.Select(Resolve));
            }

            var workbench = new RustWorkbench();
            var project = workbench.OpenProject(sandbox.Directory);
            var result = await workbench.BuildAsync(project, options, null, TestContext.Current.CancellationToken);
            result.Success.Should().BeTrue("cargo failed: " + string.Join(Environment.NewLine, result.Errors.Select(e => e.Rendered ?? e.Message)) + Environment.NewLine + result.StandardError);
            var fileName = CrateTargets.NativeFileName(target, CrateName);
            var artifact = result.Artifacts
                .Where(a => a.Kinds.Contains("cdylib"))
                .SelectMany(a => a.Filenames)
                .Single(f => string.Equals(Path.GetFileName(f), fileName, StringComparison.Ordinal));
            return new FixtureBuild { Sandbox = sandbox, Result = result, ArtifactPath = artifact, Workspace = workspace };
        }
        catch
        {
            sandbox.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task The_fixture_built_for_the_host_links_the_engine_by_name_and_reuses_the_copy_dotnet_loaded()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        WebGpuTestPackage.LocateOrSkip();
        var target = WebGpuTestPackage.HostTarget();
        if (target == null)
        {
            Assert.Skip("This host is not one of the runtime identifiers CrateBuilder builds for.");
        }

        using var build = await BuildFixtureAsync(target.Value, hostTriple: true, null);

        //Act
        var notes = new WebGpuLinkVerifier().Verify(build.ArtifactPath, target.Value, build.Workspace, out var ok);
        CodeBrixWebGpuNative.EnsureLoaded();
        var module = NativeLibrary.Load(build.ArtifactPath);
        uint reported;
        uint builtAgainst;
        List<string> mapped;
        try
        {
            reported = Marshal.GetDelegateForFunctionPointer<VersionFunction>(NativeLibrary.GetExport(module, "probe_api_version"))();
            builtAgainst = Marshal.GetDelegateForFunctionPointer<VersionFunction>(NativeLibrary.GetExport(module, "probe_built_api_version"))();
            mapped = OperatingSystem.IsLinux() ? MappedFilesNamed("libcodebrix_wgpu.so") : null;
        }
        finally
        {
            NativeLibrary.Free(module);
        }

        //Assert
        ok.Should().BeTrue(string.Join(Environment.NewLine, notes));
        notes.Should().Contain(n => n.Contains("links " + WebGpuLinkVerifier.ExpectedLinkName(target.Value) + ", so the loader reuses"));
        reported.Should().Be(CodeBrixWebGpuNative.ApiVersion);
        reported.Should().Be(CodeBrixWebGpuNative.ExpectedApiVersion);
        reported.Should().Be(2u);
        builtAgainst.Should().Be(CodeBrixWebGpuNative.ExpectedApiVersion);
        if (mapped != null)
        {
            mapped.Should().ContainSingle();
            mapped[0].Should().Be(RealPath(CodeBrixWebGpuNative.LoadedPath));
            mapped[0].Should().NotStartWith(RealPath(build.Sandbox.Directory));
        }
    }

    [Fact]
    public async Task The_fixture_cross_built_for_linux_arm64_passes_the_link_verifier()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        WebGpuTestPackage.LocateOrSkip();
        if (WebGpuTestPackage.HostTarget() == CrateTarget.LinuxArm64 || !OperatingSystem.IsLinux())
        {
            Assert.Skip("The linux-arm64 cross build runs on a Linux host of another architecture.");
        }

        FixtureBuild build;
        try
        {
            build = await BuildFixtureAsync(CrateTarget.LinuxArm64, hostTriple: false, o => LinuxLinkProfile.ForTriple(LinuxLinkProfile.Arm64Triple).ApplyTo(o, LinuxLinkProfile.Arm64Triple));
        }
        catch (ToolchainNotFoundException ex)
        {
            Assert.Skip("The linux-arm64 cross toolchain is not ready: " + ex.Message);
            return;
        }

        using (build)
        {
            //Act
            var notes = new WebGpuLinkVerifier().Verify(build.ArtifactPath, CrateTarget.LinuxArm64, build.Workspace, out var ok);

            //Assert
            ok.Should().BeTrue(string.Join(Environment.NewLine, notes));
            notes.First().Should().Contain("(Elf, arm64)");
        }
    }

    [Fact]
    public async Task The_fixture_built_for_android_arm64_at_api_33_passes_the_link_verifier()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        WebGpuTestPackage.LocateOrSkip();
        var ndk = AndroidNdkLocator.Find();
        if (ndk == null)
        {
            Assert.Skip("No Android NDK was found (set ANDROID_NDK_HOME or ANDROID_HOME).");
        }

        if (!ndk.SupportsApiLevel(WebGpuLinkRecipe.AndroidApiFloor))
        {
            Assert.Skip("The Android NDK at " + ndk.Path + " has no clang wrappers for API 33.");
        }

        FixtureBuild build;
        try
        {
            build = await BuildFixtureAsync(CrateTarget.AndroidArm64, hostTriple: false, o => AndroidLinkProfile.ForNdk(ndk, WebGpuLinkRecipe.AndroidApiFloor).ApplyTo(o, AndroidLinkProfile.Arm64Triple));
        }
        catch (ToolchainNotFoundException ex)
        {
            Assert.Skip("The Android target is not ready: " + ex.Message);
            return;
        }

        using (build)
        {
            //Act
            var notes = new WebGpuLinkVerifier().Verify(build.ArtifactPath, CrateTarget.AndroidArm64, build.Workspace, out var ok);

            //Assert
            ok.Should().BeTrue(string.Join(Environment.NewLine, notes));
            notes.First().Should().Contain("(Elf, arm64)");
        }
    }

    private static List<string> MappedFilesNamed(string fileName)
        => File.ReadAllLines("/proc/self/maps")
            .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 6)
            .Select(parts => parts[5].Trim())
            .Where(path => path.StartsWith('/'))
            .Where(path => string.Equals(Path.GetFileName(path), fileName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var info = new FileInfo(full);
        var target = info.Exists ? info.ResolveLinkTarget(returnFinalTarget: true) : null;
        return target?.FullName ?? full;
    }
}
