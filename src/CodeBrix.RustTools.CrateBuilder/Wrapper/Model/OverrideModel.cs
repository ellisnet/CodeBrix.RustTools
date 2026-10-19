namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Model;

/// <summary>
/// Hand-written C# copied verbatim to Overrides/.
/// </summary>
internal sealed class OverrideModel
{
    /// <summary>The Rust item it belongs to.</summary>
    public string RustPath { get; set; }

    /// <summary>The file name under Overrides/.</summary>
    public string FileName { get; set; }

    /// <summary>The source, verbatim.</summary>
    public string Source { get; set; }
}
