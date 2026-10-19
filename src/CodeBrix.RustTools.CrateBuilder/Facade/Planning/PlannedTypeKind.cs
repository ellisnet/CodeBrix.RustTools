namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The kinds of <see cref="PlannedType"/>.
/// </summary>
internal enum PlannedTypeKind
{
    /// <summary><c>()</c>.</summary>
    Unit,

    /// <summary>An integer or float.</summary>
    Primitive,

    /// <summary><c>bool</c>.</summary>
    Bool,

    /// <summary><c>str</c> (only usable behind a reference).</summary>
    Str,

    /// <summary>A mirror struct.</summary>
    Mirror,

    /// <summary>A handle type.</summary>
    Handle,

    /// <summary>A C-like enum.</summary>
    Enum,

    /// <summary><c>[T; N]</c>.</summary>
    Array,

    /// <summary><c>[T]</c>.</summary>
    Slice,

    /// <summary><c>String</c>.</summary>
    String,

    /// <summary><c>Vec&lt;T&gt;</c>.</summary>
    Vec,

    /// <summary><c>Option&lt;T&gt;</c>.</summary>
    Option,

    /// <summary><c>Result&lt;T, E&gt;</c>.</summary>
    Result,

    /// <summary><c>(A, B)</c>.</summary>
    Tuple,

    /// <summary><c>&amp;T</c> / <c>&amp;mut T</c>.</summary>
    Reference,

    /// <summary><c>*const T</c> / <c>*mut T</c>.</summary>
    Pointer,

    /// <summary>Cannot cross the C boundary automatically.</summary>
    Unsupported,
}
