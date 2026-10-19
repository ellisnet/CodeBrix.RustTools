namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// The structural kind of a <see cref="RustType"/> (JSON: snake_case, for example
/// <c>impl_trait</c>).
/// </summary>
internal enum RustTypeKind
{
    /// <summary>A primitive (<c>i32</c>, <c>f64</c>, <c>bool</c>, <c>char</c>, <c>str</c>, ...);
    /// <see cref="RustType.Name"/> holds it.</summary>
    Primitive,

    /// <summary><c>&amp;T</c> / <c>&amp;mut T</c> / <c>&amp;'a T</c>.</summary>
    Reference,

    /// <summary><c>[T]</c>.</summary>
    Slice,

    /// <summary><c>[T; N]</c>.</summary>
    Array,

    /// <summary>A named type (<c>Option&lt;T&gt;</c>, <c>Self</c>, <c>std::rc::Rc&lt;T&gt;</c>).</summary>
    Path,

    /// <summary><c>(A, B)</c> (one or more elements).</summary>
    Tuple,

    /// <summary><c>()</c>, also the return type of a function without <c>-&gt;</c>.</summary>
    Unit,

    /// <summary><c>*const T</c> / <c>*mut T</c>.</summary>
    Ptr,

    /// <summary><c>impl Trait</c>.</summary>
    ImplTrait,

    /// <summary><c>dyn Trait</c>.</summary>
    DynTrait,

    /// <summary><c>fn(A) -&gt; B</c>, optionally <c>unsafe</c> / <c>extern "C"</c>.</summary>
    FnPtr,

    /// <summary><c>!</c>.</summary>
    Never,

    /// <summary><c>_</c>.</summary>
    Infer,

    /// <summary>A type written as a macro invocation.</summary>
    Macro,

    /// <summary>Anything else syn models.</summary>
    Other,
}
