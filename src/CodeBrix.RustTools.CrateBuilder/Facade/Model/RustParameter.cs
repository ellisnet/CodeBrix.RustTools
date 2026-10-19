namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A function parameter (after the receiver).
/// </summary>
internal sealed class RustParameter
{
    /// <summary>The identifier; <c>arg&lt;N&gt;</c> for a destructuring pattern.</summary>
    public string Name { get; set; }

    /// <summary>The destructuring pattern verbatim; <c>null</c> for a plain identifier.</summary>
    public string Pattern { get; set; }

    /// <summary>The parameter's type.</summary>
    public RustType Type { get; set; }
}
