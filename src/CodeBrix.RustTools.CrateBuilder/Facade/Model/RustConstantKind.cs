namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// <c>const</c> or <c>static</c> (JSON: snake_case).
/// </summary>
internal enum RustConstantKind
{
    /// <summary>A <c>const</c> item.</summary>
    Const,

    /// <summary>A <c>static</c> item.</summary>
    Static,
}
