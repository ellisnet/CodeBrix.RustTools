namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A const generic parameter (<c>const N: usize</c>).
/// </summary>
internal sealed class RustConstParam
{
    /// <summary>The parameter's name.</summary>
    public string Name { get; set; }

    /// <summary>Its type.</summary>
    public RustType Type { get; set; }

    /// <summary>The default, verbatim; <c>null</c> when none.</summary>
    public string Default { get; set; }
}
