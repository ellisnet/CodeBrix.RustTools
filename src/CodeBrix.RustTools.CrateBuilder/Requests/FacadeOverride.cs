namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// A hand-written replacement for the generated C export of one Rust item - the escape hatch
/// for an item the automatic mapping cannot handle.
/// </summary>
public sealed class FacadeOverride
{
    /// <summary>The Rust item it replaces, as a path inside the crate, for example
    /// <c>Encoder::encode_frame</c> or <c>parse_header</c>.</summary>
    public string RustPath { get; set; }

    /// <summary>The <c>extern "C"</c> function (Rust source) to put in the facade crate in
    /// place of the generated one.</summary>
    public string RustSource { get; set; }

    /// <summary>The managed side (C# source) for the wrapper, or <c>null</c> to let the
    /// wrapper declare the export from its signature.</summary>
    public string CSharpSource { get; set; }

    internal FacadeOverride Clone() => new FacadeOverride { RustPath = RustPath, RustSource = RustSource, CSharpSource = CSharpSource };
}
