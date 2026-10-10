using System;
using System.Linq;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Licensing;

public class OpenSourceLicenseInfoTests
{
    [Fact]
    public void All_describes_every_enum_member()
    {
        //Act
        var all = OpenSourceLicenseInfo.All;

        //Assert
        all.Should().HaveCount(Enum.GetValues<OpenSourceLicense>().Length);
        all.Select(i => i.SpdxId).Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(all.Count);
        all.Should().OnlyContain(i => !string.IsNullOrWhiteSpace(i.Name));
    }

    [Theory]
    [InlineData("MIT", OpenSourceLicense.Mit)]
    [InlineData("mit", OpenSourceLicense.Mit)]
    [InlineData(" Apache-2.0 ", OpenSourceLicense.Apache20)]
    [InlineData("0BSD", OpenSourceLicense.ZeroBsd)]
    [InlineData("Unicode-3.0", OpenSourceLicense.Unicode30)]
    [InlineData("zlib-acknowledgement", OpenSourceLicense.ZlibAcknowledgement)]
    [InlineData("BlueOak-1.0.0", OpenSourceLicense.BlueOak100)]
    [InlineData("GPL-2.0", OpenSourceLicense.Gpl20Only)]
    [InlineData("GPL-2.0+", OpenSourceLicense.Gpl20OrLater)]
    [InlineData("GPL-3.0", OpenSourceLicense.Gpl30Only)]
    [InlineData("GPL-3.0+", OpenSourceLicense.Gpl30OrLater)]
    [InlineData("LGPL-2.1", OpenSourceLicense.Lgpl21Only)]
    [InlineData("LGPL-2.1+", OpenSourceLicense.Lgpl21OrLater)]
    [InlineData("LGPL-3.0", OpenSourceLicense.Lgpl30Only)]
    [InlineData("lgpl-3.0+", OpenSourceLicense.Lgpl30OrLater)]
    [InlineData("AGPL-3.0", OpenSourceLicense.Agpl30Only)]
    [InlineData("AGPL-3.0+", OpenSourceLicense.Agpl30OrLater)]
    [InlineData("BSD-2-Clause-FreeBSD", OpenSourceLicense.Bsd2Clause)]
    public void TryParse_maps_current_and_deprecated_ids(string id, OpenSourceLicense expected)
    {
        //Act
        var found = OpenSourceLicenseInfo.TryParse(id, out var license);

        //Assert
        found.Should().BeTrue();
        license.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Proprietary-Acme-1.0")]
    [InlineData("LicenseRef-Custom")]
    public void TryParse_returns_false_for_unknown_ids(string id)
        => OpenSourceLicenseInfo.TryParse(id, out _).Should().BeFalse();

    [Fact]
    public void ToSpdxId_returns_the_canonical_identifier()
    {
        //Assert
        OpenSourceLicenseInfo.ToSpdxId(OpenSourceLicense.Mit0).Should().Be("MIT-0");
        OpenSourceLicenseInfo.ToSpdxId(OpenSourceLicense.Gpl30OrLater).Should().Be("GPL-3.0-or-later");
        OpenSourceLicenseInfo.ToSpdxId(OpenSourceLicense.Cc010).Should().Be("CC0-1.0");
    }

    [Fact]
    public void Get_reports_category_and_approval_flags()
    {
        //Act
        var mpl = OpenSourceLicenseInfo.Get(OpenSourceLicense.Mpl20);
        var agpl = OpenSourceLicenseInfo.Get(OpenSourceLicense.Agpl30OrLater);
        var cc0 = OpenSourceLicenseInfo.Get(OpenSourceLicense.Cc010);

        //Assert
        mpl.Category.Should().Be(LicenseCategory.WeakCopyleft);
        mpl.IsOsiApproved.Should().BeTrue();
        mpl.IsFsfFree.Should().BeTrue();
        agpl.Category.Should().Be(LicenseCategory.NetworkCopyleft);
        agpl.IsOrLater.Should().BeTrue();
        cc0.Category.Should().Be(LicenseCategory.PublicDomain);
        cc0.IsOsiApproved.Should().BeFalse();
    }

    [Fact]
    public void Parse_throws_for_an_unknown_id()
    {
        //Arrange
        Action act = () => OpenSourceLicenseInfo.Parse("Nope-1.0");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void InCategories_filters_by_category()
    {
        //Act
        var strong = OpenSourceLicenseInfo.InCategories(LicenseCategory.StrongCopyleft);

        //Assert
        strong.Should().Contain(OpenSourceLicense.Gpl30Only);
        strong.Should().NotContain(OpenSourceLicense.Mit);
    }
}
