namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The impl block a method or associated const belongs to.
/// </summary>
internal sealed class RustOwner
{
    /// <summary>The impl's self type as written (<c>Vec2</c>, <c>Grid&lt;T&gt;</c>).</summary>
    public string TypeText { get; set; }

    /// <summary>The impl's self type, structurally.</summary>
    public RustType Type { get; set; }

    /// <summary>The canonical path of the local struct / enum / union / alias it names;
    /// <c>null</c> when it could not be resolved.</summary>
    public string ResolvedPath { get; set; }

    /// <summary>The impl block's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();
}
