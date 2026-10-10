using System.Text.Json;
using CodeBrix.RustTools.Building;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoJsonMessageParserTests
{
    [Theory]
    [InlineData("error", DiagnosticLevel.Error)]
    [InlineData("warning", DiagnosticLevel.Warning)]
    [InlineData("note", DiagnosticLevel.Note)]
    [InlineData("help", DiagnosticLevel.Help)]
    [InlineData("failure-note", DiagnosticLevel.FailureNote)]
    [InlineData("error: internal compiler error", DiagnosticLevel.Ice)]
    [InlineData("something-new", DiagnosticLevel.Note)]
    public void ParseLevel_maps_rustc_levels(string level, DiagnosticLevel expected)
        => CargoJsonMessageParser.ParseLevel(level).Should().Be(expected);

    [Fact]
    public void ParseArtifact_reads_a_release_test_artifact_with_text_debuginfo()
    {
        //Arrange
        using var document = JsonDocument.Parse("{\"reason\":\"compiler-artifact\",\"package_id\":\"registry+https://github.com/rust-lang/crates.io-index#serde@1.0.228\",\"manifest_path\":\"/c/serde/Cargo.toml\",\"target\":{\"kind\":[\"lib\"],\"crate_types\":[\"lib\"],\"name\":\"serde\"},\"profile\":{\"opt_level\":\"3\",\"debuginfo\":\"line-tables-only\",\"debug_assertions\":false,\"overflow_checks\":false,\"test\":true},\"features\":[\"derive\",\"std\"],\"filenames\":[\"/t/libserde.rlib\",\"/t/libserde.rmeta\"],\"executable\":null,\"fresh\":true}");

        //Act
        var artifact = CargoJsonMessageParser.ParseArtifact(document.RootElement);

        //Assert
        artifact.PackageName.Should().Be("serde");
        artifact.PackageVersion.Should().Be("1.0.228");
        artifact.Executable.Should().BeNull();
        artifact.Fresh.Should().BeTrue();
        artifact.Features.Should().ContainInOrder("derive", "std");
        artifact.Filenames.Should().HaveCount(2);
        artifact.Profile.OptLevel.Should().Be("3");
        artifact.Profile.DebugInfo.Should().Be("line-tables-only");
        artifact.Profile.Test.Should().BeTrue();
        artifact.IsRunnable.Should().BeFalse();
    }

    [Fact]
    public void ParseCompilerMessage_returns_null_without_a_message()
    {
        //Arrange
        using var document = JsonDocument.Parse("{\"reason\":\"compiler-message\",\"package_id\":\"x\"}");

        //Act
        var diagnostic = CargoJsonMessageParser.ParseCompilerMessage(document.RootElement);

        //Assert
        diagnostic.Should().BeNull();
    }

    [Fact]
    public void IsSummary_is_false_for_a_real_diagnostic_with_a_code()
        => CargoJsonMessageParser.IsSummary(new CompilerDiagnostic { Message = "aborting due to 1 previous error", Code = "E0001" }).Should().BeFalse();

    [Fact]
    public void ToString_falls_back_to_level_and_message()
        => new CompilerDiagnostic { Level = DiagnosticLevel.Note, Message = "see the docs" }.ToString().Should().Be("note: see the docs");
}
