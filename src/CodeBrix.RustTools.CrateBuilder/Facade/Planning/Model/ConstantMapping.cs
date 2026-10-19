namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A public <c>const</c> (or non-<c>mut</c> <c>static</c>) of the crate whose value is a plain
/// literal of a number, <c>bool</c> or <c>&amp;str</c> type: the wrapper declares it as a C#
/// <c>const</c> with the same value. Nothing crosses C for it.
/// </summary>
internal sealed class ConstantMapping
{
    /// <summary>The item's path inside the crate, shortest public form without
    /// <c>crate::</c>.</summary>
    public string RustPath { get; set; }

    /// <summary>The Rust type as written (<c>u32</c>, <c>&amp;'static str</c>).</summary>
    public string RustType { get; set; }

    /// <summary>The Rust value as written (<c>0x10</c>, <c>"text"</c>).</summary>
    public string RustValue { get; set; }

    /// <summary>The C# type keyword (<c>uint</c>, <c>string</c>, ...).</summary>
    public string ManagedType { get; set; }

    /// <summary>The C# literal of the value.</summary>
    public string ManagedLiteral { get; set; }

    /// <summary>The C# member name (PascalCase, unique in the entry class).</summary>
    public string ManagedName { get; set; }

    /// <summary>The Rust doc comment.</summary>
    public string Doc { get; set; }
}
