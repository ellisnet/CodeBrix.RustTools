using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Tests.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// The license gate of <see cref="RustWorkbench"/>: cargo metadata + audit before cargo runs.
/// Every test works on a sandboxed copy of a sample and runs cargo metadata offline (skipping
/// when the local registry cache cannot satisfy the graph).
/// </summary>
public class RustWorkbenchLicenseGateTests
{
    private static LicensePolicy StrictPermissive()
    {
        var policy = LicensePolicy.Permissive();
        policy.ExemptWorkspaceMembers = false;
        return policy;
    }

    [Fact]
    public async Task BuildAsync_throws_when_the_gpl_member_of_sample_04_is_checked()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = StrictPermissive() };

        //Act
        var act = () => workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<LicensePolicyViolationException>()).Which;
        exception.Report.Should().NotBeNull();
        exception.Report.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        exception.Summary.Should().Contain("unitconv_report");
        exception.Message.Should().Contain("cargo build was not run");
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }

    [Fact]
    public async Task BuildAsync_returns_a_failed_result_when_violations_do_not_throw()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = StrictPermissive(), ThrowOnLicenseViolation = false };

        //Act
        var result = await workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.LicenseGateFailed.Should().BeTrue();
        result.ExitCode.Should().Be(CargoCommandResult.LicenseGateFailedExitCode);
        result.Artifacts.Should().BeEmpty();
        result.LicenseAudit.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        result.ToString().Should().Contain("not run");
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }

    [Fact]
    public async Task TestAsync_returns_a_failed_test_result_when_violations_do_not_throw()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = StrictPermissive(), ThrowOnLicenseViolation = false };

        //Act
        var result = await workbench.TestAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.LicenseGateFailed.Should().BeTrue();
        result.ExitCode.Should().Be(-2);
        result.Readiness.Should().NotBeNull();
        result.Command.Should().Be(CargoCommand.Test);
        result.TestsPassed.Should().Be(0);
    }

    [Fact]
    public async Task BuildAsync_passes_the_gate_of_sample_04_with_a_crate_override()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var policy = StrictPermissive().Override("unitconv_report", "internal tool, never distributed");
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy };

        //Act
        var result = await workbench.BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.LicenseGateFailed.Should().BeFalse();
        result.LicenseAudit.Passed.Should().BeTrue();
        result.LicenseAudit.GetFinding("unitconv_report").Status.Should().Be(CrateLicenseStatus.Exempt);
        result.RunnableArtifacts.Select(a => a.TargetName).Should().Contain("unitconv");
    }

    [Fact]
    public async Task CheckAsync_blocks_sample_05_when_unresolved_licenses_are_rejected()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var policy = LicensePolicy.Permissive();
        policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Reject;
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy, ThrowOnLicenseViolation = false };

        //Act
        var result = await workbench.CheckAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.LicenseGateFailed.Should().BeTrue();
        result.LicenseAudit.Unresolved.Select(f => f.CrateName).Should().ContainInOrder("licensed_by_file", "no_license_at_all", "unknown_license_id");
        result.LicenseAudit.RejectedCount.Should().Be(0);
    }

    [Fact]
    public async Task BuildAsync_builds_sample_05_when_unresolved_licenses_are_allowed_and_reports_a_total()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        var metadata = await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var policy = LicensePolicy.Permissive();
        policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Allow;
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy };
        var progress = new CollectingProgress();

        //Act
        var result = await workbench.BuildAsync(project, options, progress, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.LicenseAudit.Should().NotBeNull();
        result.LicenseAudit.Passed.Should().BeTrue();
        result.LicenseAudit.UnresolvedCount.Should().Be(3);
        progress.Reports.Should().NotBeEmpty();
        metadata.ResolveNodes.Should().HaveCount(4);
        progress.Reports.Should().OnlyContain(p => p.Total == 4);
        options.ExpectedCrateCount.Should().BeNull();
    }

    [Fact]
    public async Task BuildAsync_report_only_builds_sample_04_and_attaches_the_failed_audit()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = StrictPermissive(), LicenseGateMode = LicenseGateMode.ReportOnly };
        var progress = new CollectingProgress();

        //Act
        var result = await workbench.BuildAsync(project, options, progress, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.LicenseGateFailed.Should().BeFalse();
        result.ExitCode.Should().Be(0);
        result.LicenseAudit.Should().NotBeNull();
        result.LicenseAudit.Passed.Should().BeFalse();
        result.LicenseAudit.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        result.RunnableArtifacts.Select(a => a.TargetName).Should().Contain("unitconv");
        result.Readiness.Should().NotBeNull();
        var warning = progress.Reports.First();
        warning.Phase.Should().Be(CargoProgressPhase.Warning);
        warning.Message.Should().Be("License audit failed (report only): 1 rejected, 0 unresolved");
        progress.Reports.Should().Contain(p => p.Phase == CargoProgressPhase.Finished);
    }

    [Fact]
    public async Task CheckAsync_report_only_attaches_a_passed_audit_without_a_warning()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var policy = LicensePolicy.Permissive();
        policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Allow;
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy, LicenseGateMode = LicenseGateMode.ReportOnly };
        var progress = new CollectingProgress();

        //Act
        var result = await workbench.CheckAsync(project, options, progress, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.LicenseAudit.Passed.Should().BeTrue();
        progress.Reports.Should().NotContain(p => p.Message.StartsWith("License audit failed"));
    }

    [Fact]
    public async Task TestAsync_report_only_ignores_ThrowOnLicenseViolation_and_runs_the_tests()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);
        var policy = LicensePolicy.Permissive();
        policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Reject;
        var options = new CargoBuildOptions { Offline = true, LicensePolicy = policy, LicenseGateMode = LicenseGateMode.ReportOnly, ThrowOnLicenseViolation = true };

        //Act
        var result = await workbench.TestAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.LicenseGateFailed.Should().BeFalse();
        result.LicenseAudit.UnresolvedCount.Should().Be(3);
        result.TestBinaryCount.Should().BeGreaterThan(0);
        result.TestsPassed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AuditLicensesAsync_reports_without_building()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(5);
        await TestMetadata.OfflineAsync(sandbox);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var report = await workbench.AuditLicensesAsync(project, LicensePolicy.Permissive(), new CargoMetadataOptions { Offline = true }, TestContext.Current.CancellationToken);

        //Assert
        report.Passed.Should().BeFalse();
        report.UnresolvedCount.Should().Be(3);
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }
}
