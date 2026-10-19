using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using static CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures.PlanKit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;

/// <summary>
/// A hand-built mapping plan for rust-samples/test_project_15 ("datacrate"), written the way the
/// planner writes it: mirrors EncoderConfig (with an enum field), Tick (tuple struct) and Vec2;
/// enum Mode; handle Encoder; slices, strings, an owned buffer and string, an optional value,
/// a Result of a tuple and a fixed 3x3 array.
/// </summary>
internal static class DatacratePlan
{
    public const string Lib = "datacrate";

    public static MappingPlan Create()
    {
        var plan = Plan(Lib);
        const string p = "datacrate_";

        var mode = new EnumMapping { RustPath = "Mode", RustTypeName = "Mode", CName = p + "mode", ManagedName = "Mode", Doc = "How an [`Encoder`] processes its input." };
        mode.Variants.Add(new EnumVariantMapping { RustName = "A", CName = "DATACRATE_MODE_A", ManagedName = "A", Value = 1, Doc = "Pass the bytes through unchanged." });
        mode.Variants.Add(new EnumVariantMapping { RustName = "B", CName = "DATACRATE_MODE_B", ManagedName = "B", Value = 2, Doc = "Invert every byte." });
        plan.Enums.Add(mode);

        var config = new MirrorStruct { RustPath = "EncoderConfig", RustTypeName = "EncoderConfig", CName = p + "encoder_config", ManagedName = "EncoderConfig", Doc = "The settings an [`Encoder`] is created with.", ByteSize = 8, Alignment = 4 };
        config.Fields.Add(new MirrorField { Name = "mode", CName = "mode", ManagedName = "Mode", Type = Enum(mode), Doc = "The processing mode.", Offset = 0 });
        config.Fields.Add(new MirrorField { Name = "frame_size", CName = "frame_size", ManagedName = "FrameSize", Type = Prim("u32"), Doc = "Bytes per frame.", Offset = 4 });
        var tick = new MirrorStruct { RustPath = "Tick", RustTypeName = "Tick", CName = p + "tick", ManagedName = "Tick", Doc = "A point in time, in ticks (ticks per second = 254016000000).", IsTupleStruct = true, TupleArity = 1, ByteSize = 8, Alignment = 8 };
        tick.Fields.Add(new MirrorField { Name = "0", CName = "item0", ManagedName = "Item0", Type = Prim("i64"), Doc = string.Empty, Offset = 0 });
        var vec2 = new MirrorStruct { RustPath = "Vec2", RustTypeName = "Vec2", CName = p + "vec2", ManagedName = "Vec2", Doc = "A two-dimensional vector of `f64` components.", ByteSize = 16, Alignment = 8 };
        vec2.Fields.Add(new MirrorField { Name = "x", CName = "x", ManagedName = "X", Type = Prim("f64"), Doc = "The horizontal component.", Offset = 0 });
        vec2.Fields.Add(new MirrorField { Name = "y", CName = "y", ManagedName = "Y", Type = Prim("f64"), Doc = "The vertical component.", Offset = 8 });
        plan.Mirrors.Add(config);
        plan.Mirrors.Add(tick);
        plan.Mirrors.Add(vec2);

        var encoder = AddHandle(plan, "Encoder", p + "encoder", "A stateful encoder (not `Copy`): it owns a buffer and counts the frames it has encoded.");
        var matrix = Array(p, "f64", 3, 3);

        var functions = new List<ExportedFunction>
        {
            Fn("checksum", p + "checksum", FunctionKind.Free, ReturnShape.Value, Prim("u32"), "upstream::checksum(data)",
                SliceIn("data", Slice("u8")), Out("out", Prim("u32"))),
            Own(encoder, Fn("Encoder::encode", p + "encoder_encode", FunctionKind.MutMethod, ReturnShape.OwnedBuffer, VecOut(p, "u8"), "upstream::Encoder::encode(this, input)",
                Receiver(encoder, "&mut Encoder"), SliceIn("input", Slice("u8")), Out("out", VecOut(p, "u8"), ParameterRole.OutBuffer))),
            Own(encoder, Fn("Encoder::frames", p + "encoder_frames", FunctionKind.Method, ReturnShape.Value, Prim("u32"), "upstream::Encoder::frames(this)",
                Receiver(encoder, "&Encoder"), Out("out", Prim("u32")))),
            Own(encoder, Fn("Encoder::new", p + "encoder_new", FunctionKind.Constructor, ReturnShape.Handle, Handle(encoder), "upstream::Encoder::new(cfg)",
                In("cfg", "cfg", Mirror(config)), Out("out", Handle(encoder)))),
            Fn("find", p + "find", FunctionKind.Free, ReturnShape.OptionalValue, Prim("usize"), "upstream::find(xs, x)",
                SliceIn("xs", Slice("i32")), In("x", "x", Prim("i32")), Out("out_present", Bool(), ParameterRole.OutPresent), Out("out", Prim("usize"))),
            Release(plan, "free_buffer", MappingKind.PrimitiveVec),
            Release(plan, "free_string", MappingKind.String),
            Fn("greet", p + "greet", FunctionKind.Free, ReturnShape.OwnedString, StringOut(p), "upstream::greet(name)",
                StrIn("name"), Out("out", StringOut(p), ParameterRole.OutBuffer)),
            Fn("invert", p + "invert", FunctionKind.Free, ReturnShape.Value, matrix, "upstream::invert(m)",
                In("m", "m", matrix), Out("out", matrix)),
            Owned("Mode", Fn("Mode::from_name", p + "mode_from_name", FunctionKind.Free, ReturnShape.OptionalValue, Enum(mode), "upstream::Mode::from_name(name)",
                StrIn("name"), Out("out_present", Bool(), ParameterRole.OutPresent), Out("out", Enum(mode)))),
            ParsePair(p),
            Owned("Tick", Fn("Tick::seconds", p + "tick_seconds", FunctionKind.Free, ReturnShape.Value, Prim("f64"), "upstream::Tick::seconds(&this)",
                ValueReceiver(Mirror(tick)), Out("out", Prim("f64")))),
            Owned("Vec2", Fn("Vec2::dot", p + "vec2_dot", FunctionKind.Free, ReturnShape.Value, Prim("f64"), "upstream::Vec2::dot(&this, other)",
                ValueReceiver(Mirror(vec2)), In("other", "other", Mirror(vec2)), Out("out", Prim("f64")))),
            Owned("Vec2", Fn("Vec2::new", p + "vec2_new", FunctionKind.Free, ReturnShape.Value, Mirror(vec2), "upstream::Vec2::new(x, y)",
                In("x", "x", Prim("f64")), In("y", "y", Prim("f64")), Out("out", Mirror(vec2)))),
        };
        Finish(plan, functions);
        plan.DocNotes.Add(new DocNote { RustPath = "Tick", Text = "A point in time, in ticks (ticks per second = 254016000000, see [`TICKS_PER_SECOND`])." });
        return plan;
    }

    /// <summary>The exports the built library must have.</summary>
    public static IEnumerable<string> PlannedSymbols(MappingPlan plan)
    {
        yield return plan.SymbolPrefix + "last_error_message";
        foreach (var f in plan.Functions)
        {
            yield return f.CName;
        }
    }

    private static ExportedFunction ParsePair(string p)
    {
        var f = Fn("parse_pair", p + "parse_pair", FunctionKind.Free, ReturnShape.ResultTuple, null, "upstream::parse_pair(s)",
            StrIn("s"), Out("out0", Prim("i32")), Out("out1", Prim("i32")));
        f.Return.Type = null;
        f.Return.TupleItems.Add(Prim("i32"));
        f.Return.TupleItems.Add(Prim("i32"));
        f.Return.ErrorTypeDisplay = "ParseError";
        return f;
    }

    private static ExportedFunction Own(HandleType h, ExportedFunction f)
    {
        f.Owner = h;
        f.OwnerRustPath = h.RustPath;
        return f;
    }

    private static ExportedFunction Owned(string ownerPath, ExportedFunction f)
    {
        f.OwnerRustPath = ownerPath;
        return f;
    }
}
