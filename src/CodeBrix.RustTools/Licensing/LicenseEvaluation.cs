using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The outcome of <see cref="SpdxLicenseExpression.Evaluate"/>, with a human-readable reason.
/// </summary>
public sealed class LicenseEvaluation
{
    /// <summary>Approved, rejected or unresolved.</summary>
    public LicenseEvaluationOutcome Outcome { get; init; }

    /// <summary>Why the outcome was reached (names the licenses involved).</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The license identifiers in the expression that are not known SPDX licenses.</summary>
    public IReadOnlyList<string> UnknownIdentifiers { get; init; } = Array.Empty<string>();

    /// <summary>True when <see cref="Outcome"/> is <see cref="LicenseEvaluationOutcome.Approved"/>.</summary>
    public bool IsApproved => Outcome == LicenseEvaluationOutcome.Approved;

    /// <inheritdoc />
    public override string ToString() => $"{Outcome}: {Reason}";
}
