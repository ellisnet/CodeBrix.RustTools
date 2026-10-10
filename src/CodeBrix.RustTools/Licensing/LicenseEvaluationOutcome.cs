namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The result of checking a license expression against a <see cref="LicensePolicy"/>.
/// </summary>
public enum LicenseEvaluationOutcome
{
    /// <summary>The policy allows the expression (for <c>OR</c>, at least one choice is allowed).</summary>
    Approved,

    /// <summary>The policy does not allow the expression.</summary>
    Rejected,

    /// <summary>The expression could not be decided (an unknown identifier was involved and
    /// the known parts did not decide it).</summary>
    Unresolved,
}
