namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// How a struct or enum variant holds its fields (JSON: snake_case).
/// </summary>
internal enum RustItemShape
{
    /// <summary><c>{ a: T, b: U }</c>.</summary>
    Named,

    /// <summary><c>(T, U)</c>.</summary>
    Tuple,

    /// <summary>No fields.</summary>
    Unit,

    /// <summary>A <c>union</c> (structs only).</summary>
    Union,
}
