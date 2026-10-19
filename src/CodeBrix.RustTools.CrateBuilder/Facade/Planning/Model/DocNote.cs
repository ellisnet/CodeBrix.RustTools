namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// A sentence from the crate's own documentation that states a unit, range, convention or
/// thread rule the wrapper's user should know.
/// </summary>
internal sealed class DocNote
{
    /// <summary>The documented item's path inside the crate (<c>Tick</c>, <c>Vec2::x</c>).</summary>
    public string RustPath { get; set; }

    /// <summary>The sentence, as written.</summary>
    public string Text { get; set; }
}
