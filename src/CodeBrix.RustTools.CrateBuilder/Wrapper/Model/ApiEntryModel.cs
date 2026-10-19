namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// One member in the generated API reference.
/// </summary>
internal sealed class ApiEntryModel
{
    /// <summary>The managed signature.</summary>
    public string Signature { get; set; }

    /// <summary>The member's summary.</summary>
    public string Summary { get; set; }

    /// <summary>The Rust item it wraps, or <c>null</c>.</summary>
    public string RustPath { get; set; }
}
