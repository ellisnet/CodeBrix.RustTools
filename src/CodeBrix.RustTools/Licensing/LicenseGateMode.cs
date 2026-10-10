namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// What the license gate of a build does with a failed audit
/// (<see cref="Building.CargoBuildOptions.LicenseGateMode"/>).
/// </summary>
public enum LicenseGateMode
{
    /// <summary>A failed audit stops the command before cargo runs: it throws
    /// <see cref="LicensePolicyViolationException"/>, or, when
    /// <see cref="Building.CargoBuildOptions.ThrowOnLicenseViolation"/> is false, returns a
    /// failed result with <see cref="Building.CargoCommandResult.LicenseGateFailed"/> set (the
    /// default).</summary>
    Enforce,

    /// <summary>The audit runs and its report is attached to the result
    /// (<see cref="Building.CargoCommandResult.LicenseAudit"/>) whether it passed or not, and
    /// cargo runs anyway: a failed audit never fails the result and never throws
    /// (<see cref="Building.CargoBuildOptions.ThrowOnLicenseViolation"/> is ignored).</summary>
    ReportOnly,
}
