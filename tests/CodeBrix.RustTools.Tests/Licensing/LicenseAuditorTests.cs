using System;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Tests.Projects;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Licensing;

public class LicenseAuditorTests
{
    [Fact]
    public void Audit_approves_rejects_and_exempts_synthetic_packages()
    {
        //Arrange
        var app = TestMetadata.Local("app", "0.1.0");
        var metadata = TestMetadata.Graph(
            new[] { app },
            new[]
            {
                TestMetadata.Registry("itoa", "1.0.18", "MIT OR Apache-2.0"),
                TestMetadata.Registry("gpl-thing", "2.0.0", "GPL-3.0-only"),
                TestMetadata.Registry("unicode-ident", "1.0.26", "(MIT OR Apache-2.0) AND Unicode-3.0"),
            });

        //Act
        var report = LicenseAuditor.Audit(metadata, LicensePolicy.Permissive());

        //Assert
        report.Findings.Select(f => f.CrateName).Should().ContainInOrder("app", "gpl-thing", "itoa", "unicode-ident");
        report.GetFinding("app").Status.Should().Be(CrateLicenseStatus.Exempt);
        report.GetFinding("app").IsWorkspaceMember.Should().BeTrue();
        report.GetFinding("itoa").Status.Should().Be(CrateLicenseStatus.Approved);
        report.GetFinding("itoa").ParsedExpression.Kind.Should().Be(SpdxExpressionKind.Or);
        report.GetFinding("unicode-ident").Status.Should().Be(CrateLicenseStatus.Approved);
        report.GetFinding("gpl-thing").Status.Should().Be(CrateLicenseStatus.Rejected);
        report.GetFinding("gpl-thing").Source.Should().Be(TestMetadata.CratesIo);
        report.Passed.Should().BeFalse();
        report.RejectedCount.Should().Be(1);
        report.ApprovedCount.Should().Be(2);
        report.ExemptCount.Should().Be(1);
    }

    [Fact]
    public void Audit_gives_each_unresolved_kind_a_distinct_reason()
    {
        //Arrange
        var metadata = TestMetadata.Graph(
            new[] { TestMetadata.Local("app", "0.1.0", "MIT") },
            new[]
            {
                TestMetadata.Local("none", "0.1.0"),
                TestMetadata.Local("file-only", "0.1.0", licenseFile: "LICENSE.txt"),
                TestMetadata.Local("unknown", "0.1.0", "Proprietary-Acme-1.0"),
                TestMetadata.Local("garbage", "0.1.0", "see the LICENSE file"),
            });

        //Act
        var report = LicenseAuditor.Audit(metadata, LicensePolicy.Permissive());

        //Assert
        report.UnresolvedCount.Should().Be(4);
        report.GetFinding("none").Reason.Should().Contain("neither");
        report.GetFinding("file-only").Reason.Should().Contain("license-file");
        report.GetFinding("file-only").LicenseFile.Should().EndWith("LICENSE.txt");
        report.GetFinding("unknown").Reason.Should().Contain("unknown license identifier");
        report.GetFinding("garbage").Reason.Should().Contain("could not be parsed");
        report.GetFinding("garbage").ParsedExpression.Should().BeNull();
        report.Unresolved.Select(f => f.Reason).Distinct().Count().Should().Be(4);
        report.Passed.Should().BeFalse();
    }

    [Theory]
    [InlineData(UnresolvedLicenseHandling.Reject, false, 0)]
    [InlineData(UnresolvedLicenseHandling.Warn, true, 1)]
    [InlineData(UnresolvedLicenseHandling.Allow, true, 0)]
    public void Audit_applies_the_unresolved_handling(UnresolvedLicenseHandling handling, bool passed, int warnings)
    {
        //Arrange
        var metadata = TestMetadata.Graph(
            new[] { TestMetadata.Local("app", "0.1.0") },
            new[] { TestMetadata.Local("none", "0.1.0") });
        var policy = LicensePolicy.Permissive();
        policy.UnresolvedLicenseHandling = handling;

        //Act
        var report = LicenseAuditor.Audit(metadata, policy);

        //Assert
        report.Passed.Should().Be(passed);
        report.Warnings.Should().HaveCount(warnings);
        report.GetFinding("none").Status.Should().Be(CrateLicenseStatus.Unresolved);
        report.GetFinding("none").IsBlocking.Should().Be(!passed);
    }

    [Fact]
    public void Audit_turns_any_status_into_exempt_for_a_crate_override()
    {
        //Arrange
        var metadata = TestMetadata.Graph(
            new[] { TestMetadata.Local("app", "0.1.0") },
            new[] { TestMetadata.Registry("gpl-thing", "2.0.0", "GPL-3.0-only"), TestMetadata.Local("none", "0.1.0") });
        var policy = LicensePolicy.Permissive().Override("gpl-thing", "used only by a dev tool").Override("none", "internal crate");

        //Act
        var report = LicenseAuditor.Audit(metadata, policy);

        //Assert
        report.Passed.Should().BeTrue();
        report.GetFinding("gpl-thing").Status.Should().Be(CrateLicenseStatus.Exempt);
        report.GetFinding("gpl-thing").Reason.Should().Contain("used only by a dev tool");
        report.ExemptCount.Should().Be(3);
    }

    [Fact]
    public void Audit_checks_workspace_members_when_not_exempt()
    {
        //Arrange
        var metadata = TestMetadata.Graph(new[] { TestMetadata.Local("app", "0.1.0", "GPL-3.0-only") }, Array.Empty<CargoMetadataPackage>());
        var policy = LicensePolicy.Permissive();
        policy.ExemptWorkspaceMembers = false;

        //Act
        var report = LicenseAuditor.Audit(metadata, policy);

        //Assert
        report.GetFinding("app").Status.Should().Be(CrateLicenseStatus.Rejected);
        report.GetFinding("app").IsWorkspaceMember.Should().BeTrue();
    }

    [Fact]
    public async Task AuditAsync_rejects_the_gpl_workspace_member_only_when_members_are_checked()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(4);
        var metadata = await TestMetadata.OfflineAsync(sandbox);
        var strict = LicensePolicy.Permissive();
        strict.ExemptWorkspaceMembers = false;

        //Act
        var exempt = LicenseAuditor.Audit(metadata, LicensePolicy.Permissive());
        var checkedMembers = LicenseAuditor.Audit(metadata, strict);

        //Assert
        exempt.Passed.Should().BeTrue();
        exempt.GetFinding("unitconv_report").Status.Should().Be(CrateLicenseStatus.Exempt);
        checkedMembers.Passed.Should().BeFalse();
        checkedMembers.Rejected.Should().ContainSingle().Which.CrateName.Should().Be("unitconv_report");
        checkedMembers.GetFinding("unitconv_core").Status.Should().Be(CrateLicenseStatus.Approved);
        checkedMembers.GetFinding("unitconv_cli").Status.Should().Be(CrateLicenseStatus.Approved);
    }

    [Fact]
    public async Task AuditAsync_reports_the_three_vendor_fixtures_as_unresolved()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var project = CargoProject.Open(sandbox.Directory);
        await TestMetadata.OfflineAsync(sandbox);

        //Act
        var report = await LicenseAuditor.AuditAsync(project, LicensePolicy.Permissive(), new CargoMetadataOptions { Offline = true }, TestContext.Current.CancellationToken);

        //Assert
        report.Passed.Should().BeFalse();
        report.Unresolved.Select(f => f.CrateName).Should().ContainInOrder("licensed_by_file", "no_license_at_all", "unknown_license_id");
        report.GetFinding("licensed_by_file").Reason.Should().Contain("license-file");
        report.GetFinding("no_license_at_all").Reason.Should().Contain("neither");
        report.GetFinding("unknown_license_id").Reason.Should().Contain("Proprietary-Acme-1.0");
        report.GetFinding("test_project_05").Status.Should().Be(CrateLicenseStatus.Exempt);
        report.ToSummaryText().Should().Contain("UNRESOLVED");
    }

    [Fact]
    public async Task AuditAsync_flags_the_transitive_mpl_crate_unless_weak_copyleft_is_allowed()
    {
        //Arrange
        using var sandbox = SampleSandbox.FromSample(7);
        var metadata = await TestMetadata.OfflineAsync(sandbox);

        //Act
        var permissive = LicenseAuditor.Audit(metadata, LicensePolicy.Permissive());
        var weak = LicenseAuditor.Audit(metadata, LicensePolicy.PermissiveAndWeakCopyleft());

        //Assert
        permissive.Passed.Should().BeFalse();
        var optionExt = permissive.Rejected.Should().ContainSingle().Which;
        optionExt.CrateName.Should().Be("option-ext");
        optionExt.DeclaredExpression.Should().Be("MPL-2.0");
        weak.Passed.Should().BeTrue();
        weak.GetFinding("option-ext").Status.Should().Be(CrateLicenseStatus.Approved);
    }

    [Fact]
    public async Task AuditAsync_rejects_no_dependencies_metadata_options()
    {
        //Arrange
        var project = CargoProject.Open(RepoPaths.Sample(6));

        //Act
        Func<Task> act = () => LicenseAuditor.AuditAsync(project, LicensePolicy.Permissive(), new CargoMetadataOptions { NoDependencies = true }, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void Audit_throws_for_null_arguments()
    {
        //Arrange
        Action noMetadata = () => LicenseAuditor.Audit(null, LicensePolicy.Permissive());
        Action noPolicy = () => LicenseAuditor.Audit(new CargoMetadata(), null);

        //Assert
        noMetadata.Should().Throw<ArgumentNullException>();
        noPolicy.Should().Throw<ArgumentNullException>();
    }
}
