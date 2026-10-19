using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

/// <summary>
/// How one Rust type travels through the C boundary and appears on the managed side.
/// </summary>
internal sealed class TypeMapping
{
    /// <summary>The kind.</summary>
    public MappingKind Kind { get; set; }

    /// <summary>The upstream type as printed (<c>&amp;[u8]</c>, <c>Option&lt;usize&gt;</c>'s inner
    /// <c>usize</c>, <c>[[f64; 3]; 3]</c>).</summary>
    public string RustType { get; set; }

    /// <summary>The C type: <c>int32_t</c>, <c>bool</c>, <c>void</c>, a mirror's struct name, a
    /// handle's <c>&lt;cname&gt;*</c>, <c>int32_t</c> for enums, an array struct name, the data
    /// pointer type of slices / input strings (<c>const uint8_t*</c>), or
    /// <c>&lt;prefix&gt;buffer</c> for owned output buffers and strings.</summary>
    public string CType { get; set; }

    /// <summary>The idiomatic managed type (<c>int</c>, <c>string</c>, <c>byte[]</c>,
    /// <c>ReadOnlySpan&lt;byte&gt;</c>, a mirror / enum / handle class name).</summary>
    public string ManagedType { get; set; }

    /// <summary>The type the wrapper's interop declaration uses (<c>byte</c> for bool,
    /// <c>nint</c> for handles, <c>Buffer</c> for owned outputs, <c>ReadOnlySpan&lt;T&gt;</c> /
    /// <c>Span&lt;T&gt;</c> for input slices and strings).</summary>
    public string ManagedMarshalType { get; set; }

    /// <summary>The element of slices, vecs and arrays (for a nested array the inner array);
    /// the pointee of a raw pointer when it is a primitive.</summary>
    public TypeMapping Element { get; set; }

    /// <summary>The dimensions of a fixed array, outermost first (<c>[3, 3]</c>).</summary>
    public IList<int> ArrayLengths { get; set; } = new List<int>();

    /// <summary>The mirror of a <see cref="MappingKind.MirrorStruct"/>.</summary>
    public MirrorStruct Mirror { get; set; }

    /// <summary>The handle of a <see cref="MappingKind.Handle"/>.</summary>
    public HandleType Handle { get; set; }

    /// <summary>The enum of a <see cref="MappingKind.Enum"/>.</summary>
    public EnumMapping Enum { get; set; }

    /// <summary>The C size in bytes of primitives, bools, enums, mirrors, fixed arrays and
    /// pointers (64-bit targets); 0 otherwise.</summary>
    public int ByteSize { get; set; }

    /// <summary>The C value is blittable (everything except slices, vecs and strings, which
    /// travel as pointer + length or as a buffer).</summary>
    public bool IsBlittable { get; set; }

    /// <summary>ADDED (WP6): a mutable slice (<c>&amp;mut [T]</c>, data pointer <c>T*</c>) or
    /// mutable raw pointer, or an object handle borrowed with <c>&amp;mut</c>.</summary>
    public bool IsMutable { get; set; }
}
