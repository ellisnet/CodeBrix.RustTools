using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;
using IArtifactVerifier = CodeBrix.RustTools.CrateBuilder.Building.IArtifactVerifier;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

public class HostBuildDriverTests
{
    /// <summary>Set to a folder holding usr/lib/libSystem.tbd (exported from a
    /// CodeBrix.RustTools.Docker image with the macOS layer) to run the macOS host test.</summary>
    public const string MacOSSysrootVariable = "CODEBRIX_CRATEBUILDER_MACOS_SYSROOT";

    private const int Sample = 13;
    private const string Export = "cb_add";

    private static readonly Lazy<Task<RustToolchainInfo>> Toolchain = new Lazy<Task<RustToolchainInfo>>(
        () => new RustWorkbench().DetectToolchainAsync(null, CancellationToken.None));

    private static CrateBuilderOptions Options() => new CrateBuilderOptions { MacOSSysrootDirectory = Environment.GetEnvironmentVariable(MacOSSysrootVariable) };

    /// <summary>Skips unless cargo is installed and this machine's readiness check finds every
    /// target buildable in host mode (the reason is the readiness check's own).</summary>
    private static async Task SkipUnlessBuildableAsync(params CrateTarget[] targets)
    {
        await Capabilities.SkipIfCargoMissingAsync();
        var report = ReadinessReportBuilder.ForHost(targets, await Toolchain.Value, HostEvaluationSettings.FromOptions(Options()));
        var missing = report.UnbuildableTargets.ToList();
        if (missing.Count > 0)
        {
            var issues = string.Join("; ", report.Issues.Where(i => (i.Target.HasValue && missing.Contains(i.Target.Value)) || (i.IsBlocking && i.Code != "targets.none-buildable")).Select(i => i.ToString()));
            var guidance = missing.SelectMany(report.GetGuidance)
                .Select(g => g.Title + (g.Commands.Count > 0 ? ": " + string.Join(" && ", g.Commands) : string.Empty) + (string.IsNullOrEmpty(g.DocumentationUrl) ? string.Empty : " (" + g.DocumentationUrl + ")"))
                .Distinct()
                .ToList();
            var macOS = missing.Any(CrateTargets.IsMacOS) ? " This test reads the exported macOS sysroot folder from the " + MacOSSysrootVariable + " environment variable." : string.Empty;
            Assert.Skip("Not buildable in host mode on this machine: " + issues + (guidance.Count > 0 ? " How to get it: " + string.Join("; ", guidance) + "." : string.Empty) + macOS);
        }
    }

    private static async Task<IReadOnlyList<CrateTargetResult>> BuildAsync(BuildDriverHarness harness, CrateBuildRequest request, HostBuildDriver driver = null)
        => await (driver ?? new HostBuildDriver()).BuildAsync(harness.Workspace, request, harness.Context, harness.Context.Progress, TestContext.Current.CancellationToken);

    private static void ShouldBeCompiled(CrateTargetResult result, string expectedPath)
    {
        result.Outcome.Should().Be(CrateTargetOutcome.Compiled, result.Message);
        result.ArtifactPaths.Should().Equal(expectedPath);
        File.Exists(expectedPath).Should().BeTrue();
        result.CargoResult.Should().NotBeNull();
        result.VerificationNotes.Should().Contain(n => n.Contains(Export));
        NativeArtifactReader.Read(expectedPath).Architecture.Should().Be(NativeArtifactReader.ExpectedArchitecture(result.Target));
    }

    // ---- unit ----

    [Fact]
    public void SelectArtifacts_takes_only_the_crates_dynamic_library_in_the_target_folder()
    {
        //Arrange
        var crate = BuiltCrate.From(new StagedWorkspace { ManifestPath = Path.Combine(RepoPaths.Sample(Sample), "Cargo.toml") });
        const string triple = "aarch64-unknown-linux-gnu";
        var artifacts = new[]
        {
            new BuildArtifact { PackageName = "test_project_13", Kinds = new[] { "cdylib", "rlib" }, Filenames = new[] { "/t/target/" + triple + "/release/libtest_project_13.so", "/t/target/" + triple + "/release/libtest_project_13.rlib" } },
            new BuildArtifact { PackageName = "test_project_13", Kinds = new[] { "bin" }, Executable = "/t/target/" + triple + "/release/test_project_13", Filenames = new[] { "/t/target/" + triple + "/release/test_project_13" } },
            new BuildArtifact { PackageName = "helper", Kinds = new[] { "cdylib" }, Filenames = new[] { "/t/target/" + triple + "/release/libhelper.so" } },
            new BuildArtifact { PackageName = "test_project_13", Kinds = new[] { "proc-macro" }, Filenames = new[] { "/t/target/release/deps/libmacro.so" } },
            new BuildArtifact { PackageName = "test_project_13", Kinds = new[] { "cdylib" }, Filenames = new[] { "/t/target/release/libtest_project_13.so" } },
        };

        //Act
        var selected = HostBuildDriver.SelectArtifacts(artifacts, crate, triple);

        //Assert
        selected.Should().Equal("/t/target/" + triple + "/release/libtest_project_13.so");
    }

    [Fact]
    public void the_android_api_level_is_the_webgpu_floor_only_when_the_crate_links_webgpu()
    {
        //Act
        var plain = HostBuildDriver.AndroidApiLevelFor(new CrateBuildRequest());
        var linked = HostBuildDriver.AndroidApiLevelFor(new CrateBuildRequest { LinkToCodeBrixWebGpu = true });

        //Assert
        plain.Should().Be(AndroidLinkProfile.DefaultApiLevel);
        linked.Should().Be(33);
    }

    [Theory]
    [InlineData("libx.so", true)]
    [InlineData("libx.dylib", true)]
    [InlineData("x.dll", true)]
    [InlineData("libx.dll.a", false)]
    [InlineData("libx.rlib", false)]
    public void IsSharedLibrary_knows_the_three_extensions(string name, bool expected) =>
        HostBuildDriver.IsSharedLibrary("/t/" + name).Should().Be(expected);

    // ---- live (cargo on this machine) ----

    [Fact]
    public async Task BuildAsync_builds_the_host_triple_into_the_runtimes_layout_and_records_the_sha256()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.LinuxX64);
        Skip.UnlessLinuxX64Host(await Toolchain.Value);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_the_host_triple_into_the_runtimes_layout_and_records_the_sha256), Sample, new RustWorkbench(), options: Options());
        var request = harness.Request(BuildMode.Host, CrateTarget.LinuxX64);

        //Act
        var results = await BuildAsync(harness, request);

        //Assert
        var expected = Path.Combine(harness.OutputDirectory, "runtimes", "linux-x64", "native", "libtest_project_13.so");
        ShouldBeCompiled(results.Single(), expected);
        results.Single().Provenance.Should().Be(ArtifactProvenance.Distributable);
        results.Single().CargoResult.CommandLine.Should().Contain("--target x86_64-unknown-linux-gnu");
        harness.Context.ToolchainDescription.Should().StartWith("rustc ");
        harness.Reports.Should().Contain(p => p.Stage == CrateBuildStage.Building && p.Target == CrateTarget.LinuxX64);
        var record = BuildRecordFactory.Create(request, new CrateBuildResult { OutputDirectory = harness.OutputDirectory, Targets = results }, harness.Context, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var artifact = record.Targets.Single().Artifacts.Single();
        artifact.Path.Should().Be("runtimes/linux-x64/native/libtest_project_13.so");
        artifact.Sha256.Should().HaveLength(64);
        artifact.Length.Should().Be(new FileInfo(expected).Length);
        record.ToolchainDescription.Should().Be(harness.Context.ToolchainDescription);
    }

    [Fact]
    public async Task BuildAsync_reports_targets_the_readiness_check_rules_out_without_running_cargo_for_them()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.LinuxX64);
        Skip.UnlessLinuxX64Host(await Toolchain.Value);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_reports_targets_the_readiness_check_rules_out_without_running_cargo_for_them), Sample, new RustWorkbench(), options: Options());
        var request = harness.Request(BuildMode.Host, CrateTarget.WinArm64, CrateTarget.LinuxX64);
        harness.Context.Readiness = new CrateReadinessReport
        {
            IsReady = true,
            Mode = BuildMode.Host,
            BuildableTargets = new[] { CrateTarget.LinuxX64 },
            UnbuildableTargets = new[] { CrateTarget.WinArm64 },
            Issues = new[] { new BuildReadinessIssue { Code = "windows.llvm-mingw-missing", Message = "llvm-mingw was not found.", Target = CrateTarget.WinArm64 } },
            Toolchain = await Toolchain.Value,
        };

        //Act
        var results = await BuildAsync(harness, request);

        //Assert
        results.Select(r => r.Target).Should().Equal(CrateTarget.WinArm64, CrateTarget.LinuxX64);
        results[0].Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        results[0].Message.Should().Contain("llvm-mingw was not found");
        results[0].CargoResult.Should().BeNull();
        results[1].Outcome.Should().Be(CrateTargetOutcome.Compiled, results[1].Message);
    }

    [Fact]
    public async Task BuildAsync_cross_builds_linux_arm64_flat()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.LinuxArm64);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_cross_builds_linux_arm64_flat), Sample, new RustWorkbench(), options: Options());
        var request = harness.Request(BuildMode.Host, CrateTarget.LinuxArm64);
        request.Layout = ArtifactLayout.Flat;
        request.Profile = CrateBuildProfile.Debug;

        //Act
        var results = await BuildAsync(harness, request);

        //Assert
        ShouldBeCompiled(results.Single(), Path.Combine(harness.OutputDirectory, "libtest_project_13.so"));
        results.Single().CargoResult.CommandLine.Should().NotContain("--release");
    }

    [Fact]
    public async Task BuildAsync_cross_builds_linux_riscv64()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.LinuxRiscV64);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_cross_builds_linux_riscv64), Sample, new RustWorkbench(), options: Options());

        //Act
        var results = await BuildAsync(harness, harness.Request(BuildMode.Host, CrateTarget.LinuxRiscV64));

        //Assert
        ShouldBeCompiled(results.Single(), Path.Combine(harness.OutputDirectory, "runtimes", "linux-riscv64", "native", "libtest_project_13.so"));
    }

    [Fact]
    public async Task BuildAsync_builds_both_android_abis_with_the_ndk()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.AndroidArm64, CrateTarget.AndroidX64);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_both_android_abis_with_the_ndk), Sample, new RustWorkbench(), options: Options());

        //Act
        var results = await BuildAsync(harness, harness.Request(BuildMode.Host, CrateTarget.AndroidArm64, CrateTarget.AndroidX64));

        //Assert
        ShouldBeCompiled(results[0], Path.Combine(harness.OutputDirectory, "runtimes", "android-arm64", "native", "libtest_project_13.so"));
        ShouldBeCompiled(results[1], Path.Combine(harness.OutputDirectory, "runtimes", "android-x64", "native", "libtest_project_13.so"));
        NativeArtifactReader.Read(results[0].ArtifactPaths[0]).NeededLibraries.Should().Contain("libc.so");
    }

    [Fact]
    public async Task BuildAsync_builds_macos_through_the_link_profile_as_a_development_build()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.OsxArm64);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_macos_through_the_link_profile_as_a_development_build), Sample, new RustWorkbench(), options: Options());

        //Act
        var results = await BuildAsync(harness, harness.Request(BuildMode.Host, CrateTarget.OsxArm64));

        //Assert
        var path = Path.Combine(harness.OutputDirectory, "runtimes", "osx-arm64", "native", "libtest_project_13.dylib");
        ShouldBeCompiled(results.Single(), path);
        NativeArtifactReader.Read(path).LibraryName.Should().Be("@rpath/libtest_project_13.dylib");
        results.Single().Provenance.Should().Be(OperatingSystem.IsMacOS() ? ArtifactProvenance.Distributable : ArtifactProvenance.DevelopmentBuild);
    }

    [Fact]
    public async Task BuildAsync_builds_windows_x64_through_the_gnullvm_profile()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.WinX64);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_builds_windows_x64_through_the_gnullvm_profile), Sample, new RustWorkbench(), options: Options());

        //Act
        var results = await BuildAsync(harness, harness.Request(BuildMode.Host, CrateTarget.WinX64));

        //Assert
        var path = Path.Combine(harness.OutputDirectory, "runtimes", "win-x64", "native", "test_project_13.dll");
        ShouldBeCompiled(results.Single(), path);
        NativeArtifactReader.Read(path).NeededLibraries.Should().NotContain(n => n.Contains("unwind", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class LinkMapContribution : IBuildContribution
    {
        public Task<BuildContribution> PrepareAsync(StagedWorkspace workspace, CrateTarget target, BuildMode mode, CrateBuilderContext context, CancellationToken cancellationToken)
        {
            var contribution = new BuildContribution();
            contribution.Environment["CODEBRIX_CRATEBUILDER_PROBE"] = BuildContribution.WorkspaceToken + "/probe";
            contribution.RustFlags.Add("-C link-arg=-Wl,-Map=" + BuildContribution.WorkspaceToken + "/link.map");
            contribution.Notes.Add("link map requested for " + CrateTargets.Rid(target));
            return Task.FromResult(contribution);
        }
    }

    private sealed class RejectEverything : IArtifactVerifier
    {
        public IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok)
        {
            ok = false;
            return new[] { "rejected on purpose" };
        }
    }

    [Fact]
    public async Task BuildAsync_applies_contributions_and_a_rejecting_verifier_fails_the_target_but_keeps_the_file()
    {
        //Arrange
        await SkipUnlessBuildableAsync(CrateTarget.LinuxX64);
        Skip.UnlessLinuxX64Host(await Toolchain.Value);
        using var harness = new BuildDriverHarness(nameof(BuildAsync_applies_contributions_and_a_rejecting_verifier_fails_the_target_but_keeps_the_file), Sample, new RustWorkbench(), options: Options());
        var driver = new HostBuildDriver(new IBuildContribution[] { new LinkMapContribution() }, new IArtifactVerifier[] { new DefaultArtifactVerifier(), new RejectEverything() });

        //Act
        var results = await BuildAsync(harness, harness.Request(BuildMode.Host, CrateTarget.LinuxX64), driver);

        //Assert
        var result = results.Single();
        result.Outcome.Should().Be(CrateTargetOutcome.Failed);
        result.Message.Should().Contain("rejected on purpose");
        File.Exists(result.ArtifactPaths.Single()).Should().BeTrue();
        File.Exists(Path.Combine(harness.WorkspaceDirectory, "link.map")).Should().BeTrue();
        result.VerificationNotes.Should().Contain("link map requested for linux-x64");
        result.VerificationNotes.Should().Contain(n => n.Contains(Export));
    }

    private static class Skip
    {
        public static void UnlessLinuxX64Host(RustToolchainInfo toolchain)
        {
            if (!string.Equals(toolchain.HostTriple, "x86_64-unknown-linux-gnu", StringComparison.Ordinal))
            {
                Assert.Skip("This test builds the host triple as linux-x64 and needs an x86-64 Linux host; this host is " + toolchain.HostTriple + ".");
            }
        }
    }
}
