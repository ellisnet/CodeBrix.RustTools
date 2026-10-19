namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A <c>const</c> or <c>static</c> item, or a <c>pub const</c> of an inherent impl (with
/// <see cref="Owner"/>).
/// </summary>
internal sealed class RustConstant : RustItem
{
    /// <summary>Const or static.</summary>
    public RustConstantKind Kind { get; set; }

    /// <summary><c>static mut</c>.</summary>
    public bool IsMutable { get; set; }

    /// <summary>The declared type.</summary>
    public RustType Type { get; set; }

    /// <summary>The value verbatim when it is a literal or a negated literal
    /// (<c>254_016_000_000</c>); <c>null</c> otherwise.</summary>
    public string Value { get; set; }

    /// <summary>The impl block of an associated const; <c>null</c> otherwise.</summary>
    public RustOwner Owner { get; set; }
}
