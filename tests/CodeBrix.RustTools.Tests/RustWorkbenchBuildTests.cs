using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Running;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Tests.Projects;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// The cargo verbs, dependency inspection and binary running of <see cref="RustWorkbench"/>,
/// on sandboxed copies of the samples.
/// </summary>
public class RustWorkbenchBuildTests
{
    private const string MinimalManifest = "[package]\nname = \"tiny\"\nversion = \"0.1.0\"\nedition = \"2021\"\n";

    private static RustWorkbench WorkbenchWithoutCargo(SampleSandbox sandbox)
        => new RustWorkbench(new RustWorkbenchOptions
        {
            CargoPath = Path.Combine(sandbox.Directory, "no-such-cargo"),
            RustcPath = Path.Combine(sandbox.Directory, "no-such-rustc"),
        });

    [Fact]
    public void OpenProject_opens_a_sample_with_the_workbench_options()
    {
        //Arrange
        var workbench = new RustWorkbench();

        //Act
        var project = workbench.OpenProject(RepoPaths.Sample(5));

        //Assert
        project.Name.Should().Be("test_project_05");
        project.Options.Should().BeSameAs(workbench.Options);
    }

    [Fact]
    public async Task BuildAsync_rejects_a_null_project()
    {
        //Arrange
        var workbench = new RustWorkbench();

        //Act
        var act = () => workbench.BuildAsync(null, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task BuildAsync_throws_ToolchainNotFoundException_with_guidance_when_cargo_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var workbench = WorkbenchWithoutCargo(sandbox);
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var act = () => workbench.BuildAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Readiness.Should().NotBeNull();
        exception.Readiness.MissingTools.Should().Contain(RustTool.Cargo);
        exception.Guidance.Should().Contain(g => g.Topic == InstallGuidanceTopic.InstallRust);
        exception.Message.Should().Contain("cargo");
    }

    [Fact]
    public async Task CleanAsync_skips_the_readiness_check()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var workbench = WorkbenchWithoutCargo(sandbox);
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var act = () => workbench.CleanAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Readiness.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_builds_sample_05_without_a_gate_estimates_the_total_and_raises_the_progress_event()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var workbench = new RustWorkbench();
        var events = new ConcurrentQueue<CargoProgress>();
        workbench.Progress += (_, p) => events.Enqueue(p);
        var progress = new CollectingProgress();
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.BuildAsync(project, progress: progress, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.LicenseAudit.Should().BeNull();
        result.LicenseGateFailed.Should().BeFalse();
        result.RunnableArtifacts.Should().ContainSingle().Which.TargetName.Should().Be("textkit");
        progress.Reports.Should().Contain(p => p.Phase == CargoProgressPhase.Finished);
        progress.Reports.Should().OnlyContain(p => p.Total == 4, "the estimate covers test_project_05 and its three path dependencies");
        var finished = progress.Reports.Single(p => p.Phase == CargoProgressPhase.Finished);
        finished.Completed.Should().Be(4);
        finished.Fraction.Should().Be(1.0);
        events.Should().Contain(p => p.Phase == CargoProgressPhase.Compiling && p.CrateName == "test_project_05");
    }

    [Fact]
    public async Task BuildAsync_keeps_an_expected_crate_count_set_by_the_caller()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var workbench = new RustWorkbench();
        var progress = new CollectingProgress();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { ExpectedCrateCount = 40 };

        //Act
        var result = await workbench.BuildAsync(project, options, progress, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        progress.Reports.Should().OnlyContain(p => p.Total == 40);
    }

    [Fact]
    public async Task BuildAsync_estimates_the_total_of_the_sample_04_workspace_from_the_target_graph()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(4);
        var everyPlatform = await TestMetadata.GetMetadataAsync(sandbox);
        var workbench = new RustWorkbench();
        var progress = new CollectingProgress();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { Locked = true };

        //Act
        var result = await workbench.BuildAsync(project, options, progress, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        var finished = progress.Reports.Single(p => p.Phase == CargoProgressPhase.Finished);
        finished.Total.Should().NotBeNull();
        finished.Total.Should().Be(finished.Completed, "every package of the target graph is compiled in a clean build");
        // Sample 04's only platform-specific packages (anstyle-wincon, once_cell_polyfill,
        // windows-link, windows-sys) are Windows ones, so on Windows the target graph is the
        // whole graph and there is nothing to leave out.
        if (!OperatingSystem.IsWindows())
        {
            finished.Total.Should().BeLessThan(everyPlatform.ResolveNodes.Count, "other platforms' dependencies are not counted");
        }
    }

    [Fact]
    public async Task RunAsync_runs_the_binary_built_by_BuildAsync()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var workbench = new RustWorkbench();
        var build = await workbench.BuildAsync(workbench.OpenProject(sandbox.Directory), cancellationToken: TestContext.Current.CancellationToken);
        var options = new RunOptions();
        options.Arguments.Add("Never odd or even");

        //Act
        var run = await workbench.RunAsync(build.RunnableArtifacts.Single(), options, TestContext.Current.CancellationToken);

        //Assert
        run.Succeeded.Should().BeTrue(run.StandardError);
        run.StandardOutput.Should().Contain("palindrome: true");
    }

    [Fact]
    public async Task TestAsync_runs_the_tests_of_sample_05()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var workbench = new RustWorkbench();

        //Act
        var result = await workbench.TestAsync(workbench.OpenProject(sandbox.Directory), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.AllTestsPassed.Should().BeTrue(result.StandardError);
        result.TestsPassed.Should().BeGreaterThan(0);
        result.Command.Should().Be(CargoCommand.Test);
    }

    [Fact]
    public async Task CheckAsync_and_CleanAsync_work_on_sample_10()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(10);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var check = await workbench.CheckAsync(project, cancellationToken: TestContext.Current.CancellationToken);
        var clean = await workbench.CleanAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        check.Success.Should().BeTrue(check.StandardError);
        check.WarningCount.Should().BeGreaterThan(0);
        clean.Success.Should().BeTrue(clean.StandardError);
        Directory.Exists(Path.Combine(sandbox.Directory, "target", "debug")).Should().BeFalse();
    }

    [Fact]
    public async Task InspectDependenciesAsync_lists_the_tree_of_sample_05()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        CargoAvailability.SkipIfMissing();
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var report = await workbench.InspectDependenciesAsync(project, metadataOptions: new CargoMetadataOptions { Locked = true }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        report.Metadata.Should().NotBeNull();
        report.Inspector.Metadata.Should().BeSameAs(report.Metadata);
        report.TreeText.Should().Contain("test_project_05");
        report.TreeText.Should().Contain("licensed_by_file");
        report.DuplicateVersions.Should().BeEmpty();
        report.OutdatedChecked.Should().BeFalse();
        report.Outdated.Should().BeEmpty();
    }

    [Fact]
    public async Task InspectDependenciesAsync_asks_crates_io_about_every_registry_crate_of_sample_02()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(2);
        CargoAvailability.SkipIfMissing();
        var handler = new StubHttpMessageHandler();
        var workbench = new RustWorkbench(new RustWorkbenchOptions { HttpMessageHandler = handler, CratesIoRequestInterval = TimeSpan.Zero });
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var report = await workbench.InspectDependenciesAsync(project, true, new CargoMetadataOptions { Locked = true }, TestContext.Current.CancellationToken);

        //Assert
        report.OutdatedChecked.Should().BeTrue();
        report.Outdated.Should().BeEmpty();
        handler.Requests.Should().NotBeEmpty();
        handler.Requests.Should().OnlyContain(r => r.Uri.AbsolutePath.StartsWith("/api/v1/crates/") && r.UserAgent == RustWorkbenchOptions.DefaultUserAgent);
        handler.Requests.Should().Contain(r => r.Uri.AbsolutePath.EndsWith("/serde"));
        report.ToString().Should().Contain("0 outdated or yanked");
    }

    [Fact]
    public void ToMetadataOptions_copies_the_dependency_selection()
    {
        //Arrange
        var options = new CargoBuildOptions { Offline = true, Locked = true, Frozen = true, AllFeatures = true, NoDefaultFeatures = true };
        options.Features.Add(" json ");
        options.Features.Add(" ");

        //Act
        var metadataOptions = RustWorkbench.ToMetadataOptions(options);

        //Assert
        metadataOptions.Offline.Should().BeTrue();
        metadataOptions.Locked.Should().BeTrue();
        metadataOptions.Frozen.Should().BeTrue();
        metadataOptions.AllFeatures.Should().BeTrue();
        metadataOptions.NoDefaultFeatures.Should().BeTrue();
        metadataOptions.NoDependencies.Should().BeFalse();
        metadataOptions.Features.Should().Equal("json");
    }
}
