namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// One field of a <see cref="MirrorStruct"/>.
/// </summary>
internal sealed class MirrorField
{
    /// <summary>The upstream field name (<c>frame_size</c>), or the index (<c>0</c>) of a tuple
    /// struct field.</summary>
    public string Name { get; set; }

    /// <summary>The field name in the C struct and the facade mirror (<c>frame_size</c>,
    /// <c>item0</c>).</summary>
    public string CName { get; set; }

    /// <summary>The managed field name (<c>FrameSize</c>, <c>Item0</c>).</summary>
    public string ManagedName { get; set; }

    /// <summary>The field's type.</summary>
    public TypeMapping Type { get; set; }

    /// <summary>The field's Rust doc comments.</summary>
    public string Doc { get; set; }

    /// <summary>ADDED (WP6): the C offset in bytes.</summary>
    public int Offset { get; set; }
}
