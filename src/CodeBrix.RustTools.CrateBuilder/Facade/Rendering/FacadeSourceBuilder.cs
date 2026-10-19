using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.WebGpu;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// Turns a <see cref="MappingPlan"/> into the <see cref="FacadeSourceModel"/> the templates
/// render: the FFI types, conversions, checks and the body of every export. All type decisions
/// of the facade live here (the templates only lay text out).
/// </summary>
internal sealed class FacadeSourceBuilder
{
    private const string Support = "crate::facade_support::";

    private static readonly Dictionary<string, string> PrimitiveByCType = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["int8_t"] = "i8", ["int16_t"] = "i16", ["int32_t"] = "i32", ["int64_t"] = "i64", ["ptrdiff_t"] = "isize",
        ["uint8_t"] = "u8", ["uint16_t"] = "u16", ["uint32_t"] = "u32", ["uint64_t"] = "u64", ["size_t"] = "usize",
        ["float"] = "f32", ["double"] = "f64",
    };

    private static readonly HashSet<string> RustPrimitives = new HashSet<string>(PrimitiveByCType.Values, StringComparer.Ordinal);

    private readonly MappingPlan _plan;
    private readonly Dictionary<MirrorStruct, bool> _fallible = new Dictionary<MirrorStruct, bool>();
    private readonly SortedDictionary<string, ArrayView> _arrays = new SortedDictionary<string, ArrayView>(StringComparer.Ordinal);

    /// <summary>Creates a builder for <paramref name="plan"/>.</summary>
    public FacadeSourceBuilder(MappingPlan plan)
    {
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    /// <summary>The exported symbol of the last-error function.</summary>
    public string LastErrorExport => _plan.SymbolPrefix + FacadeConventions.LastErrorMessageSuffix;

    /// <summary>Builds the render model. Throws <see cref="FacadeGenerationException"/> for a plan
    /// the facade cannot express.</summary>
    public FacadeSourceModel Build(string packageName, string version, string upstreamDependency, IReadOnlyList<string> extraModules)
    {
        Validate();
        var prefix = _plan.SymbolPrefix;
        var model = new FacadeSourceModel
        {
            PackageName = packageName,
            Version = version,
            FacadeCrateName = _plan.FacadeCrateName,
            LibName = _plan.LibName,
            Prefix = prefix,
            PrefixUpper = prefix.ToUpperInvariant(),
            UpstreamDependency = upstreamDependency,
            LinkToWebGpu = _plan.LinkToCodeBrixWebGpu,
            HeaderFileName = _plan.LibName + ".h",
        };
        if (_plan.LinkToCodeBrixWebGpu)
        {
            model.WebGpuModuleLines = WebGpuLinkRecipe.ModuleLines.ToList();
            model.BuildScriptLines = WebGpuLinkRecipe.BuildScriptLines.ToList();
        }

        foreach (var m in extraModules)
        {
            model.ExtraModules.Add(m);
        }

        foreach (var e in _plan.Enums)
        {
            model.Enums.Add(BuildEnum(e));
        }

        foreach (var m in _plan.Mirrors)
        {
            model.Mirrors.Add(BuildMirror(m));
        }

        foreach (var h in _plan.Handles)
        {
            model.Handles.Add(new HandleView
            {
                DocLines = RustSyntax.CommentLines(h.Doc, "///").ToList(),
                CDocLines = RustSyntax.CommentLines(h.Doc, "//").ToList(),
                CName = h.CName,
                UpstreamType = RustSyntax.Upstream(h.RustPath),
            });
        }

        model.UsesStrRef = _plan.Functions.SelectMany(f => f.Parameters).Any(p => p.Type?.Kind == MappingKind.ValueSlice && p.Type.Element?.Kind is MappingKind.Str or MappingKind.String);
        model.Exports.Add(LastErrorExport);
        foreach (var f in _plan.Functions)
        {
            model.Functions.Add(BuildFunction(f));
            model.Exports.Add(f.CName);
        }

        foreach (var o in _plan.Overrides.Where(o => !string.IsNullOrWhiteSpace(o.RustSource)))
        {
            model.Overrides.Add(new OverrideView
            {
                RustPath = RustSyntax.OneLine(o.RustPath),
                Lines = o.RustSource.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n').Select(l => l.TrimEnd()).ToList(),
            });
        }

        model.Arrays = _arrays.Values.ToList();
        return model;
    }

    // ------------------------------------------------------------------ validation

    private void Validate()
    {
        Require(RustSyntax.IsIdentifier(_plan.LibName), $"the library name '{_plan.LibName}' is not an identifier");
        Require(!string.IsNullOrWhiteSpace(_plan.FacadeCrateName) && _plan.FacadeCrateName.All(c => char.IsAsciiLetterOrDigit(c) || c == '_' || c == '-'), $"the facade crate name '{_plan.FacadeCrateName}' is not a valid package name");
        Require(!string.IsNullOrEmpty(_plan.SymbolPrefix) && RustSyntax.IsIdentifier(_plan.SymbolPrefix.TrimEnd('_') + "x"), $"the symbol prefix '{_plan.SymbolPrefix}' is not usable");

        var names = new HashSet<string>(StringComparer.Ordinal) { LastErrorExport, _plan.SymbolPrefix + FacadeConventions.BufferTypeSuffix };
        void Name(string name, string what)
        {
            Require(RustSyntax.IsCSymbol(name), $"the C name '{name}' of {what} is not a usable C identifier");
            Require(names.Add(name), $"the C name '{name}' ({what}) is used twice");
        }

        foreach (var h in _plan.Handles)
        {
            Name(h.CName, "handle " + h.RustPath);
        }

        foreach (var m in _plan.Mirrors)
        {
            Name(m.CName, "mirror " + m.RustPath);
        }

        foreach (var e in _plan.Enums)
        {
            Name(e.CName, "enum " + e.RustPath);
            foreach (var v in e.Variants)
            {
                Name(v.CName, "variant " + e.RustPath + "::" + v.RustName);
                Require(v.Value >= int.MinValue && v.Value <= int.MaxValue, $"the value of {e.RustPath}::{v.RustName} does not fit int32_t");
            }
        }

        foreach (var f in _plan.Functions)
        {
            Name(f.CName, "function " + f.RustPath);
            Require(f.Kind != FunctionKind.PassThrough, $"{f.RustPath} is a pass-through export, but the plan is not in pass-through mode");
            Require(f.Return != null, $"{f.RustPath} has no return mapping");
        }

        foreach (var a in _plan.Functions.SelectMany(f => f.Parameters).Select(p => p.Type).Concat(_plan.Mirrors.SelectMany(m => m.Fields).Select(x => x.Type)).Where(t => t?.Kind == MappingKind.FixedArray))
        {
            Require(!names.Contains(a.CType) || _arrays.ContainsKey(a.CType), $"the array struct name '{a.CType}' is also used by another item");
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new FacadeGenerationException("The mapping plan cannot be rendered as a facade: " + message + ".");
        }
    }

    // ------------------------------------------------------------------ types

    /// <summary>The Rust primitive behind a primitive mapping (aliases resolved through the C
    /// type).</summary>
    private static string PrimitiveOf(TypeMapping t)
    {
        if (t.CType != null && PrimitiveByCType.TryGetValue(t.CType, out var p))
        {
            return p;
        }

        if (t.RustType != null && RustPrimitives.Contains(t.RustType))
        {
            return t.RustType;
        }

        throw new FacadeGenerationException($"The mapping plan uses the primitive {t.RustType} ({t.CType}), which the facade does not support.");
    }

    /// <summary>The Rust FFI type of a value (parameter, field, out target).</summary>
    private string FfiType(TypeMapping t)
    {
        switch (t.Kind)
        {
            case MappingKind.Primitive:
                return PrimitiveOf(t);
            case MappingKind.Bool:
                return "u8";
            case MappingKind.Enum:
                return "i32";
            case MappingKind.MirrorStruct:
                return t.Mirror?.CName ?? t.CType;
            case MappingKind.FixedArray:
                return ArrayStruct(t).CName;
            case MappingKind.Handle:
                return "*mut " + HandleName(t);
            case MappingKind.String:
            case MappingKind.PrimitiveVec:
            case MappingKind.ValueVec:
                return "Buffer";
            default:
                throw new FacadeGenerationException($"The mapping plan passes a {t.Kind} ({t.RustType}) by value, which the facade does not support.");
        }
    }

    private static string HandleName(TypeMapping t) => t.Handle?.CName ?? t.CType?.TrimEnd('*').Trim()
        ?? throw new FacadeGenerationException($"A handle mapping ({t.RustType}) names no handle type.");

    /// <summary>The innermost element and the lengths (outermost first) of a fixed array.</summary>
    private static (TypeMapping Element, IReadOnlyList<int> Lengths) ArrayShape(TypeMapping t)
    {
        var lengths = new List<int>();
        var current = t;
        while (current.Kind == MappingKind.FixedArray)
        {
            var n = current.ArrayLengths?.Count > 0 ? current.ArrayLengths[0] : throw new FacadeGenerationException($"The fixed array {t.RustType} has no length.");
            lengths.Add(n);
            current = current.Element ?? throw new FacadeGenerationException($"The fixed array {t.RustType} has no element type.");
        }

        if (current.Kind != MappingKind.Primitive)
        {
            throw new FacadeGenerationException($"The fixed array {t.RustType} holds {current.Kind} elements; the facade supports arrays of numbers only.");
        }

        return (current, lengths);
    }

    private ArrayView ArrayStruct(TypeMapping t)
    {
        var name = t.CType ?? throw new FacadeGenerationException($"The fixed array {t.RustType} has no C type name.");
        if (_arrays.TryGetValue(name, out var view))
        {
            return view;
        }

        var (element, lengths) = ArrayShape(t);
        var rust = PrimitiveOf(element);
        for (var i = lengths.Count - 1; i >= 0; i--)
        {
            rust = "[" + rust + "; " + lengths[i].ToString(CultureInfo.InvariantCulture) + "]";
        }

        view = new ArrayView
        {
            CName = name,
            RustType = rust,
            CDeclaration = element.CType + " values" + string.Concat(lengths.Select(l => "[" + l.ToString(CultureInfo.InvariantCulture) + "]")),
            ByteSize = t.ByteSize,
        };
        _arrays.Add(name, view);
        return view;
    }

    private bool IsFallible(MirrorStruct m)
    {
        if (_fallible.TryGetValue(m, out var known))
        {
            return known;
        }

        _fallible[m] = false;
        var result = m.Fields.Any(f => f.Type.Kind is MappingKind.Bool or MappingKind.Enum
            || (f.Type.Kind == MappingKind.MirrorStruct && IsFallible(MirrorOf(f.Type))));
        _fallible[m] = result;
        return result;
    }

    private MirrorStruct MirrorOf(TypeMapping t)
        => t.Mirror ?? _plan.Mirrors.FirstOrDefault(m => m.CName == t.CType) ?? throw new FacadeGenerationException($"The mirror type {t.RustType} is not in the plan.");

    private EnumMapping EnumOf(TypeMapping t)
        => t.Enum ?? throw new FacadeGenerationException($"The enum type {t.RustType} names no enum mapping.");

    /// <summary>An upstream-typed value converted to its FFI form.</summary>
    private string ToFfi(TypeMapping t, string expr)
    {
        switch (t.Kind)
        {
            case MappingKind.Primitive:
                return expr;
            case MappingKind.Bool:
                return "(" + expr + ") as u8";
            case MappingKind.Enum:
                return EnumOf(t).CName + "::from(" + expr + ") as i32";
            case MappingKind.MirrorStruct:
                return MirrorOf(t).CName + "::from(" + expr + ")";
            case MappingKind.FixedArray:
                return ArrayStruct(t).CName + " { values: " + expr + " }";
            default:
                throw new FacadeGenerationException($"The facade cannot convert a {t.Kind} ({t.RustType}) to C.");
        }
    }

    /// <summary>An FFI value converted to its upstream type; <paramref name="fallible"/> = the
    /// expression is a <c>Result&lt;T, ()&gt;</c>.</summary>
    private string FromFfi(TypeMapping t, string expr, out bool fallible)
    {
        fallible = false;
        switch (t.Kind)
        {
            case MappingKind.Primitive:
                return expr;
            case MappingKind.Bool:
                fallible = true;
                return Support + "bool_from(" + expr + ")";
            case MappingKind.Enum:
            {
                fallible = true;
                var e = EnumOf(t);
                return e.CName + "::try_from(" + expr + ").map(" + RustSyntax.Upstream(e.RustPath) + "::from)";
            }

            case MappingKind.MirrorStruct:
            {
                var m = MirrorOf(t);
                fallible = IsFallible(m);
                return RustSyntax.Upstream(m.RustPath) + (fallible ? "::try_from(" : "::from(") + expr + ")";
            }

            case MappingKind.FixedArray:
                ArrayStruct(t);
                return "(" + expr + ").values";
            default:
                throw new FacadeGenerationException($"The facade cannot convert a C value to {t.Kind} ({t.RustType}).");
        }
    }

    private static string InvalidText(TypeMapping t) => t.Kind switch
    {
        MappingKind.Bool => "must be 0 (false) or 1 (true)",
        MappingKind.Enum => "is not a value of the enum " + (t.Enum?.RustTypeName ?? t.RustType),
        MappingKind.MirrorStruct => "holds a field value that " + (t.Mirror?.RustTypeName ?? t.RustType) + " does not accept (a bool other than 0 / 1 or an enum value the enum does not have)",
        _ => "is not valid",
    };

    // ------------------------------------------------------------------ enums and mirrors

    private EnumView BuildEnum(EnumMapping e)
    {
        var view = new EnumView
        {
            DocLines = RustSyntax.CommentLines(e.Doc, "///").ToList(),
            CDocLines = RustSyntax.CommentLines(e.Doc, "//").ToList(),
            CName = e.CName,
            UpstreamType = RustSyntax.Upstream(e.RustPath),
        };
        foreach (var v in e.Variants)
        {
            view.Variants.Add(new EnumVariantView
            {
                DocLines = RustSyntax.CommentLines(v.Doc, "///").ToList(),
                CDocLines = RustSyntax.CommentLines(v.Doc, "//").ToList(),
                Name = RustSyntax.Ident(v.RustName),
                CName = v.CName,
                Value = v.Value.ToString(CultureInfo.InvariantCulture),
            });
        }

        return view;
    }

    private MirrorView BuildMirror(MirrorStruct m)
    {
        var view = new MirrorView
        {
            DocLines = RustSyntax.CommentLines(m.Doc, "///").ToList(),
            CDocLines = RustSyntax.CommentLines(m.Doc, "//").ToList(),
            CName = m.CName,
            UpstreamType = RustSyntax.Upstream(m.RustPath),
            Fallible = IsFallible(m),
        };
        Require(m.Fields.Count > 0, $"the mirror {m.RustPath} has no fields");
        var upstreamArgs = new List<string>();
        for (var i = 0; i < m.Fields.Count; i++)
        {
            var f = m.Fields[i];
            var name = RustSyntax.Ident(f.CName);
            var upstreamField = m.IsTupleStruct ? i.ToString(CultureInfo.InvariantCulture) : RustSyntax.Ident(f.Name);
            Require(f.Type.Kind is MappingKind.Primitive or MappingKind.Bool or MappingKind.Enum or MappingKind.MirrorStruct or MappingKind.FixedArray, $"the field {m.RustPath}::{f.Name} is a {f.Type.Kind}");
            view.Fields.Add(new MirrorFieldView
            {
                DocLines = RustSyntax.CommentLines(f.Doc, "///").ToList(),
                CDocLines = RustSyntax.CommentLines(f.Doc, "//").ToList(),
                Name = name,
                RustType = FfiType(f.Type),
                CDeclaration = (f.Type.Kind == MappingKind.Bool ? "bool" : f.Type.CType) + " " + RustSyntax.CParameter(f.CName),
            });
            view.FromUpstreamLines.Add(name + ": " + ToFfi(f.Type, "v." + upstreamField) + ",");
            var conversion = FromFfi(f.Type, "c." + name, out var fallible);
            if (fallible)
            {
                conversion += "?";
            }

            upstreamArgs.Add(m.IsTupleStruct ? conversion : RustSyntax.Ident(f.Name) + ": " + conversion);
            if (m.ByteSize > 0)
            {
                view.LayoutAssertions.Add($"const _: () = assert!(::core::mem::offset_of!({m.CName}, {name}) == {f.Offset.ToString(CultureInfo.InvariantCulture)});");
            }
        }

        if (m.ByteSize > 0)
        {
            view.LayoutAssertions.Insert(0, $"const _: () = assert!(::core::mem::size_of::<{m.CName}>() == {m.ByteSize.ToString(CultureInfo.InvariantCulture)});");
            if (m.Alignment > 0)
            {
                view.LayoutAssertions.Insert(1, $"const _: () = assert!(::core::mem::align_of::<{m.CName}>() == {m.Alignment.ToString(CultureInfo.InvariantCulture)});");
            }
        }

        if (m.IsTupleStruct)
        {
            view.ToUpstreamLines.Add(view.UpstreamType + "(" + string.Join(", ", upstreamArgs) + ")");
        }
        else
        {
            view.ToUpstreamLines.Add(view.UpstreamType + " {");
            view.ToUpstreamLines.AddRangeOf(upstreamArgs.Select(a => "    " + a + ","));
            view.ToUpstreamLines.Add("}");
        }

        return view;
    }

    // ------------------------------------------------------------------ functions

    private sealed class Group
    {
        public string RustName;
        public List<ParameterMapping> Members = new List<ParameterMapping>();
        public ParameterMapping Get(ParameterRole role) => Members.FirstOrDefault(m => m.Role == role);
    }

    private FunctionView BuildFunction(ExportedFunction f)
    {
        var view = new FunctionView
        {
            DocLines = RustSyntax.CommentLines(f.Doc, "///").ToList(),
            CDocLines = RustSyntax.CommentLines(f.Doc, "//").ToList(),
            CName = f.CName,
        };
        var rustParams = new List<string>();
        var cParams = new List<string>();
        foreach (var p in f.Parameters)
        {
            rustParams.Add(RustSyntax.Ident(p.CName) + ": " + RustParameterType(f, p));
            cParams.Add(CParameterType(p) + " " + RustSyntax.CParameter(p.CName));
        }

        var returnsStatus = f.Kind is not (FunctionKind.Destroy or FunctionKind.Release);
        view.RustParameters = string.Join(", ", rustParams);
        view.RustReturn = returnsStatus ? " -> i32" : string.Empty;
        view.CPrototype = (returnsStatus ? "int32_t " : "void ") + f.CName + "(" + (cParams.Count == 0 ? "void" : string.Join(", ", cParams)) + ");";
        view.BodyLines = f.Kind switch
        {
            FunctionKind.Destroy => DestroyBody(f),
            FunctionKind.Release => ReleaseBody(f),
            _ => StatusBody(f),
        };
        return view;
    }

    private string RustParameterType(ExportedFunction f, ParameterMapping p)
    {
        switch (p.Role)
        {
            case ParameterRole.Value:
                return f.Kind == FunctionKind.Release ? "Buffer" : FfiType(p.Type);
            case ParameterRole.Handle:
                return "*mut " + HandleName(p.Type);
            case ParameterRole.SliceData when p.Type.Kind == MappingKind.ValueSlice && p.Type.Element.Kind is MappingKind.Str or MappingKind.String:
                return "*const " + Support + "StrRef";
            case ParameterRole.SliceData when p.Type.Kind == MappingKind.ValueSlice:
                return "*const " + FfiType(p.Type.Element);
            case ParameterRole.SliceData:
                return (p.Type.IsMutable ? "*mut " : "*const ") + SliceElement(p.Type);
            case ParameterRole.StringData:
                return "*const u8";
            case ParameterRole.SliceLength:
            case ParameterRole.StringLength:
                return "usize";
            case ParameterRole.InPresent:
                return "u8";
            case ParameterRole.OutPresent:
                return "*mut u8";
            case ParameterRole.OutValue:
            case ParameterRole.OutBuffer:
                return "*mut " + FfiType(p.Type);
            default:
                throw new FacadeGenerationException($"{f.RustPath}: the parameter role {p.Role} ({p.CName}) is not supported by the facade (an owned result is one Buffer out-parameter).");
        }
    }

    private static string CParameterType(ParameterMapping p) => p.Role switch
    {
        ParameterRole.SliceLength or ParameterRole.StringLength => "size_t",
        ParameterRole.InPresent => "bool",
        ParameterRole.OutPresent => "bool*",
        ParameterRole.OutValue or ParameterRole.OutBuffer => (p.Type.Kind == MappingKind.Bool ? "bool" : p.Type.CType) + "*",
        _ => p.Type.Kind == MappingKind.Bool ? "bool" : p.Type.CType,
    };

    private static string SliceElement(TypeMapping t)
    {
        if (t.Element != null)
        {
            return PrimitiveOf(t.Element);
        }

        var text = (t.RustType ?? string.Empty).Replace("&mut", string.Empty).Replace("&", string.Empty).Trim().TrimStart('[').TrimEnd(']').Trim();
        if (text.StartsWith("Vec<", StringComparison.Ordinal))
        {
            text = text.Substring(4).TrimEnd('>');
        }

        return RustPrimitives.Contains(text) ? text : throw new FacadeGenerationException($"The slice type {t.RustType} has no number element type.");
    }

    private static List<string> DestroyBody(ExportedFunction f)
    {
        var handle = f.Parameters.FirstOrDefault(p => p.Role == ParameterRole.Handle)
            ?? throw new FacadeGenerationException($"{f.CName}: a destroy function needs its handle parameter.");
        var name = RustSyntax.Ident(handle.CName);
        return new List<string>
        {
            $"if {name}.is_null() {{",
            "    return;",
            "}",
            $"let _ = ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| unsafe {{ ::std::mem::drop(::std::boxed::Box::from_raw({name})) }}));",
        };
    }

    private static List<string> ReleaseBody(ExportedFunction f)
    {
        var buffer = f.Parameters.FirstOrDefault()
            ?? throw new FacadeGenerationException($"{f.CName}: a release function needs its buffer parameter.");
        return new List<string> { $"unsafe {{ {Support}release_buffer({RustSyntax.Ident(buffer.CName)}) }}" };
    }

    private List<string> StatusBody(ExportedFunction f)
    {
        // Every C parameter is read into an internal __cb_* temporary first, and the upstream
        // locals (named after the Rust parameters, "this" for the receiver) are bound only right
        // before the call, so a Rust parameter name can never shadow a C parameter still in use.
        var body = new List<string> { Support + "clear_last_error();" };
        var outs = f.Parameters.Where(p => p.Role is ParameterRole.OutValue or ParameterRole.OutPresent or ParameterRole.OutBuffer).ToList();
        var outNames = new Dictionary<ParameterMapping, string>();
        for (var i = 0; i < outs.Count; i++)
        {
            var o = outs[i];
            var temp = "__cb_o" + i.ToString(CultureInfo.InvariantCulture);
            outNames[o] = temp;
            body.Add($"let {temp} = {RustSyntax.Ident(o.CName)};");
            body.Add($"if {temp}.is_null() {{ return {Support}null_argument({RustSyntax.RustString(o.CName)}); }}");
        }

        // logical parameters, in order of first appearance
        var groups = new List<Group>();
        foreach (var p in f.Parameters.Where(p => !outs.Contains(p)))
        {
            var g = groups.FirstOrDefault(x => x.RustName == p.RustName && !x.Members.Any(m => m.Role == p.Role));
            if (g == null)
            {
                g = new Group { RustName = p.RustName };
                groups.Add(g);
            }

            g.Members.Add(p);
        }

        var handleGroups = new List<(int Index, Group Group, ParameterMapping Handle, string Access)>();
        var args = new List<string>();
        var bindings = new List<string>();
        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            var temp = "__cb_a" + i.ToString(CultureInfo.InvariantCulture);
            var local = RustSyntax.Local(g.RustName);
            var present = g.Get(ParameterRole.InPresent);
            var handle = g.Get(ParameterRole.Handle);
            var value = g.Get(ParameterRole.Value);
            var sliceData = g.Get(ParameterRole.SliceData);
            var stringData = g.Get(ParameterRole.StringData);
            bindings.Add($"let {local} = {temp};");
            args.Add(local);
            if (handle != null)
            {
                var receiver = g.RustName == "self";
                var access = receiver
                    ? f.Kind switch { FunctionKind.MutMethod => "handle_mut", FunctionKind.ConsumingMethod => "handle_take", _ => "handle_ref" }
                    : (handle.Type.IsMutable || (handle.Type.RustType ?? string.Empty).Replace(" ", string.Empty).Contains("&mut", StringComparison.Ordinal)) ? "handle_mut" : "handle_ref";
                handleGroups.Add((i, g, handle, access));
                body.Add($"let __cb_p{i.ToString(CultureInfo.InvariantCulture)} = {RustSyntax.Ident(handle.CName)};");
                if (present != null)
                {
                    body.Add($"let __cb_f{i.ToString(CultureInfo.InvariantCulture)} = match {Support}flag({RustSyntax.Ident(present.CName)}, {RustSyntax.RustString(present.CName)}) {{ Ok(v) => v, Err(s) => return s }};");
                }

                continue;
            }

            if (value != null)
            {
                var conversion = FromFfi(value.Type, RustSyntax.Ident(value.CName), out var fallible);
                var converted = fallible
                    ? $"match {conversion} {{ Ok(v) => v, Err(_) => return {Support}invalid_argument({RustSyntax.RustString(value.CName)}, {RustSyntax.RustString(InvalidText(value.Type))}) }}"
                    : conversion;
                body.Add(present != null
                    ? $"let {temp} = match {Support}flag({RustSyntax.Ident(present.CName)}, {RustSyntax.RustString(present.CName)}) {{ Ok(true) => Some({converted}), Ok(false) => None, Err(s) => return s }};"
                    : $"let {temp} = {converted};");
                continue;
            }

            if (sliceData != null)
            {
                var length = g.Get(ParameterRole.SliceLength) ?? throw new FacadeGenerationException($"{f.RustPath}: the slice parameter {sliceData.CName} has no length parameter.");
                var fn = sliceData.Type.IsMutable ? "slice_mut" : "slice";
                body.Add($"let {temp} = match {Support}{fn}({RustSyntax.Ident(sliceData.CName)}, {RustSyntax.Ident(length.CName)}, {RustSyntax.RustString(g.RustName)}) {{ Ok(v) => v, Err(s) => return s }};");
                if (sliceData.Type.Kind == MappingKind.ValueSlice && sliceData.Type.Element.Kind is MappingKind.Handle or MappingKind.Str or MappingKind.String)
                {
                    // objects are cloned out of their boxes, strings checked as UTF-8 (an owned
                    // String is copied); a failing element returns its status
                    var element = sliceData.Type.Element;
                    var name = RustSyntax.RustString(g.RustName);
                    var item = element.Kind switch
                    {
                        MappingKind.Handle => $"{Support}handle_ref(*__cb_e, {name}).map(::std::clone::Clone::clone)",
                        MappingKind.String => $"{Support}utf8(__cb_e.data, __cb_e.len, {name}).map(::std::string::String::from)",
                        _ => $"{Support}utf8(__cb_e.data, __cb_e.len, {name})",
                    };
                    body.Add($"let {temp}: ::std::vec::Vec<_> = {{ let mut __cb_items = ::std::vec::Vec::with_capacity({temp}.len()); for __cb_e in {temp}.iter() {{ match {item} {{ Ok(v) => __cb_items.push(v), Err(s) => return s }} }} __cb_items }};");
                }
                else if (sliceData.Type.Kind == MappingKind.ValueSlice)
                {
                    // every element is converted to the upstream type (a copy the call borrows)
                    var element = sliceData.Type.Element;
                    var conversion = FromFfi(element, "*__cb_v", out var fallible);
                    body.Add(fallible
                        ? $"let {temp}: ::std::vec::Vec<_> = match {temp}.iter().map(|__cb_v| {conversion}).collect::<::std::result::Result<::std::vec::Vec<_>, _>>() {{ Ok(v) => v, Err(_) => return {Support}invalid_argument({RustSyntax.RustString(sliceData.CName)}, {RustSyntax.RustString("has an element that " + InvalidText(element))}) }};"
                        : $"let {temp}: ::std::vec::Vec<_> = {temp}.iter().map(|__cb_v| {conversion}).collect();");
                }

                continue;
            }

            if (stringData != null)
            {
                var length = g.Get(ParameterRole.StringLength) ?? throw new FacadeGenerationException($"{f.RustPath}: the string parameter {stringData.CName} has no length parameter.");
                var text = $"match {Support}utf8({RustSyntax.Ident(stringData.CName)}, {RustSyntax.Ident(length.CName)}, {RustSyntax.RustString(g.RustName)}) {{ Ok(v) => v, Err(s) => return s }}";
                body.Add(present != null
                    ? $"let {temp} = match {Support}flag({RustSyntax.Ident(present.CName)}, {RustSyntax.RustString(present.CName)}) {{ Ok(true) => Some({text}), Ok(false) => None, Err(s) => return s }};"
                    : $"let {temp} = {text};");
                continue;
            }

            throw new FacadeGenerationException($"{f.RustPath}: the parameter group '{g.RustName}' ({string.Join(", ", g.Members.Select(m => m.Role))}) is not a shape the facade knows.");
        }

        // objects: the same object twice where one is changed or used up is refused
        for (var i = 0; i < handleGroups.Count; i++)
        {
            for (var j = i + 1; j < handleGroups.Count; j++)
            {
                var a = handleGroups[i];
                var b = handleGroups[j];
                if (HandleName(a.Handle.Type) == HandleName(b.Handle.Type) && (a.Access != "handle_ref" || b.Access != "handle_ref"))
                {
                    var an = "__cb_p" + a.Index.ToString(CultureInfo.InvariantCulture);
                    var bn = "__cb_p" + b.Index.ToString(CultureInfo.InvariantCulture);
                    body.Add($"if !{an}.is_null() && {an} == {bn} {{ return {Support}invalid_argument({RustSyntax.RustString(b.Handle.CName)}, {RustSyntax.RustString("is the same object as " + a.Handle.CName + ", which this call changes")}); }}");
                }
            }
        }

        foreach (var (index, g, h, access) in handleGroups.OrderBy(x => x.Access == "handle_take" ? 1 : 0))
        {
            var n = index.ToString(CultureInfo.InvariantCulture);
            var get = $"match {Support}{access}(__cb_p{n}, {RustSyntax.RustString(h.CName)}) {{ Ok(v) => v, Err(s) => return s }}";
            body.Add(g.Get(ParameterRole.InPresent) != null
                ? $"let __cb_a{n} = if __cb_f{n} {{ Some({get}) }} else {{ None }};"
                : $"let __cb_a{n} = {get};");
        }

        body.AddRange(bindings);

        // the call
        var call = string.IsNullOrWhiteSpace(f.RustCallExpression) ? FallbackCall(f, args) : f.RustCallExpression.Trim();
        if (f.IsUnsafeUpstream)
        {
            call = "unsafe { " + call + " }";
        }

        body.Add($"let __cb_call = ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| {call}));");
        body.Add("let __cb_value = match __cb_call {");
        body.Add("    Ok(v) => v,");
        body.Add("    Err(payload) => {");
        foreach (var (index, _, _, access) in handleGroups.Where(x => x.Access != "handle_take"))
        {
            body.Add($"        unsafe {{ {Support}poison(__cb_p{index.ToString(CultureInfo.InvariantCulture)}) }};");
        }

        body.Add($"        return {Support}panicked(payload);");
        body.Add("    }");
        body.Add("};");

        var shape = f.Return.Shape;
        if (shape is ReturnShape.ResultValue or ReturnShape.ResultHandle or ReturnShape.ResultVoid or ReturnShape.ResultBuffer or ReturnShape.ResultString or ReturnShape.ResultTuple)
        {
            body.Add("let __cb_value = match __cb_value {");
            body.Add("    Ok(v) => v,");
            body.Add($"    Err(e) => return {Support}fail({Support}STATUS_UPSTREAM_ERROR, {ErrorText(f.Return.ErrorFormat)}),");
            body.Add("};");
        }

        var values = outs.Where(o => o.Role != ParameterRole.OutPresent).ToList();
        switch (shape)
        {
            case ReturnShape.Void:
            case ReturnShape.ResultVoid:
                Require(values.Count == 0, $"{f.RustPath} returns nothing but has out-parameters");
                break;
            case ReturnShape.Tuple:
            case ReturnShape.ResultTuple:
            {
                Require(values.Count > 0, $"{f.RustPath} returns a tuple but has no out-parameters");
                var names = values.Select((_, i) => "__cb_" + i.ToString(CultureInfo.InvariantCulture)).ToList();
                body.Add("let (" + string.Join(", ", names) + (names.Count == 1 ? ",)" : ")") + " = __cb_value;");
                for (var i = 0; i < values.Count; i++)
                {
                    body.Add(Write(values[i], outNames[values[i]], names[i]));
                }

                break;
            }

            case ReturnShape.OptionalValue:
            {
                var present = outs.FirstOrDefault(o => o.Role == ParameterRole.OutPresent);
                Require(present != null && values.Count == 1, $"{f.RustPath} returns an optional value but does not have one present flag and one value out-parameter");
                body.Add("match __cb_value {");
                body.Add("    Some(__cb_some) => {");
                body.Add($"        {outNames[present]}.write(1);");
                body.Add("        " + Write(values[0], outNames[values[0]], "__cb_some"));
                body.Add("    }");
                body.Add($"    None => {outNames[present]}.write(0),");
                body.Add("}");
                break;
            }

            case ReturnShape.OptionalHandle:
            {
                Require(values.Count == 1, $"{f.RustPath} returns an optional object but does not have one out-parameter");
                body.Add("match __cb_value {");
                body.Add("    Some(__cb_some) => " + Write(values[0], outNames[values[0]], "__cb_some").TrimEnd(';') + ",");
                body.Add($"    None => {outNames[values[0]]}.write(::std::ptr::null_mut()),");
                body.Add("}");
                break;
            }

            default:
                Require(values.Count == 1, $"{f.RustPath} returns one value but has {values.Count} value out-parameters");
                body.Add(Write(values[0], outNames[values[0]], "__cb_value"));
                break;
        }

        body.Add(Support + "STATUS_OK");
        return body;
    }

    private string Write(ParameterMapping o, string n, string expr)
    {
        var t = o.Type;
        switch (t.Kind)
        {
            case MappingKind.Handle:
                return $"{n}.write({Support}HandleBox::boxed({expr}));";
            case MappingKind.String:
                return $"{n}.write(Buffer::from_string(::std::string::String::from({expr})));";
            case MappingKind.ValueVec when t.Element.Kind == MappingKind.String:
                // every string its own buffer (released by free_string), in a buffer of buffers
                return $"{n}.write(Buffer::from_buffers(::std::iter::IntoIterator::into_iter({expr}).map(|__cb_v| Buffer::from_string(::std::string::String::from(__cb_v))).collect()));";
            case MappingKind.ValueVec when t.Element.Kind == MappingKind.Handle:
                // every element becomes its own object
                return $"{n}.write(Buffer::from_elements::<{FfiType(t.Element)}>(::std::iter::IntoIterator::into_iter({expr}).map(|__cb_v| {Support}HandleBox::boxed(__cb_v)).collect()));";
            case MappingKind.ValueVec:
            {
                var element = FfiType(t.Element);
                return $"{n}.write(Buffer::from_elements::<{element}>(::std::iter::IntoIterator::into_iter({expr}).map(|__cb_v| {ToFfi(t.Element, "__cb_v")}).collect()));";
            }

            case MappingKind.PrimitiveVec:
            {
                var element = t.Element != null ? PrimitiveOf(t.Element) : SliceElement(t);
                return element == "u8"
                    ? $"{n}.write(Buffer::from_vec(::std::vec::Vec::<u8>::from({expr})));"
                    : $"{n}.write(Buffer::from_elements::<{element}>(::std::vec::Vec::<{element}>::from({expr})));";
            }

            default:
                return $"{n}.write({ToFfi(t, expr)});";
        }
    }

    private static string ErrorText(string format) => format switch
    {
        "Display" => "::std::string::ToString::to_string(&e)",
        "Debug" => "::std::format!(\"{:?}\", e)",
        "TypeName" => "::std::string::String::from(::std::any::type_name_of_val(&e))",
        _ => "(&" + Support + "ErrText(&e)).error_text()",
    };

    private static string FallbackCall(ExportedFunction f, IReadOnlyList<string> args)
    {
        var first = f.Parameters.FirstOrDefault();
        if (first != null && first.RustName == "self")
        {
            var method = f.RustPath.Split("::").Last();
            return FacadeConventions.ReceiverLocal + "." + method + "(" + string.Join(", ", args.Skip(1)) + ")";
        }

        return RustSyntax.Upstream(f.RustPath) + "(" + string.Join(", ", args) + ")";
    }
}

/// <summary>List helper for the builder.</summary>
internal static class FacadeListExtensions
{
    /// <summary>Appends every item of <paramref name="items"/>.</summary>
    public static void AddRangeOf<T>(this IList<T> list, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            list.Add(item);
        }
    }
}
