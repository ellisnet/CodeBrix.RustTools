namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The license audit result for one crate (one package of the resolved graph).
/// </summary>
public sealed class CrateLicenseFinding
{
    /// <summary>The crate name.</summary>
    public string CrateName { get; init; } = string.Empty;

    /// <summary>The crate version.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>The <c>license</c> field exactly as declared, or <c>null</c>.</summary>
    public string DeclaredExpression { get; init; }

    /// <summary>The parsed expression, or <c>null</c> when there is none or it could not be
    /// parsed.</summary>
    public SpdxLicenseExpression ParsedExpression { get; init; }

    /// <summary>The full path of the declared <c>license-file</c>, or <c>null</c>.</summary>
    public string LicenseFile { get; init; }

    /// <summary>The verdict.</summary>
    public CrateLicenseStatus Status { get; init; }

    /// <summary>Why the verdict was reached.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>True when the crate is a member of the audited workspace.</summary>
    public bool IsWorkspaceMember { get; init; }

    /// <summary>The package source (<c>registry+...</c>, <c>git+...</c>), or <c>null</c> for
    /// workspace members and path dependencies.</summary>
    public string Source { get; init; }

    /// <summary>The full path of the crate's Cargo.toml.</summary>
    public string ManifestPath { get; init; }

    /// <summary>cargo's package id.</summary>
    public string PackageId { get; init; }

    /// <summary>True when this finding makes the audit fail (rejected, or unresolved under
    /// <see cref="UnresolvedLicenseHandling.Reject"/>).</summary>
    public bool IsBlocking { get; init; }

    /// <inheritdoc />
    public override string ToString()
        => $"{CrateName} v{Version}: {Status} ({DeclaredExpression ?? "no license"}) - {Reason}";
}
