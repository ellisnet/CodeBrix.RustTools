using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Builds;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Docker.Tests.Helpers;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Live;

[Collection(DockerEngineCollection.Name)]
public class ContainerBuildLiveTests
{
    private readonly DockerEngineFixture _fixture;

    public ContainerBuildLiveTests(DockerEngineFixture fixture)
    {
        _fixture = fixture;
    }

    private DockerRustWorkbench Workbench => _fixture.Workbench;

    /// <summary>Whether a program the container built for its own target can run on this
    /// host: a Linux host whose architecture is the engine's (x64 or ARM64).</summary>
    private bool CanRunNativeLinuxOutputOnHost
        => RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
           && _fixture.Engine.NativeLinuxTriple != null
           && _fixture.Engine.NativeLinuxTriple == RuntimeInformation.OSArchitecture switch
           {
               Architecture.X64 => "x86_64-unknown-linux-gnu",
               Architecture.Arm64 => "aarch64-unknown-linux-gnu",
               _ => null,
           };

    private static LicensePolicy StrictPermissive()
    {
        var policy = LicensePolicy.Permissive();
        policy.ExemptWorkspaceMembers = false;
        return policy;
    }

    private static void LogBuild(string what, ContainerBuildResult result)
        => DockerEngineFixture.Log($"{DateTime.Now:O} {what}: {result.Duration} ("
            + string.Join(", ", result.StageDurations.Select(pair => $"{pair.Key} {pair.Value.TotalSeconds:0.0}s")) + ")");

    [Fact]
    public async Task CopySourceAsync_skips_target_git_and_node_modules()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        sandbox.WriteFile("target/debug/stale.txt", "stale");
        sandbox.WriteFile(".git/HEAD", "ref: refs/heads/main");
        sandbox.WriteFile("tools/node_modules/x/index.js", "//");
        sandbox.WriteFile("notes/keep.txt", "kept");
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        var transport = new DockerCopyTransport(Workbench.Client, container.Id);
        var reports = new List<SourceCopyProgress>();

        //Act
        var copy = await Workbench.CopySourceAsync(container, sandbox.Directory, new[] { "notes/skip-me.txt" }, new SynchronousProgress<SourceCopyProgress>(reports.Add), TestContext.Current.CancellationToken);

        //Assert
        (await transport.StatAsync("/work/Cargo.toml", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/vendor/licensed_by_file/LICENSE.txt", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/notes/keep.txt", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/.git", TestContext.Current.CancellationToken)).Exists.Should().BeFalse();
        (await transport.StatAsync("/work/target/debug/stale.txt", TestContext.Current.CancellationToken)).Exists.Should().BeFalse();
        (await transport.StatAsync("/work/tools/node_modules", TestContext.Current.CancellationToken)).Exists.Should().BeFalse();
        copy.FileCount.Should().BeGreaterThan(5);
        copy.FileCount.Should().Be(reports.Count);
        copy.TotalBytes.Should().Be(reports.Last().BytesCopied);
        copy.ExcludePatterns.Should().Contain(new[] { ".git", "target", "node_modules", "notes/skip-me.txt" });
    }

    [Fact]
    public async Task CopySourceAsync_replaces_what_an_earlier_copy_left_in_work()
    {
        //Arrange
        using var first = SampleSandbox.FromSample(5);
        using var second = SampleSandbox.FromSample(13);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        var transport = new DockerCopyTransport(Workbench.Client, container.Id);
        await Workbench.CopySourceAsync(container, first.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        await Workbench.CopySourceAsync(container, second.Directory, null, null, TestContext.Current.CancellationToken);

        //Assert
        (await transport.StatAsync("/work/vendor", TestContext.Current.CancellationToken)).Exists.Should().BeFalse();
        (await transport.StatAsync("/work/src/lib.rs", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/target", TestContext.Current.CancellationToken)).IsDirectory.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_builds_sample_05_and_retrieves_a_runnable_executable()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        using var output = SampleSandbox.CreateEmpty();
        var stages = new List<BuildStage>();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.Flat },
        };

        //Act
        var result = await Workbench.BuildAsync(request, new SynchronousProgress<ContainerBuildProgress>(p => stages.Add(p.Stage)), TestContext.Current.CancellationToken);
        LogBuild("build 05", result);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.FailedStage.Should().BeNull();
        result.ContainerRemoved.Should().BeTrue();
        result.Source.Kind.Should().Be(ContainerBuildSourceKind.ProjectDirectory);
        result.Source.ContainerDirectory.Should().Be("/work");
        result.Results[_fixture.NativeTarget].RunnableArtifacts.Should().ContainSingle().Which.Executable.Should().Be("/work/target/debug/textkit");
        stages.Distinct().Should().Equal(BuildStage.CheckingEngine, BuildStage.EnsuringImage, BuildStage.StartingContainer, BuildStage.CopyingSource,
            BuildStage.Building, BuildStage.RetrievingArtifacts, BuildStage.CleaningUp, BuildStage.Completed);
        var artifact = result.Retrieval.Artifacts.Should().ContainSingle().Which;
        artifact.Kind.Should().Be(ArtifactKind.Executable);
        artifact.HostPath.Should().Be(Path.Combine(output.Directory, "textkit"));
        artifact.TargetName.Should().Be("textkit");
        artifact.IsExecutable.Should().BeTrue();
        artifact.Size.Should().Be(new FileInfo(artifact.HostPath).Length);
        artifact.Sha256.Should().Be(await ArtifactRetriever.ComputeSha256Async(artifact.HostPath, TestContext.Current.CancellationToken));
        artifact.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(artifact.HostPath).HasFlag(UnixFileMode.UserExecute).Should().BeTrue();
        }

        if (CanRunNativeLinuxOutputOnHost)
        {
            var run = Process.Start(new ProcessStartInfo(artifact.HostPath, "Never odd or even") { RedirectStandardOutput = true, RedirectStandardError = true });
            var stdout = await run.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await run.WaitForExitAsync(TestContext.Current.CancellationToken);
            run.ExitCode.Should().Be(0);
            stdout.Should().Contain("palindrome: true");
        }
    }

    [Fact]
    public async Task BuildAsync_retrieves_the_two_executables_of_sample_06()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(6);
        using var output = SampleSandbox.CreateEmpty();
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory },
        };

        //Act
        var result = await Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        LogBuild("build 06", result);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Retrieval.Artifacts.Should().HaveCount(2);
        result.Retrieval.Artifacts.Should().OnlyContain(a => a.Kind == ArtifactKind.Executable && a.IsExecutable);
        result.Retrieval.Artifacts.Select(a => Path.GetDirectoryName(a.HostPath)).Distinct()
            .Should().Equal(Path.Combine(output.Directory, _fixture.Engine.NativeLinuxTriple));
    }

    [Fact]
    public async Task RunCargoAsync_reports_the_warnings_of_sample_10_through_the_container_parser()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(10);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        var result = await Workbench.RunCargoAsync(container, CargoCommand.Build, null, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.WarningCount.Should().BeGreaterThanOrEqualTo(20);
        result.BuildScriptWarnings.Should().NotBeEmpty();
        result.BuildScriptWarnings[0].Message.Should().Contain("intentional build-script warning");
        result.Warnings.SelectMany(w => w.Spans).Should().Contain(s => s.FileName.StartsWith("src/", StringComparison.Ordinal));
        result.WorkingDirectory.Should().Be("/work");
        result.CommandLine.Should().StartWith("cargo build --message-format=json");
    }

    [Fact]
    public async Task NetworkDisabled_container_cannot_reach_a_host_but_builds_offline_from_the_shared_cache()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(2);
        var client = Workbench.Client;
        await using (var networked = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken))
        {
            await Workbench.CopySourceAsync(networked, sandbox.Directory, null, null, TestContext.Current.CancellationToken);
            var fetch = await Workbench.RunCargoAsync(networked, CargoCommand.Fetch, new CargoBuildOptions { Locked = true }, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);
            fetch.Success.Should().BeTrue(fetch.StandardError);
        }

        await using var isolated = await Workbench.StartContainerAsync(_fixture.BaseImage, new BuildContainerOptions { NetworkDisabled = true }, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(isolated, sandbox.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        var lookup = await ContainerCommandRunner.RunAsync(client, isolated.Id, new[] { "sh", "-c", "cat /etc/resolv.conf; getent hosts crates.io" }, "/", null, TestContext.Current.CancellationToken);
        var interfaces = await ContainerCommandRunner.RunAsync(client, isolated.Id, new[] { "ls", "/sys/class/net" }, "/", null, TestContext.Current.CancellationToken);
        var build = await Workbench.RunCargoAsync(isolated, CargoCommand.Build, new CargoBuildOptions { Locked = true, Offline = true }, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);

        //Assert
        lookup.Succeeded.Should().BeFalse("a network-less container must not resolve crates.io: " + lookup.StandardOutput);
        interfaces.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Should().Equal("lo");
        build.Success.Should().BeTrue(build.StandardError);
        build.RunnableArtifacts.Should().Contain(a => a.TargetName == "test_project_02");
    }

    [Fact]
    public async Task BuildAsync_license_gate_throws_for_the_gpl_member_of_sample_04()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Options = new CargoBuildOptions { LicensePolicy = StrictPermissive() },
        };

        //Act
        var act = () => Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<LicensePolicyViolationException>()).Which;
        exception.Report.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
    }

    [Fact]
    public async Task BuildAsync_license_gate_returns_a_failed_result_when_violations_do_not_throw()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Options = new CargoBuildOptions { LicensePolicy = StrictPermissive(), ThrowOnLicenseViolation = false },
        };

        //Act
        var result = await Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.FailedStage.Should().Be(BuildStage.AuditingLicenses);
        result.LicenseAudit.Passed.Should().BeFalse();
        result.LicenseAudit.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        var target = result.Results[_fixture.NativeTarget];
        target.LicenseGateFailed.Should().BeTrue();
        target.ExitCode.Should().Be(CargoCommandResult.LicenseGateFailedExitCode);
        target.LicenseAudit.Should().BeSameAs(result.LicenseAudit);
        result.StageDurations.Keys.Should().NotContain(BuildStage.Building);
        result.ContainerRemoved.Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_license_gate_report_only_builds_sample_04_and_reports_the_failed_audit()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Options = new CargoBuildOptions { LicensePolicy = StrictPermissive(), LicenseGateMode = LicenseGateMode.ReportOnly },
        };
        var reports = new List<ContainerBuildProgress>();
        var progress = new SynchronousProgress<ContainerBuildProgress>(reports.Add);

        //Act
        var result = await Workbench.BuildAsync(request, progress, TestContext.Current.CancellationToken);
        LogBuild("build 04 (gate report only)", result);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.FailedStage.Should().BeNull();
        result.LicenseAudit.Passed.Should().BeFalse();
        result.LicenseAudit.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        var target = result.Results[_fixture.NativeTarget];
        target.Success.Should().BeTrue(target.StandardError);
        target.LicenseGateFailed.Should().BeFalse();
        target.LicenseAudit.Should().BeSameAs(result.LicenseAudit);
        target.RunnableArtifacts.Select(a => a.TargetName).Should().Contain("unitconv");
        result.StageDurations.Keys.Should().Contain(BuildStage.Building);
        reports.Should().Contain(p => p.Stage == BuildStage.AuditingLicenses && p.Message == "License audit failed (report only): 1 rejected, 0 unresolved");
        var finished = reports.Where(p => p.CargoProgress != null).Select(p => p.CargoProgress).Single(p => p.Phase == CargoProgressPhase.Finished);
        finished.Total.Should().NotBeNull("the total is estimated from the target's cargo metadata");
        finished.Total.Should().Be(finished.Completed, "a clean build compiles every package of the target graph");
    }

    [Fact]
    public async Task RunCargoAsync_report_only_checks_sample_04_and_estimates_the_total_without_a_policy()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);
        var gated = new List<CargoProgress>();
        var ungated = new List<CargoProgress>();
        var options = new CargoBuildOptions { LicensePolicy = StrictPermissive(), LicenseGateMode = LicenseGateMode.ReportOnly };

        //Act
        var reportOnly = await Workbench.RunCargoAsync(container, CargoCommand.Check, options, _fixture.NativeTarget, new SynchronousProgress<CargoProgress>(gated.Add), TestContext.Current.CancellationToken);
        var withoutPolicy = await Workbench.RunCargoAsync(container, CargoCommand.Check, null, _fixture.NativeTarget, new SynchronousProgress<CargoProgress>(ungated.Add), TestContext.Current.CancellationToken);

        //Assert
        reportOnly.Success.Should().BeTrue(reportOnly.StandardError);
        reportOnly.LicenseGateFailed.Should().BeFalse();
        reportOnly.LicenseAudit.Passed.Should().BeFalse();
        gated.First().Phase.Should().Be(CargoProgressPhase.Warning);
        gated.First().Message.Should().Be("License audit failed (report only): 1 rejected, 0 unresolved");
        withoutPolicy.Success.Should().BeTrue(withoutPolicy.StandardError);
        withoutPolicy.LicenseAudit.Should().BeNull();
        ungated.Should().NotBeEmpty();
        ungated.Should().OnlyContain(p => p.Total.HasValue && p.Total == gated.First().Total);
    }

    [Fact]
    public async Task BuildAsync_license_gate_passes_sample_04_with_a_crate_override()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = sandbox.Directory,
            Options = new CargoBuildOptions { LicensePolicy = StrictPermissive().Override("unitconv_report", "internal tool, never distributed") },
        };

        //Act
        var result = await Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        LogBuild("build 04 (gate passed)", result);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.LicenseAudit.Passed.Should().BeTrue();
        result.LicenseAudit.GetFinding("unitconv_report").Status.Should().Be(CrateLicenseStatus.Exempt);
        result.Results[_fixture.NativeTarget].LicenseAudit.Should().BeSameAs(result.LicenseAudit);
        result.Results[_fixture.NativeTarget].RunnableArtifacts.Select(a => a.TargetName).Should().Contain("unitconv");
    }

    [Fact]
    public async Task BuildAsync_builds_a_workspace_member_in_its_own_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);

        //Act
        var result = await Workbench.BuildAsync(new ContainerBuildRequest { ProjectDirectory = Path.Combine(sandbox.Directory, "crates", "unitconv_core") }, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Source.HostDirectory.Should().Be(sandbox.Directory);
        result.Source.ContainerDirectory.Should().Be("/work/crates/unitconv_core");
        result.Source.PackageName.Should().Be("unitconv_core");
    }

    [Fact]
    public async Task TestAsync_runs_the_tests_of_sample_05_inside_the_container()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        var result = await Workbench.TestAsync(container, null, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.TestsPassed.Should().BeGreaterThan(0);
        result.TestsFailed.Should().Be(0);
        result.TestBinaryCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task AuditLicensesAsync_and_GetMetadataAsync_run_inside_the_container()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);

        //Act
        var metadata = await Workbench.GetMetadataAsync(container, null, TestContext.Current.CancellationToken);
        var report = await Workbench.AuditLicensesAsync(container, StrictPermissive(), null, TestContext.Current.CancellationToken);

        //Assert
        metadata.WorkspaceRoot.Should().Be("/work");
        metadata.WorkspaceMembers.Should().HaveCount(3);
        report.Passed.Should().BeFalse();
        report.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
    }

    [Fact]
    public async Task BuildAsync_reports_cargo_progress_and_a_kept_container_rebuilds_sample_02_quickly()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(2);
        using var output = SampleSandbox.CreateEmpty();
        var retrieval = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.Flat };
        var containerOptions = new BuildContainerOptions { ReuseExisting = true, KeepOnDispose = true, Env = { ["CODEBRIX_TEST_RUN"] = Guid.NewGuid().ToString("N") } };
        var completed = new List<int>();
        var progress = new SynchronousProgress<ContainerBuildProgress>(p =>
        {
            if (p.CargoProgress != null)
            {
                completed.Add(p.CargoProgress.Completed);
            }
        });

        //Act
        var first = await Workbench.BuildAsync(new ContainerBuildRequest { ProjectDirectory = sandbox.Directory, ContainerOptions = containerOptions, Retrieval = retrieval }, progress, TestContext.Current.CancellationToken);
        var second = await Workbench.BuildAsync(new ContainerBuildRequest { ProjectDirectory = sandbox.Directory, ContainerOptions = containerOptions, DestroyContainerAfterBuild = true }, null, TestContext.Current.CancellationToken);
        LogBuild("build 02 (first, kept container)", first);
        LogBuild("build 02 (second, same container)", second);

        //Assert
        first.Success.Should().BeTrue(first.ToString());
        second.Success.Should().BeTrue(second.ToString());
        completed.Should().BeInAscendingOrder();
        completed.Last().Should().BeGreaterThan(completed.First());
        first.ContainerRemoved.Should().BeFalse();
        second.ContainerId.Should().Be(first.ContainerId);
        second.ContainerRemoved.Should().BeTrue();
        second.StageDurations[BuildStage.Building].Should().BeLessThan(TimeSpan.FromTicks(first.StageDurations[BuildStage.Building].Ticks / 2));
        first.Retrieval.Artifacts.Should().ContainSingle().Which.HostPath.Should().Be(Path.Combine(output.Directory, "test_project_02"));
        Directory.GetFiles(output.Directory).Should().NotContain(f => Path.GetFileName(f).Contains("_derive", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RunCargoAsync_cancelled_mid_build_leaves_no_cargo_or_rustc_running()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(2);
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var compiling = 0;
        var progress = new SynchronousProgress<CargoProgress>(p =>
        {
            if (p.Phase == CargoProgressPhase.Compiling && Interlocked.Increment(ref compiling) == 3)
            {
                cancel.Cancel();
            }
        });

        //Act
        var act = () => Workbench.RunCargoAsync(container, CargoCommand.Build, null, _fixture.NativeTarget, progress, cancel.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        var processes = await Workbench.ExecAsync(container, new[] { "ps", "-eo", "stat=,comm=" }, TestContext.Current.CancellationToken);
        var alive = processes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => !line.StartsWith('Z'))
            .Select(line => line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries).Last())
            .ToList();
        alive.Should().NotContain(name => name == "cargo" || name == "rustc" || name.StartsWith("build-script", StringComparison.Ordinal) || name == "cc" || name == "ld");
        compiling.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task BuildAsync_builds_a_zip_archive_of_sample_05()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        using var archives = SampleSandbox.CreateEmpty();
        var archive = Path.Combine(archives.Directory, "test_project_05.zip");
        ZipFile.CreateFromDirectory(sandbox.Directory, archive, CompressionLevel.Fastest, includeBaseDirectory: true);

        //Act
        var result = await Workbench.BuildAsync(new ContainerBuildRequest { ArchivePath = archive }, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.Source.Kind.Should().Be(ContainerBuildSourceKind.Archive);
        result.Source.ArchivePath.Should().Be(archive);
        result.Results[_fixture.NativeTarget].RunnableArtifacts.Should().ContainSingle().Which.TargetName.Should().Be("textkit");
        Directory.Exists(result.Source.HostDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_removes_the_container_by_default()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(13);

        //Act
        var result = await Workbench.BuildAsync(new ContainerBuildRequest { ProjectDirectory = sandbox.Directory }, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString());
        result.ContainerRemoved.Should().BeTrue();
        var inspect = () => Workbench.Client.Containers.InspectAsync(result.ContainerId, TestContext.Current.CancellationToken);
        await inspect.Should().ThrowAsync<DockerContainerNotFoundException>();
    }

    [Fact]
    public async Task RetrieveArtifactsAsync_lays_out_the_cdylib_of_sample_13()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);
        var build = await Workbench.RunCargoAsync(container, CargoCommand.Build, null, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);
        ArtifactRetrievalOptions Libraries(ArtifactLayout layout, string folder)
            => new ArtifactRetrievalOptions { HostDirectory = Path.Combine(output.Directory, folder), Layout = layout, IncludeExecutables = false };

        //Act
        var flat = await Workbench.RetrieveArtifactsAsync(container, build.Artifacts, Libraries(ArtifactLayout.Flat, "flat"), _fixture.NativeTarget, TestContext.Current.CancellationToken);
        var byTarget = await Workbench.RetrieveArtifactsAsync(container, build.Artifacts, Libraries(ArtifactLayout.ByTarget, "by-target"), _fixture.NativeTarget, TestContext.Current.CancellationToken);
        var nuget = await Workbench.RetrieveArtifactsAsync(container, build.Artifacts, Libraries(ArtifactLayout.NuGetRuntimes, "nuget"), _fixture.NativeTarget, TestContext.Current.CancellationToken);
        var everything = await Workbench.RetrieveArtifactsAsync(container, build.Artifacts,
            new ArtifactRetrievalOptions { HostDirectory = Path.Combine(output.Directory, "all"), IncludeRlibs = true }, _fixture.NativeTarget, TestContext.Current.CancellationToken);

        //Assert
        build.Success.Should().BeTrue(build.StandardError);
        Relative(flat).Should().Equal("libtest_project_13.so");
        Relative(byTarget).Should().Equal(_fixture.Engine.NativeLinuxTriple + "/libtest_project_13.so");
        Relative(nuget).Should().Equal("runtimes/" + _fixture.Engine.NativeLinuxRuntimeIdentifier + "/native/libtest_project_13.so");
        nuget.Artifacts[0].Kind.Should().Be(ArtifactKind.SharedLibrary);
        nuget.Artifacts[0].Sha256.Should().Be(flat.Artifacts[0].Sha256);
        everything.Artifacts.Select(a => a.Kind).Should().BeEquivalentTo(new[] { ArtifactKind.SharedLibrary, ArtifactKind.Rlib, ArtifactKind.Executable });
    }

    [Fact]
    public async Task RetrieveArtifactsAsync_refuses_to_overwrite_when_told_not_to()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(13);
        using var output = SampleSandbox.CreateEmpty();
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        await Workbench.CopySourceAsync(container, sandbox.Directory, null, null, TestContext.Current.CancellationToken);
        var build = await Workbench.RunCargoAsync(container, CargoCommand.Build, null, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);
        output.WriteFile("libtest_project_13.so", "existing");
        var options = new ArtifactRetrievalOptions { HostDirectory = output.Directory, Layout = ArtifactLayout.Flat, IncludeExecutables = false, Overwrite = false };

        //Act
        var act = () => Workbench.RetrieveArtifactsAsync(container, build.Artifacts, options, _fixture.NativeTarget, TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ArtifactRetrievalException>()).Which.ContainerPath.Should().Be("/work/target/debug/libtest_project_13.so");
        File.ReadAllText(Path.Combine(output.Directory, "libtest_project_13.so")).Should().Be("existing");
    }

    private static IReadOnlyList<string> Relative(ArtifactRetrievalResult result)
        => result.Artifacts.Select(a => Path.GetRelativePath(result.HostDirectory, a.HostPath).Replace('\\', '/')).ToList();

    [Fact]
    public async Task CopySourceIntoAsync_fills_one_sub_folder_and_leaves_the_rest_of_work_alone()
    {
        //Arrange
        using var project = SampleSandbox.FromSample(5);
        using var tool = SampleSandbox.CreateEmpty();
        tool.WriteFile("Cargo.toml", "[package]\nname = \"tool\"\nversion = \"0.1.0\"\nedition = \"2021\"\n");
        tool.WriteFile("src/main.rs", "fn main() {}\n");
        tool.WriteFile("old.txt", "first copy");
        await using var container = await Workbench.StartContainerAsync(_fixture.BaseImage, null, TestContext.Current.CancellationToken);
        var transport = new DockerCopyTransport(Workbench.Client, container.Id);
        await Workbench.CopySourceIntoAsync(container, tool.Directory, "tools/x", null, null, TestContext.Current.CancellationToken);
        File.Delete(Path.Combine(tool.Directory, "old.txt"));

        //Act
        await Workbench.CopySourceAsync(container, project.Directory, null, null, TestContext.Current.CancellationToken);
        var lost = await transport.StatAsync("/work/tools/x/Cargo.toml", TestContext.Current.CancellationToken);
        await Workbench.CopySourceIntoAsync(container, tool.Directory, "tools/x", null, null, TestContext.Current.CancellationToken);
        var copy = await Workbench.CopySourceIntoAsync(container, tool.Directory, "./.hidden/x", null, null, TestContext.Current.CancellationToken);
        var build = await Workbench.RunCargoAsync(container, CargoCommand.Build, new CargoBuildOptions { WorkingDirectory = "tools/x" }, _fixture.NativeTarget, null, TestContext.Current.CancellationToken);

        //Assert
        lost.Exists.Should().BeFalse();
        copy.ContainerDirectory.Should().Be("/work/.hidden/x");
        (await transport.StatAsync("/work/Cargo.toml", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/vendor/licensed_by_file/LICENSE.txt", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/tools/x/Cargo.toml", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        (await transport.StatAsync("/work/tools/x/old.txt", TestContext.Current.CancellationToken)).Exists.Should().BeFalse();
        (await transport.StatAsync("/work/.hidden/x/src/main.rs", TestContext.Current.CancellationToken)).Exists.Should().BeTrue();
        build.Success.Should().BeTrue(build.StandardError);
        build.WorkingDirectory.Should().Be("/work/tools/x");
    }

    [Fact]
    public async Task BuildAsync_copies_the_copy_root_and_applies_the_targets_own_environment()
    {
        //Arrange
        using var stage = SampleSandbox.CreateEmpty();
        stage.WriteFile("crate/Cargo.toml", "[package]\nname = \"staged_app\"\nversion = \"0.1.0\"\nedition = \"2021\"\n\n[dependencies]\nhelper = { path = \"../vendor/helper\" }\n\n[lints.rust]\nunexpected_cfgs = \"allow\"\n");
        stage.WriteFile("crate/src/main.rs", "#[cfg(not(codebrix_probe))]\ncompile_error!(\"the target environment did not reach rustc\");\n\nfn main() {\n    println!(\"{}\", helper::answer());\n}\n");
        stage.WriteFile("vendor/helper/Cargo.toml", "[package]\nname = \"helper\"\nversion = \"0.1.0\"\nedition = \"2021\"\n");
        stage.WriteFile("vendor/helper/src/lib.rs", "pub fn answer() -> i32 { 42 }\n");
        var triple = CrossTargetInfo.Get(_fixture.NativeTarget).Triple;
        var request = new ContainerBuildRequest
        {
            ProjectDirectory = Path.Combine(stage.Directory, "crate"),
            CopyRootDirectory = stage.Directory,
            Targets = new List<CrossTarget> { CrossTarget.LinuxNative },
            ContainerOptions = new BuildContainerOptions { NetworkDisabled = true },
            Options = new CargoBuildOptions { Offline = true },
        };
        request.TargetEnvironment[CrossTarget.LinuxNative] = new Dictionary<string, string>
        {
            ["CARGO_TARGET_" + triple.ToUpperInvariant().Replace('-', '_') + "_RUSTFLAGS"] = "--cfg codebrix_probe",
        };
        var withoutCopyRoot = request.Clone();
        withoutCopyRoot.CopyRootDirectory = null;

        //Act
        var result = await Workbench.BuildAsync(request, null, TestContext.Current.CancellationToken);
        var failed = await Workbench.BuildAsync(withoutCopyRoot, null, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.ToString() + string.Join("\n", result.Results.Values.Select(r => r.StandardError)));
        result.Source.HostDirectory.Should().Be(stage.Directory);
        result.Source.ContainerDirectory.Should().Be("/work/crate");
        failed.Success.Should().BeFalse();
        failed.Source.ContainerDirectory.Should().Be("/work");
        LogBuild(nameof(BuildAsync_copies_the_copy_root_and_applies_the_targets_own_environment), result);
    }
}
