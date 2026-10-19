using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Facade;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Internal.Pipeline;

public class CrateBuilderPipelineTests
{
    private static CrateBuildRequest Request(SampleSandbox sandbox, params CrateTarget[] targets) => new CrateBuildRequest
    {
        Source = new LocalManifestSource("/src/fake/Cargo.toml"),
        Targets = targets.Length == 0 ? new List<CrateTarget> { CrateTarget.LinuxX64 } : targets.ToList(),
        OutputDirectory = Path.Combine(sandbox.Directory, "out"),
    };

    private static CrateBuilder Builder(SampleSandbox sandbox, FakeStages fakes, bool keepScratch = false)
        => new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = Path.Combine(sandbox.Directory, "scratch"), KeepScratch = keepScratch }, fakes.ToStages());

    [Fact]
    public async Task stages_run_in_the_plans_order_and_the_record_is_written()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages();
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox, CrateTarget.LinuxX64, CrateTarget.WinX64);
        request.Wrapper = new WrapperOptions { AssemblyName = "Fake.Wrapper", RootNamespace = "Fake.Wrapper" };
        var reported = new List<CrateBuildProgress>();

        //Act
        var result = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(reported.Add), TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness", "ResolvingSource", "Staging", "Facade", "Building", "Verifying", "Wrapping");
        result.Success.Should().BeTrue();
        result.FailedStage.Should().BeNull();
        result.Targets.Select(t => t.Outcome).Should().OnlyContain(o => o == CrateTargetOutcome.Compiled);
        result.Targets.Select(t => t.Rid).Should().Equal("linux-x64", "win-x64");
        result.Targets[0].VerificationNotes.Should().Equal("fake verification");
        result.Wrapper.ProjectPath.Should().Be("/out/Wrapper/Wrapper.csproj");
        result.BuildRecordPath.Should().Be(Path.Combine(request.OutputDirectory, BuildRecord.DefaultFileName));
        File.Exists(result.BuildRecordPath).Should().BeTrue();
        var record = BuildRecord.Load(result.BuildRecordPath);
        record.Targets.Should().HaveCount(2);
        record.Targets[1].Artifacts[0].Path.Should().Be("runtimes/win-x64/native/fake_crate_facade.dll");
        record.Targets[1].Artifacts[0].Sha256.Should().HaveLength(64);
        record.Wrapper.AssemblyName.Should().Be("Fake.Wrapper");
        record.Wrapper.EntryClassName.Should().Be("FakeEntry");
        record.Source.SourceHash.Should().Be(new string('c', 64));
        var stages = reported.Select(p => p.Stage).Distinct().ToList();
        stages.Should().Equal(CrateBuildStage.Readiness, CrateBuildStage.ResolvingSource, CrateBuildStage.Staging, CrateBuildStage.ExtractingApi,
            CrateBuildStage.Building, CrateBuildStage.Verifying, CrateBuildStage.Wrapping, CrateBuildStage.Recording, CrateBuildStage.CleaningUp);
    }

    [Fact]
    public async Task a_cdylib_without_a_wrapper_skips_the_facade_stages()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = new[] { "cdylib", "rlib" } };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness", "ResolvingSource", "Staging", "Building", "Verifying");
        result.Success.Should().BeTrue();
        result.Wrapper.Should().BeNull();
    }

    [Fact]
    public async Task an_rlib_without_a_wrapper_gets_a_facade()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = Array.Empty<string>() };
        using var builder = Builder(sandbox, fakes);

        //Act
        await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Contain("Facade");
    }

    [Fact]
    public async Task facade_mode_auto_routes_a_cdylib_that_exports_nothing_through_the_facade_stages_in_the_same_run()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = new[] { "cdylib" }, CrateExportsNothing = true };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness", "ResolvingSource", "Staging", "Building", "Facade", "Building", "Verifying");
        result.Success.Should().BeTrue(result.ToString());
        result.Warnings.Should().Contain(w => w.Contains("routed through the facade stages", StringComparison.Ordinal));
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.Facade.Reason.Should().Be(CrateBuildPipeline.AutoFallbackReason);
        BuildRecord.Load(result.BuildRecordPath).Facade.Reason.Should().Be(CrateBuildPipeline.AutoFallbackReason);
        result.Targets.Single().ArtifactPaths.Single().Should().EndWith("libfake_crate_facade.so");
    }

    [Fact]
    public async Task facade_mode_never_builds_the_crate_as_it_is_and_reports_the_missing_exports()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = Array.Empty<string>(), CrateExportsNothing = true };
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox);
        request.FacadeMode = FacadeMode.Never;

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness", "ResolvingSource", "Staging", "Building");
        result.Success.Should().BeFalse();
        result.FailedStage.Should().Be(CrateBuildStage.Building);
        result.FailureMessage.Should().Contain(DefaultArtifactVerifier.NoExportsNote);
        result.Record.Facade.Should().BeNull();
    }

    [Fact]
    public async Task facade_mode_always_runs_the_facade_stages_for_a_cdylib_and_records_why()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = new[] { "cdylib" } };
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox);
        request.FacadeMode = FacadeMode.Always;

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness", "ResolvingSource", "Staging", "Facade", "Building", "Verifying");
        result.Record.Facade.Reason.Should().Be("FacadeMode.Always was requested.");
    }

    [Fact]
    public void NeedsFacade_follows_the_facade_mode_and_auto_decides_from_the_wrapper_and_crate_types()
    {
        //Arrange
        StagedWorkspace Staged(params string[] types) => new StagedWorkspace { Identity = new CrateSourceIdentity { CrateTypes = types } };
        var wrapper = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" };

        //Assert
        CrateBuildPipeline.NeedsFacade(new CrateBuildRequest(), Staged("cdylib")).Should().BeFalse();
        CrateBuildPipeline.NeedsFacade(new CrateBuildRequest(), Staged("rlib")).Should().BeTrue();
        CrateBuildPipeline.NeedsFacade(new CrateBuildRequest { Wrapper = wrapper }, Staged("cdylib")).Should().BeTrue();
        CrateBuildPipeline.NeedsFacade(new CrateBuildRequest { FacadeMode = FacadeMode.Never }, Staged("rlib")).Should().BeFalse();
        CrateBuildPipeline.NeedsFacade(new CrateBuildRequest { FacadeMode = FacadeMode.Always }, Staged("cdylib")).Should().BeTrue();
        CrateBuildPipeline.FacadeReason(new CrateBuildRequest { Wrapper = wrapper }, Staged("cdylib")).Should().Contain("wrapper");
        CrateBuildPipeline.FacadeReason(new CrateBuildRequest(), Staged("rlib")).Should().Contain("not a cdylib");
    }

    [Fact]
    public void FacadeMode_never_cannot_be_combined_with_a_wrapper()
    {
        //Arrange
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource("/src/fake/Cargo.toml"),
            Targets = { CrateTarget.LinuxX64 },
            OutputDirectory = "/out",
            FacadeMode = FacadeMode.Never,
            Wrapper = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" },
        };
        Action act = () => request.Validate();

        //Act + Assert
        act.Should().Throw<ArgumentException>().WithMessage("*FacadeMode Never*");
        request.Clone().FacadeMode.Should().Be(FacadeMode.Never);
    }

    [Theory]
    [InlineData("ResolvingSource", CrateBuildStage.ResolvingSource)]
    [InlineData("Staging", CrateBuildStage.Staging)]
    [InlineData("Facade", CrateBuildStage.ExtractingApi)]
    [InlineData("Building", CrateBuildStage.Building)]
    [InlineData("Verifying", CrateBuildStage.Verifying)]
    [InlineData("Wrapping", CrateBuildStage.Wrapping)]
    public async Task a_failing_stage_is_reported_not_thrown_and_the_record_is_still_written(string failAt, CrateBuildStage expected)
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { FailAt = failAt, CrateTypes = Array.Empty<string>() };
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox);
        request.Wrapper = new WrapperOptions { AssemblyName = "W", RootNamespace = "W" };

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.FailedStage.Should().Be(expected);
        result.FailureMessage.Should().Be("the fake stage failed");
        fakes.Calls.Last().Should().Be(failAt);
        File.Exists(result.BuildRecordPath).Should().BeTrue();
        result.Record.Warnings.Should().Contain($"Failed at {expected}: the fake stage failed");
        fakes.SourceCleanedUp.Should().Be(failAt != "ResolvingSource");
    }

    [Fact]
    public async Task a_crate_builder_exception_names_its_own_stage()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { FailAt = "Facade", Failure = new FacadeGenerationException("no public items", CrateBuildStage.Planning), CrateTypes = Array.Empty<string>() };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.FailedStage.Should().Be(CrateBuildStage.Planning);
        result.FailureMessage.Should().Be("no public items");
    }

    [Fact]
    public async Task a_crate_builder_exception_without_a_stage_gets_the_running_one()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var failure = new CrateBuilderException("staging went wrong");
        var fakes = new FakeStages { FailAt = "Staging", Failure = failure };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.FailedStage.Should().Be(CrateBuildStage.Staging);
        failure.Stage.Should().Be(CrateBuildStage.Staging);
    }

    [Fact]
    public async Task an_invalid_request_throws_before_any_stage()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages();
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox);
        request.OutputDirectory = null;

        //Act
        var act = () => builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
        fakes.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task a_blocking_readiness_issue_ends_the_build_without_a_record()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { ReadinessBlocks = true };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.Calls.Should().Equal("Readiness");
        result.FailedStage.Should().Be(CrateBuildStage.Readiness);
        result.FailureMessage.Should().Contain("Cargo is not installed.");
        result.BuildRecordPath.Should().BeNull();
        result.Record.Should().BeNull();
        result.Targets.Single().Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        Directory.Exists(Path.Combine(sandbox.Directory, "out")).Should().BeFalse();
    }

    [Fact]
    public async Task unbuildable_targets_are_reported_and_the_others_are_built()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { Unbuildable = new[] { CrateTarget.OsxArm64 } };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox, CrateTarget.OsxArm64, CrateTarget.LinuxX64), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        fakes.TargetsBuilt.Should().Equal(CrateTarget.LinuxX64);
        result.Targets.Select(t => t.Target).Should().Equal(CrateTarget.OsxArm64, CrateTarget.LinuxX64);
        result.Targets[0].Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
        result.Targets[0].Message.Should().Be("std missing for OsxArm64");
        result.Targets[1].Outcome.Should().Be(CrateTargetOutcome.Compiled);
        result.FailedStage.Should().BeNull();
        result.Success.Should().BeFalse();
        result.Record.Targets[0].Outcome.Should().Be(CrateTargetOutcome.NotBuildableInThisMode);
    }

    [Fact]
    public void the_default_stages_are_the_real_implementations()
    {
        //Arrange
        //Act
        var stages = CrateBuilderStages.CreateDefault();

        //Assert
        stages.Readiness.Should().BeOfType<ReadinessChecker>();
        stages.SourceResolver.Should().BeOfType<SourceResolver>();
        stages.WorkspaceStager.Should().BeOfType<WorkspaceStager>();
        stages.FacadeGenerator.Should().BeOfType<FacadeGenerator>();
        stages.BuildDriver.Should().BeOfType<CodeBrix.RustTools.CrateBuilder.Building.ModeBuildDriver>();
        stages.ArtifactVerifier.Should().BeOfType<VerificationRecorder>();
        stages.WrapperWriter.Should().BeOfType<WrapperProjectWriter>();
    }

    [Fact]
    public async Task the_facade_lib_name_names_the_native_file_and_the_record_lists_the_exports()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { CrateTypes = Array.Empty<string>() };
        using var builder = Builder(sandbox, fakes);

        //Act
        var result = await builder.BuildAsync(Request(sandbox, CrateTarget.LinuxX64), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue();
        result.Targets[0].ArtifactPaths[0].Should().EndWith("libfake_crate_facade.so");
        result.Record.Facade.Should().NotBeNull();
        result.Record.Facade.LibName.Should().Be("fake_crate_facade");
        result.Record.Facade.PackageName.Should().Be("fake-crate-facade");
        result.Record.Facade.Generated.Should().BeTrue();
    }

    [Fact]
    public async Task the_run_folder_is_deleted_unless_kept()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var deleted = new FakeStages();
        var kept = new FakeStages();
        using var deleting = Builder(sandbox, deleted);
        using var keeping = Builder(sandbox, kept, keepScratch: true);

        //Act
        await deleting.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);
        await keeping.BuildAsync(Request(sandbox), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        deleted.StagedRunDirectory.Should().StartWith(Path.Combine(sandbox.Directory, "scratch", "runs"));
        Directory.Exists(deleted.StagedRunDirectory).Should().BeFalse();
        Directory.Exists(kept.StagedRunDirectory).Should().BeTrue();
        deleted.SourceCleanedUp.Should().BeTrue();
    }

    [Fact]
    public async Task the_callers_cancellation_propagates_after_cleanup()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var fakes = new FakeStages
        {
            DuringBuild = token =>
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            },
        };
        using var builder = Builder(sandbox, fakes);

        //Act
        var act = () => builder.BuildAsync(Request(sandbox), cancellationToken: cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        fakes.SourceCleanedUp.Should().BeTrue();
        Directory.Exists(fakes.StagedRunDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task the_request_timeout_is_reported_as_a_failed_stage()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { DuringBuild = token => Task.Delay(Timeout.InfiniteTimeSpan, token) };
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox);
        request.Timeout = TimeSpan.FromMilliseconds(200);

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.FailedStage.Should().Be(CrateBuildStage.Building);
        result.FailureMessage.Should().Contain("timed out");
        File.Exists(result.BuildRecordPath).Should().BeTrue();
    }

    [Fact]
    public async Task the_callers_request_is_not_changed_and_the_event_sees_every_report()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakes = new FakeStages { Unbuildable = new[] { CrateTarget.WinArm64 } };
        using var builder = Builder(sandbox, fakes);
        var request = Request(sandbox, CrateTarget.WinArm64, CrateTarget.LinuxX64);
        var events = new List<CrateBuildStage>();
        builder.Progress += (_, p) => events.Add(p.Stage);
        builder.Progress += (_, _) => throw new InvalidOperationException("a broken handler");

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        request.Targets.Should().Equal(CrateTarget.WinArm64, CrateTarget.LinuxX64);
        events.Should().Contain(CrateBuildStage.CleaningUp);
        result.Targets.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(CrateTarget.OsxArm64, BuildMode.Docker, CodeBrix.RustTools.Toolchain.OperatingSystemKind.MacOS, ArtifactProvenance.DevelopmentBuild)]
    [InlineData(CrateTarget.OsxArm64, BuildMode.Host, CodeBrix.RustTools.Toolchain.OperatingSystemKind.DebianLinux, ArtifactProvenance.DevelopmentBuild)]
    [InlineData(CrateTarget.OsxX64, BuildMode.Host, CodeBrix.RustTools.Toolchain.OperatingSystemKind.MacOS, ArtifactProvenance.Distributable)]
    [InlineData(CrateTarget.LinuxX64, BuildMode.Docker, CodeBrix.RustTools.Toolchain.OperatingSystemKind.DebianLinux, ArtifactProvenance.Distributable)]
    public void DefaultProvenance_marks_macos_files_not_built_on_a_mac_as_development_builds(CrateTarget target, BuildMode mode, CodeBrix.RustTools.Toolchain.OperatingSystemKind host, ArtifactProvenance expected)
        => CrateBuildPipeline.DefaultProvenance(target, mode, host).Should().Be(expected);
}
