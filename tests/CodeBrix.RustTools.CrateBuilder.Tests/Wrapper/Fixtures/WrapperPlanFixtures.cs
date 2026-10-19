using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper.Fixtures;

/// <summary>
/// Hand-built mapping plans for the wrapper writer's tests. <see cref="DataCrate"/> covers
/// every <see cref="ReturnShape"/>, every <see cref="ParameterRole"/> and every
/// <see cref="MappingKind"/> (modelled on rust-samples/test_project_15 plus extra shapes);
/// <see cref="PassThrough"/> is test_project_13's single C export <c>cb_add</c>.
/// </summary>
internal static class WrapperPlanFixtures
{
    public const string DataCrateNamespace = "Fixture.DataCrate";

    public static TypeMapping Prim(string rust, int size = 0) => new TypeMapping { Kind = MappingKind.Primitive, RustType = rust, ByteSize = size, IsBlittable = true };

    public static TypeMapping Bool() => new TypeMapping { Kind = MappingKind.Bool, RustType = "bool", ByteSize = 1, IsBlittable = true };

    public static TypeMapping Unit() => new TypeMapping { Kind = MappingKind.Unit, RustType = "()" };

    public static TypeMapping Str() => new TypeMapping { Kind = MappingKind.Str, RustType = "&str" };

    public static TypeMapping String() => new TypeMapping { Kind = MappingKind.String, RustType = "String" };

    public static TypeMapping Slice(string element, bool mutable = false)
        => new TypeMapping { Kind = MappingKind.PrimitiveSlice, RustType = (mutable ? "&mut [" : "&[") + element + "]", Element = Prim(element), ManagedMarshalType = mutable ? "Span<x>" : "ReadOnlySpan<x>" };

    public static TypeMapping Vec(string element) => new TypeMapping { Kind = MappingKind.PrimitiveVec, RustType = "Vec<" + element + ">", Element = Prim(element) };

    public static TypeMapping Array(string rust) => new TypeMapping { Kind = MappingKind.FixedArray, RustType = rust };

    public static TypeMapping Of(MirrorStruct mirror) => new TypeMapping { Kind = MappingKind.MirrorStruct, RustType = mirror.RustTypeName, Mirror = mirror, IsBlittable = true };

    public static TypeMapping Of(HandleType handle) => new TypeMapping { Kind = MappingKind.Handle, RustType = handle.RustTypeName, Handle = handle };

    public static TypeMapping Of(EnumMapping enumMapping) => new TypeMapping { Kind = MappingKind.Enum, RustType = enumMapping.RustTypeName, Enum = enumMapping, ByteSize = 4, IsBlittable = true };

    public static ParameterMapping P(string name, TypeMapping type, ParameterRole role = ParameterRole.Value) => new ParameterMapping { RustName = name, CName = name, ManagedName = null, Type = type, Role = role };

    public static ReturnMapping R(ReturnShape shape, TypeMapping type = null, string error = null, params TypeMapping[] tuple)
        => new ReturnMapping { Shape = shape, Type = type, ErrorTypeDisplay = error, TupleItems = tuple.ToList() };

    /// <summary>The data-crate plan: mirrors, enums, two handles, free functions of every shape.</summary>
    public static MappingPlan DataCrate(bool linkWebGpu = false)
    {
        const string prefix = "datacrate_";
        var plan = new MappingPlan
        {
            CrateName = "datacrate",
            LibName = "datacrate",
            FacadeCrateName = "datacrate_facade",
            SymbolPrefix = prefix,
            ManagedNamespace = DataCrateNamespace,
            ManagedAssemblyName = DataCrateNamespace,
            LinkToCodeBrixWebGpu = linkWebGpu,
        };

        var mode = new EnumMapping
        {
            RustPath = "crate::Mode", RustTypeName = "Mode", CName = prefix + "mode", ManagedName = "Mode", Doc = "How an [`Encoder`] processes its input.",
            Variants =
            {
                new EnumVariantMapping { RustName = "A", CName = "DATACRATE_MODE_A", ManagedName = "A", Value = 1, Doc = "Pass the bytes through unchanged." },
                new EnumVariantMapping { RustName = "B", CName = "DATACRATE_MODE_B", ManagedName = "B", Value = 2, Doc = "Invert every byte." },
            },
        };
        plan.Enums.Add(mode);

        var vec2 = new MirrorStruct
        {
            RustPath = "crate::Vec2", RustTypeName = "Vec2", CName = prefix + "vec2", ManagedName = "Vec2", Doc = "A two-dimensional vector of `f64` components.",
            Fields =
            {
                new MirrorField { Name = "x", CName = "x", ManagedName = "X", Type = Prim("f64", 8), Doc = "The horizontal component." },
                new MirrorField { Name = "y", CName = "y", ManagedName = "Y", Type = Prim("f64", 8), Doc = "The vertical component." },
            },
        };
        var rect = new MirrorStruct
        {
            RustPath = "crate::shapes::Rect", RustTypeName = "Rect", CName = prefix + "rect", ManagedName = "Rect", Doc = "An axis-aligned rectangle.\n\nThe corners are inclusive; `min` <= `max` on both axes.",
            Fields =
            {
                new MirrorField { Name = "min", CName = "min", ManagedName = "Min", Type = Of(vec2), Doc = "The lower corner." },
                new MirrorField { Name = "max", CName = "max", ManagedName = "Max", Type = Of(vec2), Doc = "The upper corner." },
            },
        };
        var tick = new MirrorStruct
        {
            RustPath = "crate::time::Tick", RustTypeName = "Tick", CName = prefix + "tick", ManagedName = "Tick", Doc = "A point in time in ticks; 254016000000 ticks per second.", IsTupleStruct = true, TupleArity = 1,
            Fields = { new MirrorField { Name = "0", CName = "item0", ManagedName = "Item0", Type = Prim("i64", 8) } },
        };
        var config = new MirrorStruct
        {
            RustPath = "crate::EncoderConfig", RustTypeName = "EncoderConfig", CName = prefix + "encoder_config", ManagedName = "EncoderConfig", Doc = "The settings an [`Encoder`] is created with.",
            Fields =
            {
                new MirrorField { Name = "mode", CName = "mode", ManagedName = "Mode", Type = Of(mode), Doc = "The processing mode." },
                new MirrorField { Name = "frame_size", CName = "frame_size", ManagedName = "FrameSize", Type = Prim("u32", 4), Doc = "Bytes per frame." },
            },
        };
        var settings = new MirrorStruct
        {
            RustPath = "crate::Settings", RustTypeName = "Settings", CName = prefix + "settings", ManagedName = "Settings", Doc = null,
            Fields =
            {
                new MirrorField { Name = "enabled", CName = "enabled", ManagedName = "Enabled", Type = Bool() },
                new MirrorField { Name = "weights", CName = "weights", ManagedName = "Weights", Type = Array("[f32; 4]") },
                new MirrorField { Name = "matrix", CName = "matrix", ManagedName = "Matrix", Type = Array("[[f64; 3]; 3]") },
            },
        };
        plan.Mirrors.Add(config);
        plan.Mirrors.Add(rect);
        plan.Mirrors.Add(settings);
        plan.Mirrors.Add(tick);
        plan.Mirrors.Add(vec2);

        var encoder = new HandleType { RustPath = "crate::Encoder", RustTypeName = "Encoder", CName = prefix + "encoder", ManagedName = "Encoder", HasDefault = true, HasClone = true, Doc = "A stateful encoder (not `Copy`): it owns a buffer and counts the frames it has encoded." };
        var decoder = new HandleType { RustPath = "crate::Decoder", RustTypeName = "Decoder", CName = prefix + "decoder", ManagedName = "Decoder", Doc = "Reads what an [`Encoder`] wrote." };
        plan.Handles.Add(decoder);
        plan.Handles.Add(encoder);
        var self = P("self", Of(encoder), ParameterRole.Handle);

        ExportedFunction F(string path, string cName, string managed, FunctionKind kind, HandleType owner, ReturnMapping result, params ParameterMapping[] parameters)
        {
            var function = new ExportedFunction { RustPath = path, CName = cName, ManagedName = managed, Kind = kind, Owner = owner, Return = result, Parameters = parameters.ToList(), Doc = "Wraps `" + path + "`." };
            plan.Functions.Add(function);
            owner?.Methods.Add(function);
            return function;
        }

        F("Decoder::open", prefix + "decoder_open", "Open", FunctionKind.Constructor, decoder, R(ReturnShape.ResultHandle, Of(decoder), "DecodeError"), P("path", Str(), ParameterRole.StringData), P("path_len", Prim("usize"), ParameterRole.StringLength));
        F("Decoder::read", prefix + "decoder_read", "Read", FunctionKind.MutMethod, decoder, R(ReturnShape.Value, Prim("usize")), P("self", Of(decoder), ParameterRole.Handle), P("buf", Slice("u8", true), ParameterRole.SliceData), P("buf_len", Prim("usize"), ParameterRole.SliceLength));

        F("Encoder::new", prefix + "encoder_new", "New", FunctionKind.Constructor, encoder, R(ReturnShape.Handle, Of(encoder)), P("cfg", Of(config)));
        F("Encoder::default", prefix + "encoder_default", "Default", FunctionKind.Constructor, encoder, R(ReturnShape.Handle, Of(encoder)));
        F("Encoder::try_with_frame_size", prefix + "encoder_try_with_frame_size", "TryWithFrameSize", FunctionKind.Constructor, encoder, R(ReturnShape.ResultHandle, Of(encoder), "ConfigError"), P("frame_size", Prim("u32")));
        F("Encoder::encode", prefix + "encoder_encode", "Encode", FunctionKind.MutMethod, encoder, R(ReturnShape.OwnedBuffer, Vec("u8")), self, P("input", Slice("u8"), ParameterRole.SliceData), P("input_len", Prim("usize"), ParameterRole.SliceLength));
        F("Encoder::frames", prefix + "encoder_frames", "Frames", FunctionKind.Method, encoder, R(ReturnShape.Value, Prim("u32")), self);
        F("Encoder::name", prefix + "encoder_name", "Name", FunctionKind.Method, encoder, R(ReturnShape.OptionalValue, String()), self);
        F("Encoder::absorb", prefix + "encoder_absorb", "Absorb", FunctionKind.MutMethod, encoder, R(ReturnShape.Void, Unit()), self, P("other", Of(encoder), ParameterRole.Handle));
        F("Encoder::finish", prefix + "encoder_finish", "Finish", FunctionKind.ConsumingMethod, encoder, R(ReturnShape.OwnedBuffer, Vec("u8")), self);
        F("Encoder::clone", prefix + "encoder_clone", "Clone", FunctionKind.Method, encoder, R(ReturnShape.Handle, Of(encoder)), self);
        F("Encoder::to_string", prefix + "encoder_to_string", "ToString", FunctionKind.Method, encoder, R(ReturnShape.OwnedString, String()), self);
        F("Encoder::config", prefix + "encoder_config", "Config", FunctionKind.Method, encoder, R(ReturnShape.Value, Of(config)), self);
        F("Encoder::mode", prefix + "encoder_mode", "Mode", FunctionKind.Method, encoder, R(ReturnShape.Value, Of(mode)), self);
        F("Encoder::is_idle", prefix + "encoder_is_idle", "IsIdle", FunctionKind.Method, encoder, R(ReturnShape.Value, Bool()), self);
        encoder.Destroy = new ExportedFunction { RustPath = "Encoder::drop", CName = prefix + "encoder_destroy", ManagedName = "Destroy", Kind = FunctionKind.Destroy, Owner = encoder, Parameters = { self }, Return = R(ReturnShape.Void, Unit()) };
        decoder.Destroy = new ExportedFunction { RustPath = "Decoder::drop", CName = prefix + "decoder_destroy", ManagedName = "Destroy", Kind = FunctionKind.Destroy, Owner = decoder, Parameters = { P("self", Of(decoder), ParameterRole.Handle) }, Return = R(ReturnShape.Void, Unit()) };

        F("checksum", prefix + "checksum", "Checksum", FunctionKind.Free, null, R(ReturnShape.Value, Prim("u32")), P("data", Slice("u8"), ParameterRole.SliceData), P("data_len", Prim("usize"), ParameterRole.SliceLength));
        F("greet", prefix + "greet", "Greet", FunctionKind.Free, null, R(ReturnShape.OwnedString, String()), P("name", Str(), ParameterRole.StringData), P("name_len", Prim("usize"), ParameterRole.StringLength));
        F("parse_pair", prefix + "parse_pair", "ParsePair", FunctionKind.Free, null, R(ReturnShape.ResultTuple, null, "ParseError", Prim("i32"), Prim("i32")), P("s", Str(), ParameterRole.StringData), P("s_len", Prim("usize"), ParameterRole.StringLength));
        F("invert", prefix + "invert", "Invert", FunctionKind.Free, null, R(ReturnShape.Value, Array("[[f64; 3]; 3]")), P("m", Array("[[f64; 3]; 3]")));
        F("find", prefix + "find", "Find", FunctionKind.Free, null, R(ReturnShape.OptionalValue, Prim("usize")), P("xs", Slice("i32"), ParameterRole.SliceData), P("xs_len", Prim("usize"), ParameterRole.SliceLength), P("x", Prim("i32")));
        F("scale", prefix + "scale", "Scale", FunctionKind.Free, null, R(ReturnShape.Void, Unit()), P("xs", Slice("f32", true), ParameterRole.SliceData), P("xs_len", Prim("usize"), ParameterRole.SliceLength), P("k", Prim("f32")));
        F("is_even", prefix + "is_even", "IsEven", FunctionKind.Free, null, R(ReturnShape.Value, Bool()), P("n", Prim("i64")));
        F("validate", prefix + "validate", "Validate", FunctionKind.Free, null, R(ReturnShape.ResultVoid, Unit(), "ValidationError"), P("s", Str(), ParameterRole.StringData), P("s_len", Prim("usize"), ParameterRole.StringLength));
        F("read_text", prefix + "read_text", "ReadText", FunctionKind.Free, null, R(ReturnShape.ResultString, String(), "std::io::Error"), P("path", Str(), ParameterRole.StringData), P("path_len", Prim("usize"), ParameterRole.StringLength));
        F("squares", prefix + "squares", "Squares", FunctionKind.Free, null, R(ReturnShape.OwnedBuffer, Vec("u32")), P("n", Prim("u32")));
        F("random_bytes", prefix + "random_bytes", "RandomBytes", FunctionKind.Free, null, R(ReturnShape.ResultBuffer, Vec("u8"), "RandomError"), P("n", Prim("usize")));
        F("min_max", prefix + "min_max", "MinMax", FunctionKind.Free, null, R(ReturnShape.Tuple, null, null, Prim("i32"), Prim("i32")), P("xs", Slice("i32"), ParameterRole.SliceData), P("xs_len", Prim("usize"), ParameterRole.SliceLength));
        F("lookup", prefix + "lookup", "Lookup", FunctionKind.Free, null, R(ReturnShape.OptionalHandle, Of(encoder)), P("name", Str(), ParameterRole.StringData), P("name_len", Prim("usize"), ParameterRole.StringLength));
        F("Mode::from_name", prefix + "mode_from_name", "FromName", FunctionKind.Free, null, R(ReturnShape.OptionalValue, Of(mode)), P("name", Str(), ParameterRole.StringData), P("name_len", Prim("usize"), ParameterRole.StringLength));
        F("find_name", prefix + "find_name", "FindName", FunctionKind.Free, null, R(ReturnShape.OptionalValue, String()), P("id", Prim("u32")));
        F("default_settings", prefix + "default_settings", "DefaultSettings", FunctionKind.Free, null, R(ReturnShape.Value, Of(settings)));
        F("divide", prefix + "divide", "Divide", FunctionKind.Free, null, R(ReturnShape.ResultValue, Prim("i32"), "DivideError"), P("a", Prim("i32")), P("b", Prim("i32")), P("out", Prim("i32"), ParameterRole.OutValue));
        F("time::advance", prefix + "time_advance", "Advance", FunctionKind.Free, null, R(ReturnShape.Value, Of(tick)), P("t", Of(tick)), P("n", Prim("i64")));
        F("raw_address", prefix + "raw_address", "RawAddress", FunctionKind.Free, null, R(ReturnShape.Value, new TypeMapping { Kind = MappingKind.RawPointer, RustType = "*const u8" }), P("p", new TypeMapping { Kind = MappingKind.RawPointer, RustType = "*const u8" }));
        F("Vec2::new", prefix + "vec2_new", "Vec2New", FunctionKind.Free, null, R(ReturnShape.Value, Of(vec2)), P("x", Prim("f64")), P("y", Prim("f64"))).OwnerRustPath = "crate::Vec2";
        F("Vec2::dot", prefix + "vec2_dot", "Vec2Dot", FunctionKind.Free, null, R(ReturnShape.Value, Prim("f64")), P("self", Of(vec2)), P("other", Of(vec2))).OwnerRustPath = "crate::Vec2";
        F("Vec2::lerp", prefix + "vec2_lerp", "Vec2Lerp", FunctionKind.Free, null, R(ReturnShape.Value, Of(vec2)), P("self", Of(vec2)), P("to", Of(vec2)), P("t", Prim("f64"))).OwnerRustPath = "crate::Vec2";
        F("Encoder::version", prefix + "encoder_version", "EncoderVersion", FunctionKind.Free, null, R(ReturnShape.Value, Prim("u32"))).OwnerRustPath = "crate::Encoder";
        F("clamp", prefix + "clamp", "Clamp", FunctionKind.Free, null, R(ReturnShape.Value, Prim("f64")), P("x", Prim("f64")), P("has_max", Bool(), ParameterRole.InPresent), P("max", Prim("f64")), P("has_mode", Bool(), ParameterRole.InPresent), P("mode", Of(mode)));
        F("Encoder::attach", prefix + "encoder_attach", "Attach", FunctionKind.MutMethod, encoder, R(ReturnShape.Void, Unit()), self, P("has_other", Bool(), ParameterRole.InPresent), P("other", Of(encoder), ParameterRole.Handle));
        F("Rect::area", prefix + "rect_area", "Area", FunctionKind.Free, null, R(ReturnShape.Value, Prim("f64")), P("self", Of(rect)));
        F("Rect::contains", prefix + "rect_contains", "Contains", FunctionKind.Free, null, R(ReturnShape.Value, Bool()), P("self", Of(rect)), P("p", Of(vec2)));
        plan.Functions.Add(new ExportedFunction { RustPath = "free_buffer", CName = prefix + "free_buffer", ManagedName = "FreeBuffer", Kind = FunctionKind.Release, Return = R(ReturnShape.Void, Unit()) });
        plan.Functions.Add(new ExportedFunction { RustPath = "free_string", CName = prefix + "free_string", ManagedName = "FreeString", Kind = FunctionKind.Release, Return = R(ReturnShape.Void, Unit()) });

        plan.Skipped.Add(new SkippedItem { RustPath = "crate::for_each", Kind = "function", Reason = "takes a callback (impl FnMut), which is not wrapped automatically. Add a FacadeOverride for it if you need it.", OverrideHint = "#[unsafe(no_mangle)] pub unsafe extern \"C\" fn datacrate_for_each() -> i32 { 0 }" });
        plan.DocNotes.Add(new DocNote { RustPath = "crate::time::Tick", Text = "254016000000 ticks per second." });
        plan.Warnings.Add("ParseError: Display is not known; Debug formatting is used for its message.");
        plan.Overrides.Add(new FacadeOverrideEntry
        {
            RustPath = "crate::answer",
            RustSource = "#[unsafe(no_mangle)] pub extern \"C\" fn datacrate_answer() -> i32 { 42 }",
            CSharpSource = "namespace " + DataCrateNamespace + ";\n\n/// <summary>Hand-written additions.</summary>\npublic static partial class Datacrate\n{\n    /// <summary>The answer, computed on the managed side.</summary>\n    public static int Answer() => 42;\n}\n",
        });
        return plan;
    }

    /// <summary>The pass-through plan of test_project_13 (<c>cb_add</c>).</summary>
    public static MappingPlan PassThrough() => new MappingPlan
    {
        CrateName = "test_project_13",
        LibName = "test_project_13",
        FacadeCrateName = "test_project_13_facade",
        SymbolPrefix = "test_project_13_",
        ManagedNamespace = "Fixture.Adder",
        ManagedAssemblyName = "Fixture.Adder",
        UpstreamIsCdylib = true,
        Functions =
        {
            new ExportedFunction
            {
                RustPath = "cb_add",
                CName = "cb_add",
                ManagedName = "CbAdd",
                Kind = FunctionKind.PassThrough,
                Doc = "Adds two 32-bit integers, wrapping on overflow.\n\nExported unmangled with the C calling convention, so its symbol name is `cb_add`.",
                Parameters = { P("a", Prim("i32")), P("b", Prim("i32")) },
                Return = R(ReturnShape.Value, Prim("i32")),
            },
        },
    };
}
