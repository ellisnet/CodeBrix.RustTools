using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;

/// <summary>
/// The C# spelling of every mapped Rust type, on the two sides of the wrapper: the
/// MANAGED type a consumer sees and the blittable INTEROP type of the
/// <c>LibraryImport</c> declarations (runtime marshalling is disabled, so every interop type
/// is unmanaged: bool travels as <c>byte</c>, enums as <c>int</c>, handles as <c>nint</c>,
/// strings as UTF-8 <c>byte*</c> + <c>nuint</c>, owned results as the facade's
/// <c>Buffer</c> struct).
/// </summary>
internal sealed class ManagedTypes
{
    private static readonly Dictionary<string, string> Primitives = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["i8"] = "sbyte", ["u8"] = "byte", ["i16"] = "short", ["u16"] = "ushort", ["i32"] = "int", ["u32"] = "uint",
        ["i64"] = "long", ["u64"] = "ulong", ["i128"] = "Int128", ["u128"] = "UInt128", ["isize"] = "nint",
        ["usize"] = "nuint", ["f16"] = "Half", ["f32"] = "float", ["f64"] = "double", ["char"] = "uint",
        ["bool"] = "bool",
    };

    private static readonly Dictionary<string, string> ClrNames = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["sbyte"] = "SByte", ["byte"] = "Byte", ["short"] = "Int16", ["ushort"] = "UInt16", ["int"] = "Int32",
        ["uint"] = "UInt32", ["long"] = "Int64", ["ulong"] = "UInt64", ["nint"] = "NInt", ["nuint"] = "NUInt",
        ["float"] = "Single", ["double"] = "Double", ["bool"] = "Boolean", ["Half"] = "Half", ["Int128"] = "Int128",
        ["UInt128"] = "UInt128",
    };

    private readonly SortedDictionary<string, InlineArraySpec> _arrays = new SortedDictionary<string, InlineArraySpec>(StringComparer.Ordinal);

    /// <summary>Creates the mapper for a wrapper whose native class is
    /// <paramref name="nativeClassName"/>.</summary>
    public ManagedTypes(string nativeClassName)
    {
        NativeClassName = nativeClassName;
    }

    /// <summary>The internal static class that holds the native declarations.</summary>
    public string NativeClassName { get; }

    /// <summary>Whether a generated member copies an owned vector (needs the free_buffer
    /// export).</summary>
    public bool UsesTakeArray { get; set; }

    /// <summary>Whether a generated member passes a list of strings (needs the StrRef
    /// struct in the native class).</summary>
    public bool UsesStrRef { get; set; }

    /// <summary>Whether a generated member decodes an owned string (needs the free_string
    /// export).</summary>
    public bool UsesTakeString { get; set; }

    /// <summary>Every inline array met so far, ordinal by name.</summary>
    public IReadOnlyCollection<InlineArraySpec> Arrays => _arrays.Values;

    /// <summary>The C# keyword type of a Rust primitive, or <c>null</c>.</summary>
    public static string PrimitiveOrNull(string rustType)
        => rustType != null && Primitives.TryGetValue(rustType.Trim(), out var cs) ? cs : null;

    /// <summary>The C# type a consumer sees.</summary>
    public string Managed(TypeMapping type)
    {
        switch (Require(type).Kind)
        {
            case MappingKind.Primitive:
                return Primitive(type);
            case MappingKind.Bool:
                return "bool";
            case MappingKind.Unit:
                return "void";
            case MappingKind.MirrorStruct:
                return MirrorName(type);
            case MappingKind.Handle:
                return HandleName(type);
            case MappingKind.Enum:
                return EnumName(type);
            case MappingKind.FixedArray:
                return ArrayType(type);
            case MappingKind.PrimitiveSlice:
                return (IsMutableSlice(type) ? "Span<" : "ReadOnlySpan<") + Element(type) + ">";
            case MappingKind.PrimitiveVec:
                return Element(type) + "[]";
            case MappingKind.ValueSlice:
                return "ReadOnlySpan<" + Managed(ValueElement(type)) + ">";
            case MappingKind.ValueVec:
                return Managed(ValueElement(type)) + "[]";
            case MappingKind.String:
            case MappingKind.Str:
                return "string";
            case MappingKind.RawPointer:
                return "nint";
            default:
                throw Unsupported(type);
        }
    }

    /// <summary>The blittable type of the <c>LibraryImport</c> declarations.</summary>
    public string Interop(TypeMapping type)
    {
        switch (Require(type).Kind)
        {
            case MappingKind.Primitive:
                return Primitive(type) == "bool" ? "byte" : Primitive(type);
            case MappingKind.Bool:
                return "byte";
            case MappingKind.Unit:
                return "void";
            case MappingKind.MirrorStruct:
                return NativeClassName + "." + NativeStructName(type.Mirror ?? throw Unsupported(type));
            case MappingKind.Handle:
            case MappingKind.RawPointer:
                return "nint";
            case MappingKind.Enum:
                return "int";
            case MappingKind.FixedArray:
                return ArrayType(type);
            case MappingKind.PrimitiveSlice:
                return Element(type) + "*";
            case MappingKind.ValueSlice:
                return Interop(ValueElement(type)) + "*";
            case MappingKind.PrimitiveVec:
            case MappingKind.ValueVec:
            case MappingKind.String:
                return NativeClassName + ".Buffer";
            case MappingKind.Str:
                return "byte*";
            default:
                throw Unsupported(type);
        }
    }

    /// <summary>Converts a managed value to its interop form.</summary>
    public string ToInterop(string expression, TypeMapping type)
    {
        switch (Require(type).Kind)
        {
            case MappingKind.Bool:
                return "(byte)(" + expression + " ? 1 : 0)";
            case MappingKind.Primitive when Primitive(type) == "bool":
                return "(byte)(" + expression + " ? 1 : 0)";
            case MappingKind.MirrorStruct:
                return expression + ".ToNative()";
            case MappingKind.Enum:
                return "(int)" + expression;
            default:
                return expression;
        }
    }

    /// <summary>Converts an interop value to its managed form (handles, buffers and
    /// strings are converted by the caller).</summary>
    public string FromInterop(string expression, TypeMapping type)
    {
        switch (Require(type).Kind)
        {
            case MappingKind.Bool:
                return expression + " != 0";
            case MappingKind.Primitive when Primitive(type) == "bool":
                return expression + " != 0";
            case MappingKind.MirrorStruct:
                return MirrorName(type) + ".FromNative(" + expression + ")";
            case MappingKind.Enum:
                return "(" + EnumName(type) + ")" + expression;
            case MappingKind.Handle:
                return HandleName(type) + ".FromNative(" + expression + ")";
            case MappingKind.PrimitiveVec:
                UsesTakeArray = true;
                return NativeClassName + ".TakeArray<" + Element(type) + ">(" + expression + ")";
            case MappingKind.ValueVec:
            {
                UsesTakeArray = true;
                var element = ValueElement(type);
                var interop = Interop(element);
                var managed = Managed(element);
                var each = FromInterop("__e", element);
                var array = NativeClassName + ".TakeArray<" + interop + ">(" + expression + ")";
                return each == "__e" ? array : "Array.ConvertAll<" + interop + ", " + managed + ">(" + array + ", __e => " + each + ")";
            }

            case MappingKind.String:
            case MappingKind.Str:
                UsesTakeString = true;
                return NativeClassName + ".TakeString(" + expression + ")";
            default:
                return expression;
        }
    }

    /// <summary>Whether the managed type is a value type (so an optional one is
    /// <c>T?</c>).</summary>
    public static bool IsValueType(TypeMapping type)
        => type.Kind switch
        {
            MappingKind.Handle or MappingKind.PrimitiveVec or MappingKind.ValueVec or MappingKind.String or MappingKind.Str => false,
            _ => true,
        };

    /// <summary>The C# element type of a slice, vector or array.</summary>
    public string Element(TypeMapping type)
    {
        var element = type.Element;
        if (element != null && element.Kind is MappingKind.Primitive or MappingKind.Bool)
        {
            return element.Kind == MappingKind.Bool ? "bool" : Primitive(element);
        }

        var text = (type.RustType ?? string.Empty).Replace(" ", string.Empty);
        foreach (var prefix in new[] { "&mut[", "&[", "Vec<", "&'static[" })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
            {
                var inner = text.Substring(prefix.Length).TrimEnd(']', '>');
                var cs = PrimitiveOrNull(inner);
                if (cs != null)
                {
                    return cs;
                }
            }
        }

        throw new WrapperGenerationException($"The element type of '{type.RustType}' is not a primitive; the wrapper cannot map it.");
    }

    /// <summary>The element of a <see cref="MappingKind.ValueSlice"/> /
    /// <see cref="MappingKind.ValueVec"/>.</summary>
    public static TypeMapping ValueElement(TypeMapping type)
        => type.Element ?? throw new WrapperGenerationException($"The list type '{type.RustType}' names no element type; the wrapper cannot map it.");

    /// <summary>Whether a slice is <c>&amp;mut [T]</c>.</summary>
    public static bool IsMutableSlice(TypeMapping type)
        => type.IsMutable
            || (type.RustType ?? string.Empty).Replace(" ", string.Empty).StartsWith("&mut", StringComparison.Ordinal)
            || (type.ManagedMarshalType ?? string.Empty).StartsWith("Span<", StringComparison.Ordinal)
            || (type.ManagedType ?? string.Empty).StartsWith("Span<", StringComparison.Ordinal);

    /// <summary>The managed name of a mirror (qualified when a member could hide it).</summary>
    public string MirrorName(TypeMapping type) => Qualify(type.Mirror?.ManagedName ?? type.ManagedType ?? throw Unsupported(type));

    /// <summary>The managed name of a handle class (qualified when a member could hide it).</summary>
    public string HandleName(TypeMapping type) => Qualify(type.Handle?.ManagedName ?? type.ManagedType ?? throw Unsupported(type));

    /// <summary>The managed name of an enum (qualified when a member could hide it).</summary>
    public string EnumName(TypeMapping type) => Qualify(type.Enum?.ManagedName ?? type.ManagedType ?? throw Unsupported(type));

    /// <summary>A generated type's name as code inside the wrapper writes it: escaped, and
    /// written as <c>global::&lt;RootNamespace&gt;.&lt;Name&gt;</c> when a member of the
    /// class being written (<see cref="CurrentClass"/>) may have the same spelling, which
    /// would hide the type inside that class (<c>fn vec2() -&gt; Vec2</c> becomes the method
    /// <c>Vec2</c>).</summary>
    public string Qualify(string managedName)
    {
        var escaped = CSharpNames.Escape(managedName);
        return RootNamespace != null && CurrentClass != null
            && QualifiedTypeNamesByClass.TryGetValue(CurrentClass.TrimStart('@'), out var names) && names.Contains(managedName)
            ? "global::" + RootNamespace + "." + escaped
            : escaped;
    }

    /// <summary>The wrapper's root namespace (used by <see cref="Qualify"/>).</summary>
    public string RootNamespace { get; set; }

    /// <summary>The managed class whose code is being written.</summary>
    public string CurrentClass { get; set; }

    /// <summary>Per class: the generated type names a member of that class may also use;
    /// inside that class they are written fully qualified.</summary>
    public IDictionary<string, ISet<string>> QualifiedTypeNamesByClass { get; } = new Dictionary<string, ISet<string>>(StringComparer.Ordinal);

    /// <summary>The name of a mirror's blittable struct inside the native class
    /// (<c>&lt;ManagedName&gt;Native</c>; the C name is not used because a C struct tag and an
    /// export may share a spelling, which C# members cannot).</summary>
    public static string NativeStructName(MirrorStruct mirror) => CSharpNames.Sanitize(mirror.ManagedName) + "Native";

    /// <summary>The inline-array struct for a fixed-size array type (registered for
    /// generation).</summary>
    public string ArrayType(TypeMapping type)
    {
        var planned = PlannedArrayType(type);
        if (planned != null)
        {
            return planned;
        }

        var lengths = new List<int>();
        var element = ParseArray(type.RustType, lengths);
        if (element == null)
        {
            lengths.Clear();
            var node = type;
            while (node != null && node.Kind == MappingKind.FixedArray)
            {
                if (node.ArrayLengths != null && node.ArrayLengths.Count > 0)
                {
                    lengths.AddRange(node.Element?.Kind == MappingKind.FixedArray ? node.ArrayLengths.Take(1) : node.ArrayLengths);
                }

                node = node.Element;
            }

            element = node == null ? null : node.Kind == MappingKind.Bool ? "bool" : PrimitiveOrNull(node.RustType);
        }

        if (element == null || lengths.Count == 0)
        {
            throw new WrapperGenerationException($"The fixed-size array '{type.RustType}' does not have primitive elements; the wrapper cannot map it.");
        }

        var clr = ClrNames.TryGetValue(element, out var name) ? name : CSharpNames.Pascal(element);
        string inner = element;
        string result = null;
        for (var level = lengths.Count - 1; level >= 0; level--)
        {
            var dims = lengths.Skip(level).Select(l => l.ToString(CultureInfo.InvariantCulture));
            var structName = clr + "Array" + string.Join("x", dims);
            if (!_arrays.ContainsKey(structName))
            {
                _arrays[structName] = new InlineArraySpec { Name = structName, Length = lengths[level], ElementType = inner, RustType = RustArrayText(element, lengths.Skip(level).ToList()) };
            }

            inner = structName;
            result = structName;
        }

        return result;
    }

    /// <summary>The planner's own inline-array names (for example <c>F64Array3x3</c> holding
    /// <c>F64Array3</c>), registered level by level; <c>null</c> when the plan gives none.</summary>
    private string PlannedArrayType(TypeMapping type)
    {
        if (type?.Kind != MappingKind.FixedArray || string.IsNullOrWhiteSpace(type.ManagedType) || CSharpNames.Sanitize(type.ManagedType) != type.ManagedType || type.Element == null)
        {
            return null;
        }

        var length = type.ArrayLengths != null && type.ArrayLengths.Count > 0 ? type.ArrayLengths[0] : 0;
        if (length <= 0)
        {
            var parsed = new List<int>();
            if (ParseArray(type.RustType, parsed) == null || parsed.Count == 0)
            {
                return null;
            }

            length = parsed[0];
        }

        string element;
        if (type.Element.Kind == MappingKind.FixedArray)
        {
            element = PlannedArrayType(type.Element);
            if (element == null)
            {
                return null;
            }
        }
        else if (type.Element.Kind == MappingKind.Bool)
        {
            element = "bool";
        }
        else
        {
            element = PrimitiveOrNull(type.Element.RustType);
            if (element == null)
            {
                return null;
            }
        }

        if (!_arrays.ContainsKey(type.ManagedType))
        {
            _arrays[type.ManagedType] = new InlineArraySpec { Name = type.ManagedType, Length = length, ElementType = element, RustType = type.RustType };
        }

        return type.ManagedType;
    }

    private static string RustArrayText(string csElement, IReadOnlyList<int> lengths)
    {
        var rust = Primitives.FirstOrDefault(p => p.Value == csElement).Key ?? csElement;
        var text = rust;
        for (var i = lengths.Count - 1; i >= 0; i--)
        {
            text = "[" + text + "; " + lengths[i].ToString(CultureInfo.InvariantCulture) + "]";
        }

        return text;
    }

    /// <summary>Parses "[[f64; 3]; 4]" into lengths (outermost first: 4, 3) and the C#
    /// element ("double"); <c>null</c> when it is not that shape.</summary>
    internal static string ParseArray(string rustType, List<int> lengths)
    {
        if (string.IsNullOrWhiteSpace(rustType))
        {
            return null;
        }

        var text = rustType.Trim();
        while (text.StartsWith('[') && text.EndsWith(']'))
        {
            var inner = text.Substring(1, text.Length - 2);
            var depth = 0;
            var split = -1;
            for (var i = 0; i < inner.Length; i++)
            {
                if (inner[i] == '[')
                {
                    depth++;
                }
                else if (inner[i] == ']')
                {
                    depth--;
                }
                else if (inner[i] == ';' && depth == 0)
                {
                    split = i;
                }
            }

            if (split < 0 || !int.TryParse(inner.Substring(split + 1).Trim().Replace("_", string.Empty), NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
            {
                return null;
            }

            lengths.Add(length);
            text = inner.Substring(0, split).Trim();
        }

        return lengths.Count == 0 ? null : PrimitiveOrNull(text);
    }

    private static string Primitive(TypeMapping type)
        => PrimitiveOrNull(type.RustType)
            ?? (string.IsNullOrWhiteSpace(type.ManagedType) ? throw Unsupported(type) : type.ManagedType);

    private static TypeMapping Require(TypeMapping type)
        => type ?? throw new WrapperGenerationException("The mapping plan has a parameter or result without a type mapping.");

    private static WrapperGenerationException Unsupported(TypeMapping type)
        => new WrapperGenerationException($"The wrapper cannot spell the type '{type?.RustType}' ({type?.Kind}) in C#; the mapping plan is incomplete.");
}
