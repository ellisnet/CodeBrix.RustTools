namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Which crate root the extractor read (JSON: snake_case).
/// </summary>
internal enum RustRootKind
{
    /// <summary>The library root (<c>[lib] path</c> or <c>src/lib.rs</c>).</summary>
    Lib,

    /// <summary>The package has no library; the binary root was read instead.</summary>
    Bin,
}
