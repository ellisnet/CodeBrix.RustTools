namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// A type alias (<c>pub type Pair&lt;T&gt; = (T, T);</c>).
/// </summary>
internal sealed class RustTypeAlias : RustItem
{
    /// <summary>The alias's generics.</summary>
    public RustGenerics Generics { get; set; } = new RustGenerics();

    /// <summary>A type or const parameter.</summary>
    public bool HasGenerics { get; set; }

    /// <summary>The aliased type.</summary>
    public RustType Type { get; set; }
}
