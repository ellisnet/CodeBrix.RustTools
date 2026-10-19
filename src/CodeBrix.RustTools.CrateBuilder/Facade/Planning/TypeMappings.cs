using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The planner's type table: C and managed spellings of the primitives and the factory methods
/// for every <see cref="TypeMapping"/> the planner creates. Names of mirrors, handles and enums
/// are filled in by <see cref="Finish"/> once the planner has named them.
/// </summary>
internal static class TypeMappings
{
    private static readonly Dictionary<string, (string C, string Managed, int Size)> Primitives = new Dictionary<string, (string, string, int)>(StringComparer.Ordinal)
    {
        ["i8"] = ("int8_t", "sbyte", 1),
        ["i16"] = ("int16_t", "short", 2),
        ["i32"] = ("int32_t", "int", 4),
        ["i64"] = ("int64_t", "long", 8),
        ["isize"] = ("ptrdiff_t", "nint", 8),
        ["u8"] = ("uint8_t", "byte", 1),
        ["u16"] = ("uint16_t", "ushort", 2),
        ["u32"] = ("uint32_t", "uint", 4),
        ["u64"] = ("uint64_t", "ulong", 8),
        ["usize"] = ("size_t", "nuint", 8),
        ["f32"] = ("float", "float", 4),
        ["f64"] = ("double", "double", 8),
    };

    /// <summary>C type aliases of <c>core::ffi</c> / <c>std::os::raw</c> / <c>libc</c> and the
    /// primitive each one is on every CrateBuilder target (<c>c_char</c>, <c>c_long</c> and
    /// <c>c_ulong</c> differ between targets and are not listed).</summary>
    public static readonly IReadOnlyDictionary<string, string> FixedCAliases = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["c_schar"] = "i8",
        ["c_uchar"] = "u8",
        ["c_short"] = "i16",
        ["c_ushort"] = "u16",
        ["c_int"] = "i32",
        ["c_uint"] = "u32",
        ["c_longlong"] = "i64",
        ["c_ulonglong"] = "u64",
        ["c_float"] = "f32",
        ["c_double"] = "f64",
        ["size_t"] = "usize",
        ["ssize_t"] = "isize",
    };

    /// <summary>Whether the planner maps the Rust primitive <paramref name="name"/>.</summary>
    public static bool IsPrimitive(string name) => name != null && Primitives.ContainsKey(name);

    /// <summary>The C size of a primitive (bytes).</summary>
    public static int SizeOf(string name) => Primitives[name].Size;

    /// <summary>A primitive (<paramref name="rustText"/> = how it was written, for example an
    /// alias).</summary>
    public static TypeMapping Primitive(string name, string rustText = null)
    {
        var p = Primitives[name];
        return new TypeMapping { Kind = MappingKind.Primitive, RustType = rustText ?? name, CType = p.C, ManagedType = p.Managed, ManagedMarshalType = p.Managed, ByteSize = p.Size, IsBlittable = true };
    }

    /// <summary><c>usize</c> (lengths).</summary>
    public static TypeMapping Usize() => Primitive("usize");

    /// <summary><c>bool</c>.</summary>
    public static TypeMapping Bool() => new TypeMapping { Kind = MappingKind.Bool, RustType = "bool", CType = "bool", ManagedType = "bool", ManagedMarshalType = "byte", ByteSize = 1, IsBlittable = true };

    /// <summary><c>()</c>.</summary>
    public static TypeMapping Unit() => new TypeMapping { Kind = MappingKind.Unit, RustType = "()", CType = "void", ManagedType = "void", ManagedMarshalType = "void", IsBlittable = true };

    /// <summary>A mirror by value (names filled by <see cref="Finish"/>).</summary>
    public static TypeMapping Mirror(MirrorStruct mirror, string rustText) => new TypeMapping { Kind = MappingKind.MirrorStruct, RustType = rustText, Mirror = mirror, IsBlittable = true };

    /// <summary>A handle pointer (names filled by <see cref="Finish"/>).</summary>
    public static TypeMapping Handle(HandleType handle, string rustText) => new TypeMapping { Kind = MappingKind.Handle, RustType = rustText, Handle = handle, ByteSize = 8, IsBlittable = true, ManagedMarshalType = "nint" };

    /// <summary>A C-like enum as <c>int32_t</c> (names filled by <see cref="Finish"/>).</summary>
    public static TypeMapping Enum(EnumMapping mapping, string rustText) => new TypeMapping { Kind = MappingKind.Enum, RustType = rustText, CType = "int32_t", Enum = mapping, ByteSize = 4, IsBlittable = true };

    /// <summary>A fixed array of a primitive, nested arrays included (<paramref name="lengths"/>
    /// outermost first).</summary>
    public static TypeMapping FixedArray(string prefix, string primitive, IReadOnlyList<int> lengths, string rustText)
    {
        TypeMapping current = Primitive(primitive);
        for (var i = lengths.Count - 1; i >= 0; i--)
        {
            var dims = lengths.Skip(i).ToList();
            var dimText = string.Join("x", dims);
            var size = current.ByteSize * lengths[i];
            current = new TypeMapping
            {
                Kind = MappingKind.FixedArray,
                RustType = i == 0 ? rustText : ArrayText(primitive, dims),
                CType = prefix + "array_" + primitive + "_" + dimText,
                ManagedType = PlannerNames.Pascal(primitive) + "Array" + dimText,
                Element = current,
                ArrayLengths = dims.ToList(),
                ByteSize = size,
                IsBlittable = true,
            };
            current.ManagedMarshalType = current.ManagedType;
        }

        return current;
    }

    /// <summary>An input slice of a primitive (<c>&amp;[T]</c> / <c>&amp;mut [T]</c>).</summary>
    public static TypeMapping Slice(string primitive, bool mutable, string rustText)
    {
        var element = Primitive(primitive);
        var span = (mutable ? "Span<" : "ReadOnlySpan<") + element.ManagedType + ">";
        return new TypeMapping { Kind = MappingKind.PrimitiveSlice, RustType = rustText, CType = (mutable ? string.Empty : "const ") + element.CType + "*", ManagedType = span, ManagedMarshalType = span, Element = element, IsMutable = mutable };
    }

    /// <summary>An input <c>Vec&lt;T&gt;</c> of a primitive (passed as a read-only slice).</summary>
    public static TypeMapping VecInput(string primitive, string rustText)
    {
        var element = Primitive(primitive);
        var span = "ReadOnlySpan<" + element.ManagedType + ">";
        return new TypeMapping { Kind = MappingKind.PrimitiveVec, RustType = rustText, CType = "const " + element.CType + "*", ManagedType = span, ManagedMarshalType = span, Element = element };
    }

    /// <summary>An owned output <c>Vec&lt;T&gt;</c> of a primitive (a Buffer).</summary>
    public static TypeMapping VecOutput(string prefix, string primitive, string rustText)
    {
        var element = Primitive(primitive);
        return new TypeMapping { Kind = MappingKind.PrimitiveVec, RustType = rustText, CType = prefix + "buffer", ManagedType = element.ManagedType + "[]", ManagedMarshalType = "Buffer", Element = element };
    }

    /// <summary>An input slice or <c>Vec</c> of a plain value type (<paramref name="element"/>:
    /// mirror, enum, bool or fixed array).</summary>
    public static TypeMapping ValueSlice(TypeMapping element, string rustText)
        => new TypeMapping { Kind = MappingKind.ValueSlice, RustType = rustText, Element = element };

    /// <summary>The element of an input list of strings: one <c>&lt;prefix&gt;str_ref</c>
    /// (pointer + byte length, UTF-8) per string (<paramref name="kind"/> Str = the list holds
    /// <c>&amp;str</c>, String = it holds owned strings).</summary>
    public static TypeMapping StringListElement(string prefix, MappingKind kind, string rustText)
        => new TypeMapping { Kind = kind, RustType = rustText, CType = prefix + "str_ref", ManagedType = "string", ManagedMarshalType = "string" };

    /// <summary>An owned output <c>Vec</c> of a plain value type (a Buffer).</summary>
    public static TypeMapping ValueVec(string prefix, TypeMapping element, string rustText)
        => new TypeMapping { Kind = MappingKind.ValueVec, RustType = rustText, CType = prefix + "buffer", ManagedMarshalType = "Buffer", Element = element };

    /// <summary>An input string (<c>&amp;str</c> = <see cref="MappingKind.Str"/>, <c>String</c>
    /// = <see cref="MappingKind.String"/>).</summary>
    public static TypeMapping StringInput(MappingKind kind, string rustText)
        => new TypeMapping { Kind = kind, RustType = rustText, CType = "const uint8_t*", ManagedType = "string", ManagedMarshalType = "ReadOnlySpan<byte>", Element = Primitive("u8") };

    /// <summary>An owned output <c>String</c> (a Buffer of UTF-8 bytes).</summary>
    public static TypeMapping StringOutput(string prefix, string rustText)
        => new TypeMapping { Kind = MappingKind.String, RustType = rustText, CType = prefix + "buffer", ManagedType = "string", ManagedMarshalType = "Buffer", Element = Primitive("u8") };

    /// <summary>The Buffer struct itself (the parameter of the release functions).</summary>
    public static TypeMapping Buffer(string prefix, MappingKind kind)
        => new TypeMapping { Kind = kind, RustType = "Buffer", CType = prefix + "buffer", ManagedType = "Buffer", ManagedMarshalType = "Buffer", Element = Primitive("u8"), ByteSize = 24, IsBlittable = true };

    /// <summary>A raw pointer (pass-through mode).</summary>
    public static TypeMapping RawPointer(string pointee, bool mutable, string rustText)
    {
        var element = pointee != null && IsPrimitive(pointee) ? Primitive(pointee) : null;
        var c = element == null ? "void*" : element.CType + "*";
        return new TypeMapping { Kind = MappingKind.RawPointer, RustType = rustText, CType = (mutable ? string.Empty : "const ") + c, ManagedType = "nint", ManagedMarshalType = "nint", Element = element, ByteSize = 8, IsBlittable = true, IsMutable = mutable };
    }

    /// <summary>A pass-through C long (<c>c_long</c> / <c>c_ulong</c>: 32-bit on Windows, 64-bit
    /// elsewhere).</summary>
    public static TypeMapping CLong(bool unsigned, string rustText)
        => new TypeMapping { Kind = MappingKind.Primitive, RustType = rustText, CType = unsigned ? "unsigned long" : "long", ManagedType = unsigned ? "System.Runtime.InteropServices.CULong" : "System.Runtime.InteropServices.CLong", ManagedMarshalType = unsigned ? "System.Runtime.InteropServices.CULong" : "System.Runtime.InteropServices.CLong", IsBlittable = true };

    /// <summary>A pass-through C char (<c>c_char</c>: signed or unsigned depending on the target;
    /// one byte either way).</summary>
    public static TypeMapping CChar(string rustText)
        => new TypeMapping { Kind = MappingKind.Primitive, RustType = rustText, CType = "char", ManagedType = "byte", ManagedMarshalType = "byte", ByteSize = 1, IsBlittable = true };

    /// <summary>Fills the C and managed names of mirror, handle and enum mappings (recursively
    /// through elements).</summary>
    public static void Finish(TypeMapping mapping)
    {
        var chain = new List<TypeMapping>();
        for (var m = mapping; m != null; m = m.Element)
        {
            chain.Add(m);
        }

        // innermost first, so a slice or list sees its element's final names
        chain.Reverse();
        foreach (var m in chain)
        {
            switch (m.Kind)
            {
                case MappingKind.MirrorStruct:
                    m.CType = m.Mirror.CName;
                    m.ManagedType = m.Mirror.ManagedName;
                    m.ManagedMarshalType = m.Mirror.ManagedName;
                    m.ByteSize = m.Mirror.ByteSize;
                    break;
                case MappingKind.Handle:
                    m.CType = m.Handle.CName + "*";
                    m.ManagedType = m.Handle.ManagedName;
                    break;
                case MappingKind.Enum:
                    m.ManagedType = m.Enum.ManagedName;
                    m.ManagedMarshalType = m.Enum.ManagedName;
                    break;
                case MappingKind.ValueSlice:
                    m.CType = m.Element.Kind == MappingKind.Handle
                        ? m.Element.CType + " const*"
                        : "const " + (m.Element.Kind == MappingKind.Bool ? "uint8_t" : m.Element.CType) + "*";
                    m.ManagedType = "ReadOnlySpan<" + m.Element.ManagedType + ">";
                    m.ManagedMarshalType = m.ManagedType;
                    break;
                case MappingKind.ValueVec:
                    m.ManagedType = m.Element.ManagedType + "[]";
                    break;
            }
        }
    }

    /// <summary>The C alignment of a mapping (bytes).</summary>
    public static int AlignmentOf(TypeMapping mapping) => mapping.Kind switch
    {
        MappingKind.MirrorStruct => mapping.Mirror.Alignment,
        MappingKind.FixedArray => AlignmentOf(mapping.Element),
        MappingKind.Enum => 4,
        _ => Math.Max(1, mapping.ByteSize),
    };

    private static string ArrayText(string primitive, IReadOnlyList<int> dims)
    {
        var text = primitive;
        for (var i = dims.Count - 1; i >= 0; i--)
        {
            text = "[" + text + "; " + dims[i] + "]";
        }

        return text;
    }
}
