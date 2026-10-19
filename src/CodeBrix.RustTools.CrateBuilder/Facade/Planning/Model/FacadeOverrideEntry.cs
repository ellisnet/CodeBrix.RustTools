namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A hand-written export from the WrapPlan, pasted verbatim by the renderers.
/// </summary>
internal sealed class FacadeOverrideEntry
{
    /// <summary>The Rust item it replaces, as the WrapPlan wrote it.</summary>
    public string RustPath { get; set; }

    /// <summary>The <c>extern "C"</c> Rust source for the facade crate.</summary>
    public string RustSource { get; set; }

    /// <summary>The C# source for the wrapper, or <c>null</c>.</summary>
    public string CSharpSource { get; set; }
}
