using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;

/// <summary>
/// Small factory for hand-built mapping plans, spelled the way the planner spells them (C types,
/// out-parameter names out / out_present / out0..., RustName "return" for outs, receiver
/// RustName "self", handle parameter "handle", value receiver "self_value").
/// </summary>
internal static class PlanKit
{
    private static readonly Dictionary<string, (string C, string Managed, int Size)> Primitives = new Dictionary<string, (string, string, int)>
    {
        ["i8"] = ("int8_t", "sbyte", 1), ["i16"] = ("int16_t", "short", 2), ["i32"] = ("int32_t", "int", 4), ["i64"] = ("int64_t", "long", 8),
        ["isize"] = ("ptrdiff_t", "nint", 8), ["u8"] = ("uint8_t", "byte", 1), ["u16"] = ("uint16_t", "ushort", 2), ["u32"] = ("uint32_t", "uint", 4),
        ["u64"] = ("uint64_t", "ulong", 8), ["usize"] = ("size_t", "nuint", 8), ["f32"] = ("float", "float", 4), ["f64"] = ("double", "double", 8),
    };

    public static MappingPlan Plan(string lib) => new MappingPlan
    {
        CrateName = lib,
        LibName = lib,
        FacadeCrateName = lib + "_facade",
        SymbolPrefix = lib + "_",
        ManagedNamespace = char.ToUpperInvariant(lib[0]) + lib.Substring(1),
        ManagedAssemblyName = char.ToUpperInvariant(lib[0]) + lib.Substring(1),
    };

    public static TypeMapping Prim(string rust)
    {
        var p = Primitives[rust];
        return new TypeMapping { Kind = MappingKind.Primitive, RustType = rust, CType = p.C, ManagedType = p.Managed, ManagedMarshalType = p.Managed, ByteSize = p.Size, IsBlittable = true };
    }

    public static TypeMapping Bool() => new TypeMapping { Kind = MappingKind.Bool, RustType = "bool", CType = "bool", ManagedType = "bool", ManagedMarshalType = "byte", ByteSize = 1, IsBlittable = true };

    public static TypeMapping Unit() => new TypeMapping { Kind = MappingKind.Unit, RustType = "()", CType = "void", ManagedType = "void", ManagedMarshalType = "void", IsBlittable = true };

    public static TypeMapping Mirror(MirrorStruct m, string text = null) => new TypeMapping { Kind = MappingKind.MirrorStruct, RustType = text ?? m.RustTypeName, CType = m.CName, ManagedType = m.ManagedName, ManagedMarshalType = m.ManagedName, Mirror = m, ByteSize = m.ByteSize, IsBlittable = true };

    public static TypeMapping Handle(HandleType h, string text = null) => new TypeMapping { Kind = MappingKind.Handle, RustType = text ?? h.RustTypeName, CType = h.CName + "*", ManagedType = h.ManagedName, ManagedMarshalType = "nint", Handle = h, ByteSize = 8, IsBlittable = true, IsMutable = text != null && text.StartsWith("&mut") };

    public static TypeMapping Enum(EnumMapping e, string text = null) => new TypeMapping { Kind = MappingKind.Enum, RustType = text ?? e.RustTypeName, CType = "int32_t", ManagedType = e.ManagedName, ManagedMarshalType = e.ManagedName, Enum = e, ByteSize = 4, IsBlittable = true };

    public static TypeMapping Array(string prefix, string primitive, params int[] lengths)
    {
        var current = Prim(primitive);
        for (var i = lengths.Length - 1; i >= 0; i--)
        {
            var dims = lengths.Skip(i).ToList();
            var dimText = string.Join("x", dims);
            var text = primitive;
            for (var d = dims.Count - 1; d >= 0; d--)
            {
                text = "[" + text + "; " + dims[d] + "]";
            }

            current = new TypeMapping
            {
                Kind = MappingKind.FixedArray,
                RustType = text,
                CType = prefix + "array_" + primitive + "_" + dimText,
                ManagedType = char.ToUpperInvariant(primitive[0]) + primitive.Substring(1) + "Array" + dimText,
                Element = current,
                ArrayLengths = dims,
                ByteSize = current.ByteSize * lengths[i],
                IsBlittable = true,
            };
            current.ManagedMarshalType = current.ManagedType;
        }

        return current;
    }

    public static TypeMapping Slice(string primitive, bool mutable = false) => new TypeMapping
    {
        Kind = MappingKind.PrimitiveSlice,
        RustType = (mutable ? "&mut [" : "&[") + primitive + "]",
        CType = (mutable ? string.Empty : "const ") + Prim(primitive).CType + "*",
        Element = Prim(primitive),
        IsMutable = mutable,
    };

    public static TypeMapping VecIn(string primitive) => new TypeMapping { Kind = MappingKind.PrimitiveVec, RustType = "Vec<" + primitive + ">", CType = "const " + Prim(primitive).CType + "*", Element = Prim(primitive) };

    public static TypeMapping VecOut(string prefix, string primitive) => new TypeMapping { Kind = MappingKind.PrimitiveVec, RustType = "Vec<" + primitive + ">", CType = prefix + "buffer", ManagedMarshalType = "Buffer", Element = Prim(primitive) };

    public static TypeMapping Str() => new TypeMapping { Kind = MappingKind.Str, RustType = "&str", CType = "const uint8_t*", Element = Prim("u8") };

    public static TypeMapping StringIn() => new TypeMapping { Kind = MappingKind.String, RustType = "String", CType = "const uint8_t*", Element = Prim("u8") };

    public static TypeMapping StringOut(string prefix) => new TypeMapping { Kind = MappingKind.String, RustType = "String", CType = prefix + "buffer", ManagedMarshalType = "Buffer", Element = Prim("u8") };

    public static TypeMapping Buffer(string prefix, MappingKind kind) => new TypeMapping { Kind = kind, RustType = "Buffer", CType = prefix + "buffer", ManagedType = "Buffer", ManagedMarshalType = "Buffer", Element = Prim("u8"), ByteSize = 24, IsBlittable = true };

    public static ParameterMapping In(string rustName, string cName, TypeMapping type, ParameterRole role = ParameterRole.Value)
        => new ParameterMapping { RustName = rustName, CName = cName, ManagedName = cName, Type = type, Role = role };

    public static IEnumerable<ParameterMapping> StrIn(string rustName, TypeMapping type = null)
    {
        yield return In(rustName, rustName + "_ptr", type ?? Str(), ParameterRole.StringData);
        yield return In(rustName, rustName + "_len", Prim("usize"), ParameterRole.StringLength);
    }

    public static IEnumerable<ParameterMapping> SliceIn(string rustName, TypeMapping type)
    {
        yield return In(rustName, rustName + "_ptr", type, ParameterRole.SliceData);
        yield return In(rustName, rustName + "_len", Prim("usize"), ParameterRole.SliceLength);
    }

    public static IEnumerable<ParameterMapping> OptionalIn(string rustName, TypeMapping type, ParameterRole role = ParameterRole.Value)
    {
        yield return In(rustName, "has_" + rustName, Bool(), ParameterRole.InPresent);
        yield return In(rustName, rustName, type, role);
    }

    public static ParameterMapping Out(string cName, TypeMapping type, ParameterRole role = ParameterRole.OutValue)
        => new ParameterMapping { RustName = "return", CName = cName, ManagedName = cName, Type = type, Role = role };

    public static ParameterMapping Receiver(HandleType h, string text = null) => In("self", "handle", Handle(h, text), ParameterRole.Handle);

    public static ParameterMapping ValueReceiver(TypeMapping type) => In("self", "self_value", type);

    public static ExportedFunction Fn(string rustPath, string cName, FunctionKind kind, ReturnShape shape, TypeMapping returnType, string call, params object[] parameters)
    {
        var f = new ExportedFunction
        {
            RustPath = rustPath,
            CName = cName,
            ManagedName = cName,
            Doc = "Calls " + rustPath + ".",
            Kind = kind,
            Return = new ReturnMapping { Shape = shape, Type = returnType ?? Unit() },
            ReturnsStatus = kind is not (FunctionKind.Destroy or FunctionKind.Release or FunctionKind.PassThrough),
            RustCallExpression = call,
        };
        foreach (var p in parameters)
        {
            switch (p)
            {
                case ParameterMapping single:
                    f.Parameters.Add(single);
                    break;
                case IEnumerable<ParameterMapping> many:
                    foreach (var m in many)
                    {
                        f.Parameters.Add(m);
                    }

                    break;
            }
        }

        if (shape is ReturnShape.ResultValue or ReturnShape.ResultHandle or ReturnShape.ResultVoid or ReturnShape.ResultBuffer or ReturnShape.ResultString or ReturnShape.ResultTuple)
        {
            f.Return.ErrorFormat = "Display";
            f.Return.ErrorTypeDisplay = "Error";
        }

        return f;
    }

    public static HandleType AddHandle(MappingPlan plan, string rustPath, string cName, string doc = null)
    {
        var h = new HandleType { RustPath = rustPath, RustTypeName = rustPath.Split("::").Last(), CName = cName, ManagedName = rustPath.Split("::").Last(), Doc = doc ?? string.Empty };
        var destroy = new ExportedFunction
        {
            RustPath = rustPath + "::destroy",
            CName = cName + "_destroy",
            ManagedName = "Destroy",
            Doc = string.Empty,
            Kind = FunctionKind.Destroy,
            Owner = h,
            Return = new ReturnMapping { Shape = ReturnShape.Void, Type = Unit() },
            ReturnsStatus = false,
            OwnerRustPath = rustPath,
        };
        destroy.Parameters.Add(Receiver(h));
        h.Destroy = destroy;
        plan.Handles.Add(h);
        return h;
    }

    public static ExportedFunction Release(MappingPlan plan, string suffix, MappingKind kind)
    {
        var f = new ExportedFunction
        {
            RustPath = suffix,
            CName = plan.SymbolPrefix + suffix,
            ManagedName = suffix,
            Doc = string.Empty,
            Kind = FunctionKind.Release,
            Return = new ReturnMapping { Shape = ReturnShape.Void, Type = Unit() },
            ReturnsStatus = false,
        };
        f.Parameters.Add(In("buffer", "buffer", Buffer(plan.SymbolPrefix, kind)));
        return f;
    }

    /// <summary>Adds the functions in CName order (the planner's order) and links handle methods.</summary>
    public static void Finish(MappingPlan plan, IEnumerable<ExportedFunction> functions)
    {
        var all = functions.Concat(plan.Handles.Select(h => h.Destroy)).OrderBy(f => f.CName, System.StringComparer.Ordinal).ToList();
        foreach (var f in all)
        {
            plan.Functions.Add(f);
            if (f.Owner != null)
            {
                f.Owner.Methods.Add(f);
            }
        }
    }
}
