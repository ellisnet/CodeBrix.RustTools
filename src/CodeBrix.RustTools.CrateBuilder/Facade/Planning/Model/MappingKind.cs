namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// The kind of a <see cref="TypeMapping"/>.
/// </summary>
internal enum MappingKind
{
    /// <summary>An integer or float (<c>i32</c> -&gt; <c>int32_t</c> -&gt; <c>int</c>).</summary>
    Primitive,

    /// <summary><c>bool</c> (C <c>bool</c>, managed <c>bool</c> marshalled as <c>byte</c>).</summary>
    Bool,

    /// <summary><c>()</c>.</summary>
    Unit,

    /// <summary>A mirror struct by value.</summary>
    MirrorStruct,

    /// <summary>An opaque handle pointer.</summary>
    Handle,

    /// <summary>A C-like enum as <c>int32_t</c>.</summary>
    Enum,

    /// <summary><c>[T; N]</c>, possibly nested, of a primitive, passed as a C struct holding the
    /// array (managed: an InlineArray struct).</summary>
    FixedArray,

    /// <summary><c>&amp;[T]</c> / <c>&amp;mut [T]</c> of a primitive (data + length).</summary>
    PrimitiveSlice,

    /// <summary><c>Vec&lt;T&gt;</c> of a primitive (an input slice, or an owned output buffer).</summary>
    PrimitiveVec,

    /// <summary><c>String</c> (an input string, or an owned output string).</summary>
    String,

    /// <summary><c>&amp;str</c> (an input string).</summary>
    Str,

    /// <summary>A raw pointer (pass-through mode only).</summary>
    RawPointer,

    /// <summary><c>&amp;[T]</c> / <c>Vec&lt;T&gt;</c> input of an element that is not a number
    /// (a mirror struct, a C-like enum, <c>bool</c>, a fixed array, an object that is Clone, or
    /// a string; see <see cref="TypeMapping.Element"/>): data + element count of the element's C
    /// form (struct, int32_t, byte, array struct, object pointer, str_ref), copied into a Rust
    /// <c>Vec</c> of the upstream type before the call (objects are cloned).</summary>
    ValueSlice,

    /// <summary>An owned output <c>Vec&lt;T&gt;</c> of such a value type: a Buffer of the
    /// element's C form.</summary>
    ValueVec,
}
