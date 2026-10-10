using System;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Tests.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Licensing;

public class LicenseAuditReportTests
{
    private static LicenseAuditReport FailingReport()
        => LicenseAuditor.Audit(
            TestMetadata.Graph(
                new[] { TestMetadata.Local("app", "0.1.0") },
                new[] { TestMetadata.Registry("gpl-thing", "2.0.0", "GPL-3.0-only"), TestMetadata.Registry("itoa", "1.0.18", "MIT") }),
            LicensePolicy.Permissive());

    [Fact]
    public void ThrowIfFailed_throws_with_the_report_attached()
    {
        //Arrange
        var report = FailingReport();

        //Act
        Action act = report.ThrowIfFailed;

        //Assert
        var exception = act.Should().Throw<LicensePolicyViolationException>().Which;
        exception.Report.Should().BeSameAs(report);
        exception.Summary.Should().Contain("gpl-thing");
        exception.Message.Should().Contain("1 rejected");
    }

    [Fact]
    public void ThrowIfFailed_does_nothing_when_the_audit_passed()
    {
        //Arrange
        var report = LicenseAuditor.Audit(
            TestMetadata.Graph(new[] { TestMetadata.Local("app", "0.1.0") }, new[] { TestMetadata.Registry("itoa", "1.0.18", "MIT") }),
            LicensePolicy.Permissive());

        //Act
        Action act = report.ThrowIfFailed;

        //Assert
        act.Should().NotThrow();
        report.Passed.Should().BeTrue();
    }

    [Fact]
    public void ToSummaryText_lists_counts_and_blocking_findings()
    {
        //Act
        var text = FailingReport().ToSummaryText();

        //Assert
        text.Should().StartWith("License audit FAILED: 3 crates - 1 approved, 1 rejected, 0 unresolved, 1 exempt.");
        text.Should().Contain("REJECTED   gpl-thing v2.0.0 (GPL-3.0-only)");
        text.Should().NotContain("itoa");
    }

    [Fact]
    public void BlockingFindings_lists_only_the_failing_crates()
        => FailingReport().BlockingFindings.Should().ContainSingle().Which.CrateName.Should().Be("gpl-thing");
}
