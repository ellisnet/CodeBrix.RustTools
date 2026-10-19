namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// An item that was not wrapped, for the generated docs.
/// </summary>
internal sealed class SkippedModel
{
    /// <summary>The Rust path.</summary>
    public string RustPath { get; set; }

    /// <summary>Why.</summary>
    public string Reason { get; set; }
}
