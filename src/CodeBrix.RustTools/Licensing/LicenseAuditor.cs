using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// Checks the license of every crate in a project's resolved dependency graph against a
/// <see cref="LicensePolicy"/>.
/// </summary>
public static class LicenseAuditor
{
    /// <summary>
    /// Runs <c>cargo metadata</c> for the project and audits the result.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">Cancels the cargo run.</param>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException">cargo metadata failed.</exception>
    public static Task<LicenseAuditReport> AuditAsync(CargoProject project, LicensePolicy policy, CancellationToken cancellationToken = default)
        => AuditAsync(project, policy, null, cancellationToken);

    /// <summary>
    /// Runs <c>cargo metadata</c> with the given options (for example
    /// <see cref="CargoMetadataOptions.Offline"/>) and audits the result.
    /// </summary>
    /// <param name="project">The project.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="metadataOptions">Options for <c>cargo metadata</c>, or <c>null</c>.</param>
    /// <param name="cancellationToken">Cancels the cargo run.</param>
    /// <exception cref="ArgumentException"><paramref name="metadataOptions"/> sets
    /// <see cref="CargoMetadataOptions.NoDependencies"/> (the audit needs the dependency graph).</exception>
    /// <exception cref="ToolchainNotFoundException">cargo is not installed.</exception>
    /// <exception cref="CargoCommandException">cargo metadata failed.</exception>
    public static async Task<LicenseAuditReport> AuditAsync(CargoProject project, LicensePolicy policy, CargoMetadataOptions metadataOptions, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(policy);
        if (metadataOptions?.NoDependencies == true)
        {
            throw new ArgumentException("A license audit needs the dependency graph; NoDependencies must be false.", nameof(metadataOptions));
        }

        var metadata = await project.GetMetadataAsync(metadataOptions, cancellationToken).ConfigureAwait(false);
        return Audit(metadata, policy);
    }

    /// <summary>
    /// Audits every package in already-collected metadata. Nothing is run.
    /// </summary>
    /// <param name="metadata">The cargo metadata.</param>
    /// <param name="policy">The policy.</param>
    public static LicenseAuditReport Audit(CargoMetadata metadata, LicensePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(policy);
        var findings = metadata.Packages
            .Select(p => Evaluate(p, metadata.IsWorkspaceMember(p.Id), policy))
            .OrderBy(f => f.CrateName, StringComparer.Ordinal)
            .ThenBy(f => f.Version, StringComparer.Ordinal)
            .ToList();
        return new LicenseAuditReport { Findings = findings, Policy = policy, WorkspaceRoot = metadata.WorkspaceRoot };
    }

    /// <summary>
    /// Evaluates one package against the policy.
    /// </summary>
    /// <param name="package">The package.</param>
    /// <param name="isWorkspaceMember">True when the package is a workspace member.</param>
    /// <param name="policy">The policy.</param>
    public static CrateLicenseFinding Evaluate(CargoMetadataPackage package, bool isWorkspaceMember, LicensePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(policy);

        var declared = string.IsNullOrWhiteSpace(package.License) ? null : package.License.Trim();
        SpdxLicenseExpression parsed = null;
        string parseError = null;
        if (declared != null)
        {
            SpdxLicenseExpression.TryParse(declared, out parsed, out parseError);
        }

        CrateLicenseStatus status;
        string reason;
        if (policy.CrateOverrides.TryGetValue(package.Name, out var overrideReason))
        {
            status = CrateLicenseStatus.Exempt;
            reason = "crate override: " + overrideReason;
        }
        else if (isWorkspaceMember && policy.ExemptWorkspaceMembers)
        {
            status = CrateLicenseStatus.Exempt;
            reason = "workspace member (exempt by policy)";
        }
        else if (declared == null && package.LicenseFile == null)
        {
            status = CrateLicenseStatus.Unresolved;
            reason = "declares neither a license expression nor a license-file";
        }
        else if (declared == null)
        {
            status = CrateLicenseStatus.Unresolved;
            reason = $"declares only a license-file ({package.LicenseFile}); its license cannot be identified automatically";
        }
        else if (parsed == null)
        {
            status = CrateLicenseStatus.Unresolved;
            reason = "the license expression could not be parsed: " + parseError;
        }
        else
        {
            var evaluation = parsed.Evaluate(policy);
            status = evaluation.Outcome switch
            {
                LicenseEvaluationOutcome.Approved => CrateLicenseStatus.Approved,
                LicenseEvaluationOutcome.Rejected => CrateLicenseStatus.Rejected,
                _ => CrateLicenseStatus.Unresolved,
            };
            reason = evaluation.Outcome == LicenseEvaluationOutcome.Unresolved && evaluation.UnknownIdentifiers.Count > 0
                ? $"unknown license identifier(s): {string.Join(", ", evaluation.UnknownIdentifiers)} ({evaluation.Reason})"
                : evaluation.Reason;
        }

        var blocking = status == CrateLicenseStatus.Rejected
            || (status == CrateLicenseStatus.Unresolved && policy.UnresolvedLicenseHandling == UnresolvedLicenseHandling.Reject);

        return new CrateLicenseFinding
        {
            CrateName = package.Name,
            Version = package.Version,
            DeclaredExpression = declared,
            ParsedExpression = parsed,
            LicenseFile = package.LicenseFilePath,
            Status = status,
            Reason = reason,
            IsWorkspaceMember = isWorkspaceMember,
            Source = package.Source,
            ManifestPath = package.ManifestPath,
            PackageId = package.Id,
            IsBlocking = blocking,
        };
    }
}
