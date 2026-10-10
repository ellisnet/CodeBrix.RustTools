using System;
using System.Linq;
using CodeBrix.RustTools.Licensing;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Licensing;

public class SpdxLicenseExpressionTests
{
    [Theory]
    [InlineData("MIT", "MIT")]
    [InlineData("mit or apache-2.0", "MIT OR Apache-2.0")]
    [InlineData("MIT OR Apache-2.0", "MIT OR Apache-2.0")]
    [InlineData("Apache-2.0 WITH LLVM-exception", "Apache-2.0 WITH LLVM-exception")]
    [InlineData("(MIT OR Apache-2.0) AND Unicode-3.0", "(MIT OR Apache-2.0) AND Unicode-3.0")]
    [InlineData("MIT/Apache-2.0", "MIT OR Apache-2.0")]
    [InlineData("MIT / Apache-2.0", "MIT OR Apache-2.0")]
    [InlineData("GPL-3.0+", "GPL-3.0-or-later")]
    [InlineData("GPL-3.0-only+", "GPL-3.0-or-later")]
    [InlineData("Apache-2.0+", "Apache-2.0+")]
    [InlineData("BSD-3-Clause OR MIT OR Apache-2.0", "BSD-3-Clause OR MIT OR Apache-2.0")]
    [InlineData("((MIT))", "MIT")]
    [InlineData("MIT AND (Apache-2.0 AND Zlib)", "MIT AND Apache-2.0 AND Zlib")]
    [InlineData("Unlicense OR MIT", "Unlicense OR MIT")]
    [InlineData("GPL-2.0-or-later with classpath-exception-2.0", "GPL-2.0-or-later WITH Classpath-exception-2.0")]
    public void Parse_produces_the_canonical_text(string text, string canonical)
        => SpdxLicenseExpression.Parse(text).ToString().Should().Be(canonical);

    [Fact]
    public void Parse_builds_an_or_node_with_license_operands()
    {
        //Act
        var expression = SpdxLicenseExpression.Parse("MIT OR Apache-2.0");

        //Assert
        expression.Kind.Should().Be(SpdxExpressionKind.Or);
        expression.Operands.Should().HaveCount(2);
        expression.Operands[0].License.Should().Be(OpenSourceLicense.Mit);
        expression.Operands[1].License.Should().Be(OpenSourceLicense.Apache20);
        expression.Licenses.Should().ContainInOrder(OpenSourceLicense.Mit, OpenSourceLicense.Apache20);
        expression.UnknownIdentifiers.Should().BeEmpty();
    }

    [Fact]
    public void Parse_binds_and_tighter_than_or()
    {
        //Act
        var expression = SpdxLicenseExpression.Parse("MIT AND Zlib OR Apache-2.0");

        //Assert
        expression.Kind.Should().Be(SpdxExpressionKind.Or);
        expression.Operands[0].Kind.Should().Be(SpdxExpressionKind.And);
        expression.Operands[1].License.Should().Be(OpenSourceLicense.Apache20);
    }

    [Fact]
    public void Parse_reads_a_with_exception()
    {
        //Act
        var expression = SpdxLicenseExpression.Parse("Apache-2.0 WITH LLVM-exception");

        //Assert
        expression.Kind.Should().Be(SpdxExpressionKind.License);
        expression.License.Should().Be(OpenSourceLicense.Apache20);
        expression.Exception.Kind.Should().Be(SpdxLicenseExceptionKind.LlvmException);
        expression.Exception.IsKnown.Should().BeTrue();
    }

    [Fact]
    public void Parse_keeps_an_unknown_exception_text()
    {
        //Act
        var exception = SpdxLicenseExpression.Parse("GPL-2.0-only WITH Acme-linking-exception").Exception;

        //Assert
        exception.Kind.Should().Be(SpdxLicenseExceptionKind.Unknown);
        exception.Id.Should().Be("Acme-linking-exception");
    }

    [Fact]
    public void Parse_keeps_unknown_identifiers()
    {
        //Act
        var expression = SpdxLicenseExpression.Parse("MIT OR Proprietary-Acme-1.0 OR LicenseRef-Internal");

        //Assert
        expression.UnknownIdentifiers.Should().ContainInOrder("Proprietary-Acme-1.0", "LicenseRef-Internal");
        expression.Operands[1].IsKnownLicense.Should().BeFalse();
        expression.Operands[1].License.Should().BeNull();
    }

    [Fact]
    public void Parse_marks_or_later_on_non_gnu_licenses()
    {
        //Act
        var expression = SpdxLicenseExpression.Parse("Apache-2.0+");

        //Assert
        expression.IsOrLater.Should().BeTrue();
        expression.License.Should().Be(OpenSourceLicense.Apache20);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("MIT AND")]
    [InlineData("OR MIT")]
    [InlineData("(MIT OR Apache-2.0")]
    [InlineData("MIT OR Apache-2.0)")]
    [InlineData("MIT Apache-2.0")]
    [InlineData("see LICENSE file")]
    [InlineData("MIT, Apache-2.0")]
    [InlineData("MIT WITH")]
    [InlineData("MIT AND AND Zlib")]
    [InlineData("()")]
    public void TryParse_rejects_invalid_expressions(string text)
    {
        //Act
        var parsed = SpdxLicenseExpression.TryParse(text, out var expression, out var error);

        //Assert
        parsed.Should().BeFalse();
        expression.Should().BeNull();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Parse_throws_format_exception_for_garbage()
    {
        //Arrange
        Action act = () => SpdxLicenseExpression.Parse("MIT && Apache");

        //Assert
        act.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData("MIT", LicenseEvaluationOutcome.Approved)]
    [InlineData("MIT OR Apache-2.0", LicenseEvaluationOutcome.Approved)]
    [InlineData("MIT OR GPL-3.0-only", LicenseEvaluationOutcome.Approved)]
    [InlineData("GPL-3.0-only", LicenseEvaluationOutcome.Rejected)]
    [InlineData("MIT AND GPL-3.0-only", LicenseEvaluationOutcome.Rejected)]
    [InlineData("(MIT OR Apache-2.0) AND Unicode-3.0", LicenseEvaluationOutcome.Approved)]
    [InlineData("Apache-2.0 WITH LLVM-exception", LicenseEvaluationOutcome.Approved)]
    [InlineData("MPL-2.0", LicenseEvaluationOutcome.Rejected)]
    [InlineData("Proprietary-Acme-1.0", LicenseEvaluationOutcome.Unresolved)]
    [InlineData("MIT OR Proprietary-Acme-1.0", LicenseEvaluationOutcome.Approved)]
    [InlineData("MIT AND Proprietary-Acme-1.0", LicenseEvaluationOutcome.Unresolved)]
    [InlineData("GPL-3.0-only AND Proprietary-Acme-1.0", LicenseEvaluationOutcome.Rejected)]
    [InlineData("GPL-3.0-only OR Proprietary-Acme-1.0", LicenseEvaluationOutcome.Unresolved)]
    [InlineData("MIT/Apache-2.0", LicenseEvaluationOutcome.Approved)]
    [InlineData("Apache-2.0+", LicenseEvaluationOutcome.Approved)]
    public void Evaluate_against_the_permissive_policy(string text, LicenseEvaluationOutcome expected)
        => SpdxLicenseExpression.Parse(text).Evaluate(LicensePolicy.Permissive()).Outcome.Should().Be(expected);

    [Fact]
    public void Evaluate_explains_a_rejection()
    {
        //Act
        var evaluation = SpdxLicenseExpression.Parse("GPL-3.0-only").Evaluate(LicensePolicy.Permissive());

        //Assert
        evaluation.IsApproved.Should().BeFalse();
        evaluation.Reason.Should().Contain("GPL-3.0-only");
        evaluation.ToString().Should().StartWith("Rejected");
    }

    [Fact]
    public void Evaluate_reports_unknown_identifiers()
    {
        //Act
        var evaluation = SpdxLicenseExpression.Parse("Proprietary-Acme-1.0").Evaluate(LicensePolicy.Permissive());

        //Assert
        evaluation.UnknownIdentifiers.Should().ContainSingle().Which.Should().Be("Proprietary-Acme-1.0");
        evaluation.Reason.Should().Contain("Proprietary-Acme-1.0");
    }

    [Fact]
    public void Evaluate_accepts_a_disallowed_license_when_its_exception_is_allowed()
    {
        //Arrange
        var expression = SpdxLicenseExpression.Parse("GPL-3.0-only WITH GCC-exception-3.1");

        //Act
        var without = expression.Evaluate(LicensePolicy.Permissive());
        var with = expression.Evaluate(LicensePolicy.Permissive().AllowException("gcc-exception-3.1"));

        //Assert
        without.Outcome.Should().Be(LicenseEvaluationOutcome.Rejected);
        with.Outcome.Should().Be(LicenseEvaluationOutcome.Approved);
        with.Reason.Should().Contain("GCC-exception-3.1");
    }

    [Fact]
    public void Evaluate_throws_for_a_null_policy()
    {
        //Arrange
        Action act = () => SpdxLicenseExpression.Parse("MIT").Evaluate(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
