using System.Collections.Generic;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoCommandResultTests
{
    private static LicenseAuditReport FailedReport() => new LicenseAuditReport
    {
        Findings = new List<CrateLicenseFinding>
        {
            new CrateLicenseFinding { CrateName = "gpl_crate", Version = "1.0.0", DeclaredExpression = "GPL-3.0-only", Status = CrateLicenseStatus.Rejected, IsBlocking = true, Reason = "not allowed" },
        },
    };

    [Fact]
    public void ForFailedLicenseGate_returns_a_failed_result_carrying_the_report()
    {
        //Arrange
        var report = FailedReport();

        //Act
        var result = CargoCommandResult.ForFailedLicenseGate(CargoCommand.Build, report, "/work");

        //Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-2);
        result.LicenseAudit.Should().BeSameAs(report);
        result.LicenseGateFailed.Should().BeTrue();
        result.WorkingDirectory.Should().Be("/work");
        result.Artifacts.Should().BeEmpty();
        result.ToString().Should().Be("cargo build not run: the license audit failed (1 rejected, 0 unresolved crate(s))");
    }

    [Fact]
    public void LicenseGateFailedExitCode_is_distinct_from_a_killed_cargo()
        => CargoCommandResult.LicenseGateFailedExitCode.Should().Be(-2);

    [Fact]
    public void Readiness_is_null_when_the_workbench_did_not_evaluate_one()
        => new CargoCommandResult().Readiness.Should().BeNull();

    [Fact]
    public void ForFailedLicenseGate_returns_a_test_result_for_the_test_command()
        => CargoCommandResult.ForFailedLicenseGate(CargoCommand.Test, FailedReport(), null).Should().BeOfType<CargoTestResult>();

    [Fact]
    public void LicenseGateFailed_is_false_when_a_failed_report_only_audit_is_attached()
    {
        //Arrange
        var result = new CargoCommandResult { Success = true };

        //Act
        result.LicenseAudit = FailedReport();

        //Assert
        result.LicenseAudit.Passed.Should().BeFalse();
        result.LicenseGateFailed.Should().BeFalse();
        result.ToString().Should().StartWith("cargo build succeeded");
    }

    [Fact]
    public void LicenseGateFailed_is_false_when_no_gate_ran()
        => new CargoCommandResult { Success = true }.LicenseGateFailed.Should().BeFalse();
}
