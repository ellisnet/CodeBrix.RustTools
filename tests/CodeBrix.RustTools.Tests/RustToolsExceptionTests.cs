using System;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

public class RustToolsExceptionTests
{
    [Fact]
    public void every_library_exception_derives_from_RustToolsException()
    {
        //Arrange
        var exceptions = new Exception[]
        {
            new ToolchainNotFoundException("missing"),
            new CargoCommandException("failed"),
            new LicensePolicyViolationException("rejected"),
            new SourceArchiveException("bad archive"),
        };

        //Assert
        exceptions.Should().OnlyContain(e => e is RustToolsException);
    }

    [Fact]
    public void ToolchainNotFoundException_carries_readiness_and_guidance()
    {
        //Arrange
        var info = TestToolchains.With(OperatingSystemKind.MacOS, InstalledTool.Missing(RustTool.Cargo, "x"));
        var readiness = ToolchainReadiness.Evaluate(info);
        var guidance = InstallGuidanceProvider.ForReadiness(readiness);

        //Act
        var exception = new ToolchainNotFoundException("cargo is missing", readiness, guidance);

        //Assert
        exception.Readiness.Should().BeSameAs(readiness);
        exception.Guidance.Should().NotBeEmpty();
        new ToolchainNotFoundException("plain").Guidance.Should().BeEmpty();
    }

    [Fact]
    public void CargoCommandException_records_exit_code_and_stderr()
    {
        //Act
        var exception = new CargoCommandException("clippy is not installed", 1, "error: 'cargo-clippy' is not installed", null);

        //Assert
        exception.ExitCode.Should().Be(1);
        exception.StandardError.Should().Contain("cargo-clippy");
        exception.Guidance.Should().BeEmpty();
    }

    [Fact]
    public void LicensePolicyViolationException_uses_the_message_as_summary_by_default()
    {
        //Act
        var exception = new LicensePolicyViolationException("2 crates rejected");

        //Assert
        exception.Summary.Should().Be("2 crates rejected");
        new LicensePolicyViolationException("short", "long summary").Summary.Should().Be("long summary");
    }

    [Fact]
    public void SourceArchiveException_records_the_archive_path()
        => new SourceArchiveException("entry escapes", "/tmp/a.zip", null).ArchivePath.Should().Be("/tmp/a.zip");
}
