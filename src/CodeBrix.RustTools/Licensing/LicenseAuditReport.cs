using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The result of a license audit: one finding per crate in the dependency graph, the counts
/// per verdict, and whether the policy is satisfied.
/// </summary>
public sealed class LicenseAuditReport
{
    /// <summary>One finding per crate, sorted by name then version.</summary>
    public IReadOnlyList<CrateLicenseFinding> Findings { get; init; } = Array.Empty<CrateLicenseFinding>();

    /// <summary>The policy the audit used.</summary>
    public LicensePolicy Policy { get; init; }

    /// <summary>The workspace root that was audited (from cargo metadata), or <c>null</c>.</summary>
    public string WorkspaceRoot { get; init; }

    /// <summary>True when no finding is blocking: nothing rejected, and nothing unresolved
    /// unless the policy warns about or allows unresolved crates.</summary>
    public bool Passed => Findings.All(f => !f.IsBlocking);

    /// <summary>The number of approved crates.</summary>
    public int ApprovedCount => Count(CrateLicenseStatus.Approved);

    /// <summary>The number of rejected crates.</summary>
    public int RejectedCount => Count(CrateLicenseStatus.Rejected);

    /// <summary>The number of unresolved crates.</summary>
    public int UnresolvedCount => Count(CrateLicenseStatus.Unresolved);

    /// <summary>The number of exempt crates.</summary>
    public int ExemptCount => Count(CrateLicenseStatus.Exempt);

    /// <summary>The findings that make the audit fail.</summary>
    public IReadOnlyList<CrateLicenseFinding> BlockingFindings => Findings.Where(f => f.IsBlocking).ToList();

    /// <summary>The rejected findings.</summary>
    public IReadOnlyList<CrateLicenseFinding> Rejected => Findings.Where(f => f.Status == CrateLicenseStatus.Rejected).ToList();

    /// <summary>The unresolved findings.</summary>
    public IReadOnlyList<CrateLicenseFinding> Unresolved => Findings.Where(f => f.Status == CrateLicenseStatus.Unresolved).ToList();

    /// <summary>One line per unresolved crate when the policy's handling is
    /// <see cref="UnresolvedLicenseHandling.Warn"/>; empty otherwise.</summary>
    public IReadOnlyList<string> Warnings
        => Policy?.UnresolvedLicenseHandling == UnresolvedLicenseHandling.Warn
            ? Unresolved.Select(f => $"{f.CrateName} v{f.Version}: {f.Reason}").ToList()
            : Array.Empty<string>();

    /// <summary>
    /// Returns the finding for a crate (the first version when several are present), or
    /// <c>null</c>.
    /// </summary>
    /// <param name="crateName">The crate name.</param>
    public CrateLicenseFinding GetFinding(string crateName)
        => Findings.FirstOrDefault(f => string.Equals(f.CrateName, crateName, StringComparison.Ordinal));

    /// <summary>
    /// Builds a multi-line, human-readable summary: the counts, then every blocking finding,
    /// then the warnings.
    /// </summary>
    public string ToSummaryText()
    {
        var builder = new StringBuilder();
        builder.Append("License audit ").Append(Passed ? "PASSED" : "FAILED").Append(": ")
            .Append(Findings.Count).Append(" crates - ")
            .Append(ApprovedCount).Append(" approved, ")
            .Append(RejectedCount).Append(" rejected, ")
            .Append(UnresolvedCount).Append(" unresolved, ")
            .Append(ExemptCount).Append(" exempt.");
        foreach (var finding in BlockingFindings)
        {
            builder.AppendLine().Append("  ").Append(finding.Status == CrateLicenseStatus.Rejected ? "REJECTED   " : "UNRESOLVED ")
                .Append(finding.CrateName).Append(" v").Append(finding.Version)
                .Append(" (").Append(finding.DeclaredExpression ?? "no license").Append("): ").Append(finding.Reason);
        }

        foreach (var warning in Warnings)
        {
            builder.AppendLine().Append("  WARNING    ").Append(warning);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Throws when the audit failed.
    /// </summary>
    /// <exception cref="LicensePolicyViolationException"><see cref="Passed"/> is false; the
    /// exception carries this report and its summary.</exception>
    public void ThrowIfFailed()
    {
        if (!Passed)
        {
            throw new LicensePolicyViolationException(
                $"The license audit failed: {RejectedCount} rejected and {UnresolvedCount} unresolved crate(s).",
                this);
        }
    }

    /// <inheritdoc />
    public override string ToString() => ToSummaryText();

    private int Count(CrateLicenseStatus status) => Findings.Count(f => f.Status == status);
}
