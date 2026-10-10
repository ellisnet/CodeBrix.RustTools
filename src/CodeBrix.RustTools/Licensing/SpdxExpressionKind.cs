namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// The kind of a node in a parsed <see cref="SpdxLicenseExpression"/>.
/// </summary>
public enum SpdxExpressionKind
{
    /// <summary>A single license identifier (with an optional <c>+</c> and <c>WITH</c> exception).</summary>
    License,

    /// <summary>A conjunction: every operand applies.</summary>
    And,

    /// <summary>A disjunction: the user may choose any one operand.</summary>
    Or,
}
