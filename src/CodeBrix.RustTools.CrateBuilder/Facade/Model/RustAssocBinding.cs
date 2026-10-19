namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// An associated type binding in a path's arguments (<c>Iterator&lt;Item = i32&gt;</c>).
/// </summary>
internal sealed class RustAssocBinding
{
    /// <summary>The associated type's name (<c>Item</c>).</summary>
    public string Name { get; set; }

    /// <summary>The bound type.</summary>
    public RustType Type { get; set; }
}
