namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// One C function the built library exports. Later work packages may add members (signature,
/// mapping, documentation).
/// </summary>
internal sealed class FacadeExport
{
    /// <summary>The exported symbol name.</summary>
    public string Name { get; set; }
}
