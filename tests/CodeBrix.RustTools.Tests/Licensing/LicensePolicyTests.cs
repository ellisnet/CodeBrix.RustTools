using System;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Licensing;

public class LicensePolicyTests
{
    [Fact]
    public void Permissive_contains_the_documented_licenses()
    {
        //Act
        var policy = LicensePolicy.Permissive();

        //Assert
        policy.AllowedLicenses.Should().HaveCount(13);
        policy.AllowedLicenses.Should().Contain(OpenSourceLicense.Unicode30);
        policy.AllowedLicenses.Should().Contain(OpenSourceLicense.ZeroBsd);
        policy.AllowedLicenses.Should().NotContain(OpenSourceLicense.Mpl20);
        policy.UnresolvedLicenseHandling.Should().Be(UnresolvedLicenseHandling.Reject);
        policy.ExemptWorkspaceMembers.Should().BeTrue();
    }

    [Fact]
    public void PermissiveAndWeakCopyleft_adds_mpl_and_lgpl()
    {
        //Act
        var policy = LicensePolicy.PermissiveAndWeakCopyleft();

        //Assert
        policy.IsAllowed(OpenSourceLicense.Mpl20).Should().BeTrue();
        policy.IsAllowed(OpenSourceLicense.Lgpl21OrLater).Should().BeTrue();
        policy.IsAllowed(OpenSourceLicense.Mit).Should().BeTrue();
        policy.IsAllowed(OpenSourceLicense.Gpl30Only).Should().BeFalse();
    }

    [Fact]
    public void Allow_is_fluent_and_adds_licenses()
    {
        //Act
        var policy = new LicensePolicy().Allow(OpenSourceLicense.Mit).Allow(OpenSourceLicense.Gpl30Only);

        //Assert
        policy.AllowedLicenses.Should().HaveCount(2);
        policy.IsAllowed(OpenSourceLicense.Gpl30Only).Should().BeTrue();
    }

    [Fact]
    public void IsAllowed_accepts_or_later_when_a_later_version_is_allowed()
    {
        //Arrange
        var policy = new LicensePolicy().Allow(OpenSourceLicense.Gpl30Only);

        //Assert
        policy.IsAllowed(OpenSourceLicense.Gpl20OrLater).Should().BeTrue();
        policy.IsAllowed(OpenSourceLicense.Gpl20Only).Should().BeFalse();
    }

    [Fact]
    public void Override_and_Clone_copy_every_setting()
    {
        //Arrange
        var policy = LicensePolicy.Permissive().Override("ring", "reviewed by legal").AllowException("LLVM-exception");
        policy.UnresolvedLicenseHandling = UnresolvedLicenseHandling.Warn;
        policy.ExemptWorkspaceMembers = false;

        //Act
        var copy = policy.Clone();
        copy.Allow(OpenSourceLicense.Mpl20);

        //Assert
        copy.CrateOverrides["RING"].Should().Be("reviewed by legal");
        copy.AllowedExceptions.Should().Contain("llvm-exception");
        copy.UnresolvedLicenseHandling.Should().Be(UnresolvedLicenseHandling.Warn);
        copy.ExemptWorkspaceMembers.Should().BeFalse();
        policy.IsAllowed(OpenSourceLicense.Mpl20).Should().BeFalse();
    }

    [Fact]
    public void Override_rejects_a_blank_crate_name()
    {
        //Arrange
        Action act = () => new LicensePolicy().Override(" ", "x");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_lists_the_allowed_identifiers()
        => new LicensePolicy().Allow(OpenSourceLicense.Mit, OpenSourceLicense.Apache20).ToString().Should().StartWith("Allowed: Apache-2.0, MIT");
}
