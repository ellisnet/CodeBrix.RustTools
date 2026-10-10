namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// What a license audit does with crates whose license cannot be decided (no license, a
/// license file only, an unknown identifier, or an unparsable expression).
/// </summary>
public enum UnresolvedLicenseHandling
{
    /// <summary>Unresolved crates fail the audit (the default).</summary>
    Reject,

    /// <summary>Unresolved crates pass but are listed in <see cref="LicenseAuditReport.Warnings"/>.</summary>
    Warn,

    /// <summary>Unresolved crates pass silently.</summary>
    Allow,
}
