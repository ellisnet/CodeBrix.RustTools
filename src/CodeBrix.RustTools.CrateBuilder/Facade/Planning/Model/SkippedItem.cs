namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// An upstream item that is not wrapped, with the reason in plain language and a stub to start
/// a FacadeOverride from.
/// </summary>
internal sealed class SkippedItem
{
    /// <summary>The item's path inside the crate, shortest public form without
    /// <c>crate::</c>.</summary>
    public string RustPath { get; set; }

    /// <summary><c>function</c>, <c>method</c>, <c>struct</c>, <c>enum</c>, <c>union</c> or
    /// <c>constant</c>.</summary>
    public string Kind { get; set; }

    /// <summary>Why, written for a novice, including what to do.</summary>
    public string Reason { get; set; }

    /// <summary>A ready-to-fill Rust <c>extern "C"</c> stub for a FacadeOverride.</summary>
    public string OverrideHint { get; set; }
}
