namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The verdict on one crate in a <see cref="LicenseAuditReport"/>.
/// </summary>
public enum CrateLicenseStatus
{
    /// <summary>The crate's license expression satisfies the policy.</summary>
    Approved,

    /// <summary>The crate's license expression does not satisfy the policy.</summary>
    Rejected,

    /// <summary>The crate's license could not be decided: no license at all, a license file
    /// only, an unknown identifier, or an unparsable expression.</summary>
    Unresolved,

    /// <summary>The crate was not checked: a workspace member exempted by the policy, or a crate
    /// listed in <see cref="LicensePolicy.CrateOverrides"/>.</summary>
    Exempt,
}
