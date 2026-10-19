namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// One variant of an <see cref="EnumMapping"/>.
/// </summary>
internal sealed class EnumVariantMapping
{
    /// <summary>The upstream variant name (<c>A</c>).</summary>
    public string RustName { get; set; }

    /// <summary>The C constant: upper-case <c>&lt;PREFIX&gt;&lt;ENUM&gt;_&lt;VARIANT&gt;</c>
    /// (<c>DATACRATE_MODE_A</c>).</summary>
    public string CName { get; set; }

    /// <summary>The managed member name.</summary>
    public string ManagedName { get; set; }

    /// <summary>The discriminant: explicit, else the previous value + 1 (the first is 0).</summary>
    public long Value { get; set; }

    /// <summary>The variant's Rust doc comments.</summary>
    public string Doc { get; set; }
}
