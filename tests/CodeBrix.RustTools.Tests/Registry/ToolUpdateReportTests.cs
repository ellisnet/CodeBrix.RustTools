using System;
using CodeBrix.RustTools.Registry;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Registry;

public class ToolUpdateReportTests
{
    private static RustReleaseInfo Release_1_99() => new RustReleaseInfo
    {
        RustcVersion = ToolVersion.Parse("1.99.0 (b940084d7 2026-09-28)"),
        CargoPackageVersion = ToolVersion.Parse("0.100.0"),
        ClippyVersion = ToolVersion.Parse("0.1.99"),
        RustfmtVersion = ToolVersion.Parse("1.10.0"),
    };

    [Fact]
    public void Create_flags_outdated_tools_and_adds_update_guidance()
    {
        //Act
        var report = ToolUpdateReport.Create(TestToolchains.Complete(), Release_1_99(), new ToolVersion(1, 29, 1));

        //Assert
        report.AnyUpdateAvailable.Should().BeTrue();
        report.GetCheck(RustTool.Rustc).Status.Should().Be(ToolStatus.Outdated);
        report.GetCheck(RustTool.Cargo).Latest.ToString().Should().Be("1.99.0");
        report.GetCheck(RustTool.Rustup).IsUpdateAvailable.Should().BeTrue();
        report.GetCheck(RustTool.Clippy).IsUpdateAvailable.Should().BeTrue();
        report.GetCheck(RustTool.Rustfmt).IsUpdateAvailable.Should().BeTrue();
        report.OutdatedTools.Should().HaveCount(5);
        report.Guidance.Should().ContainSingle().Which.Commands.Should().Contain("rustup update");
    }

    [Fact]
    public void Create_reports_current_tools_without_guidance()
    {
        //Arrange
        var release = new RustReleaseInfo
        {
            RustcVersion = ToolVersion.Parse("1.94.1"),
            ClippyVersion = ToolVersion.Parse("0.1.94"),
            RustfmtVersion = ToolVersion.Parse("1.8.0"),
        };

        //Act
        var report = ToolUpdateReport.Create(TestToolchains.Complete(), release, new ToolVersion(1, 29, 0));

        //Assert
        report.AnyUpdateAvailable.Should().BeFalse();
        report.Guidance.Should().BeEmpty();
        report.ToString().Should().StartWith("No updates found.");
    }

    [Fact]
    public void Create_treats_a_newer_nightly_as_current()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.MacOS, TestToolchains.Installed(RustTool.Rustc, "rustc 1.100.0-nightly (abc123 2026-10-05)"));

        //Act
        var report = ToolUpdateReport.Create(info, Release_1_99(), null);

        //Assert
        report.GetCheck(RustTool.Rustc).Status.Should().Be(ToolStatus.Installed);
        report.GetCheck(RustTool.Rustup).Status.Should().Be(ToolStatus.Unknown);
    }

    [Fact]
    public void Create_reports_missing_tools_and_records_errors()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Rustup, "none"));

        //Act
        var report = ToolUpdateReport.Create(info, null, null, new[] { "offline" });

        //Assert
        report.GetCheck(RustTool.Rustup).Status.Should().Be(ToolStatus.Missing);
        report.GetCheck(RustTool.Rustc).Status.Should().Be(ToolStatus.Unknown);
        report.Errors.Should().ContainSingle().Which.Should().Be("offline");
        report.AnyUpdateAvailable.Should().BeFalse();
    }

    [Fact]
    public void Create_suggests_rustup_when_an_outdated_rust_is_not_rustup_managed()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Rustup, "none"));

        //Act
        var report = ToolUpdateReport.Create(info, Release_1_99(), null);

        //Assert
        report.Guidance.Should().ContainSingle().Which.Topic.Should().Be(InstallGuidanceTopic.InstallRustup);
    }

    [Fact]
    public void Create_rejects_a_null_toolchain()
    {
        //Arrange
        Action act = () => ToolUpdateReport.Create(null, null, null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
