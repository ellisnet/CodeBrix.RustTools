using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using static CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures.PlanKit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;

/// <summary>
/// A small Rust crate ("shapecheck", written into a sandbox by the tests, never into
/// rust-samples/) and a hand-built mapping plan for it that uses every ReturnShape, every
/// ParameterRole the facade accepts, every MappingKind of facade mode, every FunctionKind except
/// PassThrough, the three error formats and a panic.
/// </summary>
internal static class CoverageCrate
{
    public const string Lib = "shapecheck";

    public const string Version = "0.2.0";

    public const string CargoToml = """
        [package]
        name = "shapecheck"
        version = "0.2.0"
        edition = "2024"
        license = "MIT"
        publish = false

        [dependencies]
        """;

    public const string LibRs = """
        //! Every shape the CrateBuilder facade maps, in one plain library.
        use std::fmt;

        /// A colour.
        #[derive(Clone, Copy, Debug, PartialEq, Eq)]
        pub enum Color {
            /// Red.
            Red = 0,
            /// Green.
            Green = 1,
            /// Blue (value 7 on purpose).
            Blue = 7,
        }

        /// A flag set with a bool, an enum and an array.
        #[derive(Clone, Copy, Debug, PartialEq)]
        pub struct Flags {
            /// Whether it is on.
            pub on: bool,
            /// Its colour.
            pub color: Color,
            /// Four weights.
            pub weights: [f32; 4],
        }

        /// An error with Debug only.
        #[derive(Debug)]
        pub struct DebugOnly(pub i32);

        /// An error with neither Display nor Debug.
        pub struct Opaque;

        /// A counter object.
        pub struct Counter {
            count: i64,
            log: Vec<u16>,
        }

        impl Counter {
            /// A counter starting at `start`.
            pub fn new(start: i64) -> Counter {
                Counter { count: start, log: Vec::new() }
            }

            /// A counter starting at `start`, which must not be negative.
            pub fn try_new(start: i64) -> Result<Counter, String> {
                if start < 0 { Err(format!("start {} is negative", start)) } else { Ok(Counter::new(start)) }
            }

            /// The count.
            pub fn get(&self) -> i64 {
                self.count
            }

            /// Adds `by`.
            pub fn add(&mut self, by: i64) {
                self.count += by;
                self.log.push(by as u16);
            }

            /// Adds every value.
            pub fn add_all(&mut self, values: &[i64]) {
                for v in values {
                    self.add(*v);
                }
            }

            /// Writes the count into every slot of `out`.
            pub fn fill(&self, out: &mut [i64]) {
                for slot in out.iter_mut() {
                    *slot = self.count;
                }
            }

            /// The additions so far.
            pub fn history(&self) -> Vec<u16> {
                self.log.clone()
            }

            /// The count, using the counter up.
            pub fn into_total(self) -> i64 {
                self.count
            }

            /// Half of the counter, or None when the count is 0.
            pub fn split(&self) -> Option<Counter> {
                if self.count == 0 { None } else { Some(Counter::new(self.count / 2)) }
            }

            /// Half the count; odd counts are an error.
            pub fn half(&self) -> Result<i64, DebugOnly> {
                if self.count % 2 == 0 { Ok(self.count / 2) } else { Err(DebugOnly(self.count as i32)) }
            }

            /// Panics on purpose.
            pub fn explode(&mut self) -> i64 {
                panic!("counter exploded at {}", self.count)
            }

            /// Adds the other counter's count.
            pub fn merge(&mut self, other: &Counter) {
                self.count += other.count;
            }

            /// Adds the other counter's count when there is one.
            pub fn maybe_merge(&mut self, other: Option<&Counter>) {
                if let Some(o) = other {
                    self.count += o.count;
                }
            }
        }

        impl fmt::Display for Color {
            fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
                write!(f, "{:?}", self)
            }
        }

        /// The opposite.
        pub fn negate(on: bool) -> bool {
            !on
        }

        /// The next colour.
        pub fn next_color(c: Color) -> Color {
            match c {
                Color::Red => Color::Green,
                Color::Green => Color::Blue,
                Color::Blue => Color::Red,
            }
        }

        /// Describes a flag set.
        pub fn describe(f: Flags) -> String {
            format!("{} {} {}", f.on, f.color, f.weights.iter().sum::<f32>())
        }

        /// A flag set.
        pub fn flags_of(on: bool, color: Color) -> Flags {
            Flags { on, color, weights: [1.0, 2.0, 3.0, 4.0] }
        }

        /// Scales by `k` (1 when absent).
        pub fn scale(v: [f32; 4], k: Option<f32>) -> [f32; 4] {
            let k = k.unwrap_or(1.0);
            [v[0] * k, v[1] * k, v[2] * k, v[3] * k]
        }

        /// The bytes of the text.
        pub fn bytes_of(text: String) -> Vec<u8> {
            text.into_bytes()
        }

        /// The sum.
        pub fn sum_vec(values: Vec<i32>) -> i64 {
            values.iter().map(|v| *v as i64).sum()
        }

        /// Parses a colour name.
        pub fn parse_color(name: &str) -> Result<Color, String> {
            match name {
                "red" => Ok(Color::Red),
                "green" => Ok(Color::Green),
                "blue" => Ok(Color::Blue),
                _ => Err(format!("unknown colour '{}'", name)),
            }
        }

        /// Fails unless `ok`.
        pub fn check(ok: bool) -> Result<(), Opaque> {
            if ok { Ok(()) } else { Err(Opaque) }
        }

        /// `n` bytes counting up, at most 16.
        pub fn read_bytes(n: usize) -> Result<Vec<u8>, String> {
            if n > 16 { Err(String::from("too many")) } else { Ok((0..n as u8).collect()) }
        }

        /// Upper case, unless empty.
        pub fn shout(s: &str) -> Result<String, String> {
            if s.is_empty() { Err(String::from("nothing to shout")) } else { Ok(s.to_uppercase()) }
        }

        /// The smallest and the largest value (0, 0 for none).
        pub fn min_max(xs: &[f64]) -> (f64, f64) {
            if xs.is_empty() {
                return (0.0, 0.0);
            }
            (xs.iter().cloned().fold(f64::MAX, f64::min), xs.iter().cloned().fold(f64::MIN, f64::max))
        }

        /// Quotient and remainder.
        pub fn div_mod(a: i32, b: i32) -> Result<(i32, i32), String> {
            if b == 0 { Err(String::from("division by zero")) } else { Ok((a / b, a % b)) }
        }

        /// The first even value.
        pub fn first_even(xs: &[u32]) -> Option<u32> {
            xs.iter().cloned().find(|v| v % 2 == 0)
        }

        /// Does nothing.
        pub fn noop() {}
        """;

    public static MappingPlan Create()
    {
        var plan = Plan(Lib);
        const string p = "shapecheck_";

        var color = new EnumMapping { RustPath = "Color", RustTypeName = "Color", CName = p + "color", ManagedName = "Color", Doc = "A colour." };
        color.Variants.Add(new EnumVariantMapping { RustName = "Red", CName = "SHAPECHECK_COLOR_RED", ManagedName = "Red", Value = 0, Doc = "Red." });
        color.Variants.Add(new EnumVariantMapping { RustName = "Green", CName = "SHAPECHECK_COLOR_GREEN", ManagedName = "Green", Value = 1, Doc = "Green." });
        color.Variants.Add(new EnumVariantMapping { RustName = "Blue", CName = "SHAPECHECK_COLOR_BLUE", ManagedName = "Blue", Value = 7, Doc = "Blue (value 7 on purpose)." });
        plan.Enums.Add(color);

        var weights = Array(p, "f32", 4);
        var flags = new MirrorStruct { RustPath = "Flags", RustTypeName = "Flags", CName = p + "flags", ManagedName = "Flags", Doc = "A flag set with a bool, an enum and an array.", ByteSize = 24, Alignment = 4 };
        flags.Fields.Add(new MirrorField { Name = "on", CName = "on", ManagedName = "On", Type = Bool(), Doc = "Whether it is on.", Offset = 0 });
        flags.Fields.Add(new MirrorField { Name = "color", CName = "color", ManagedName = "Color", Type = Enum(color), Doc = "Its colour.", Offset = 4 });
        flags.Fields.Add(new MirrorField { Name = "weights", CName = "weights", ManagedName = "Weights", Type = weights, Doc = "Four weights.", Offset = 8 });
        plan.Mirrors.Add(flags);

        var counter = AddHandle(plan, "Counter", p + "counter", "A counter object.");
        TypeMapping H() => Handle(counter);
        ExportedFunction M(ExportedFunction f)
        {
            f.Owner = counter;
            f.OwnerRustPath = "Counter";
            return f;
        }

        var divMod = Fn("div_mod", p + "div_mod", FunctionKind.Free, ReturnShape.ResultTuple, null, "upstream::div_mod(a, b)",
            In("a", "a", Prim("i32")), In("b", "b", Prim("i32")), Out("out0", Prim("i32")), Out("out1", Prim("i32")));
        divMod.Return.Type = null;
        divMod.Return.TupleItems.Add(Prim("i32"));
        divMod.Return.TupleItems.Add(Prim("i32"));
        var minMax = Fn("min_max", p + "min_max", FunctionKind.Free, ReturnShape.Tuple, null, "upstream::min_max(xs)",
            SliceIn("xs", Slice("f64")), Out("out0", Prim("f64")), Out("out1", Prim("f64")));
        minMax.Return.Type = null;
        minMax.Return.TupleItems.Add(Prim("f64"));
        minMax.Return.TupleItems.Add(Prim("f64"));
        var half = M(Fn("Counter::half", p + "counter_half", FunctionKind.Method, ReturnShape.ResultValue, Prim("i64"), "upstream::Counter::half(this)",
            Receiver(counter, "&Counter"), Out("out", Prim("i64"))));
        half.Return.ErrorFormat = "Debug";
        var check = Fn("check", p + "check", FunctionKind.Free, ReturnShape.ResultVoid, null, "upstream::check(ok)", In("ok", "ok", Bool()));
        check.Return.ErrorFormat = "TypeName";
        var noop = Fn("noop", p + "noop", FunctionKind.Free, ReturnShape.Void, null, null);

        var functions = new List<ExportedFunction>
        {
            M(Fn("Counter::new", p + "counter_new", FunctionKind.Constructor, ReturnShape.Handle, H(), "upstream::Counter::new(start)",
                In("start", "start", Prim("i64")), Out("out", H()))),
            M(Fn("Counter::try_new", p + "counter_try_new", FunctionKind.Constructor, ReturnShape.ResultHandle, H(), "upstream::Counter::try_new(start)",
                In("start", "start", Prim("i64")), Out("out", H()))),
            M(Fn("Counter::get", p + "counter_get", FunctionKind.Method, ReturnShape.Value, Prim("i64"), null,
                Receiver(counter, "&Counter"), Out("out", Prim("i64")))),
            M(Fn("Counter::add", p + "counter_add", FunctionKind.MutMethod, ReturnShape.Void, null, "upstream::Counter::add(this, by)",
                Receiver(counter, "&mut Counter"), In("by", "by", Prim("i64")))),
            M(Fn("Counter::add_all", p + "counter_add_all", FunctionKind.MutMethod, ReturnShape.Void, null, "upstream::Counter::add_all(this, values)",
                Receiver(counter, "&mut Counter"), SliceIn("values", Slice("i64")))),
            M(Fn("Counter::fill", p + "counter_fill", FunctionKind.Method, ReturnShape.Void, null, "upstream::Counter::fill(this, out)",
                Receiver(counter, "&Counter"), SliceIn("out", Slice("i64", mutable: true)))),
            M(Fn("Counter::history", p + "counter_history", FunctionKind.Method, ReturnShape.OwnedBuffer, VecOut(p, "u16"), "upstream::Counter::history(this)",
                Receiver(counter, "&Counter"), Out("out", VecOut(p, "u16"), ParameterRole.OutBuffer))),
            M(Fn("Counter::into_total", p + "counter_into_total", FunctionKind.ConsumingMethod, ReturnShape.Value, Prim("i64"), "upstream::Counter::into_total(this)",
                Receiver(counter, "Counter"), Out("out", Prim("i64")))),
            M(Fn("Counter::split", p + "counter_split", FunctionKind.Method, ReturnShape.OptionalHandle, H(), "upstream::Counter::split(this)",
                Receiver(counter, "&Counter"), Out("out", H()))),
            half,
            M(Fn("Counter::explode", p + "counter_explode", FunctionKind.MutMethod, ReturnShape.Value, Prim("i64"), "upstream::Counter::explode(this)",
                Receiver(counter, "&mut Counter"), Out("out", Prim("i64")))),
            M(Fn("Counter::merge", p + "counter_merge", FunctionKind.MutMethod, ReturnShape.Void, null, "upstream::Counter::merge(this, other)",
                Receiver(counter, "&mut Counter"), In("other", "other", Handle(counter, "&Counter"), ParameterRole.Handle))),
            M(Fn("Counter::maybe_merge", p + "counter_maybe_merge", FunctionKind.MutMethod, ReturnShape.Void, null, "upstream::Counter::maybe_merge(this, other)",
                Receiver(counter, "&mut Counter"), OptionalIn("other", Handle(counter, "&Counter"), ParameterRole.Handle))),
            Fn("negate", p + "negate", FunctionKind.Free, ReturnShape.Value, Bool(), "upstream::negate(on)", In("on", "on", Bool()), Out("out", Bool())),
            Fn("next_color", p + "next_color", FunctionKind.Free, ReturnShape.Value, Enum(color), "upstream::next_color(c)", In("c", "c", Enum(color)), Out("out", Enum(color))),
            Fn("describe", p + "describe", FunctionKind.Free, ReturnShape.OwnedString, StringOut(p), "upstream::describe(f)",
                In("f", "f", Mirror(flags)), Out("out", StringOut(p), ParameterRole.OutBuffer)),
            Fn("flags_of", p + "flags_of", FunctionKind.Free, ReturnShape.Value, Mirror(flags), "upstream::flags_of(on, color)",
                In("on", "on", Bool()), In("color", "color", Enum(color)), Out("out", Mirror(flags))),
            Fn("scale", p + "scale", FunctionKind.Free, ReturnShape.Value, weights, "upstream::scale(v, k)",
                In("v", "v", weights), OptionalIn("k", Prim("f32")), Out("out", weights)),
            Fn("bytes_of", p + "bytes_of", FunctionKind.Free, ReturnShape.OwnedBuffer, VecOut(p, "u8"), "upstream::bytes_of(text.to_owned())",
                StrIn("text", StringIn()), Out("out", VecOut(p, "u8"), ParameterRole.OutBuffer)),
            Fn("sum_vec", p + "sum_vec", FunctionKind.Free, ReturnShape.Value, Prim("i64"), "upstream::sum_vec(values.to_vec())",
                SliceIn("values", VecIn("i32")), Out("out", Prim("i64"))),
            Fn("parse_color", p + "parse_color", FunctionKind.Free, ReturnShape.ResultValue, Enum(color), "upstream::parse_color(name)",
                StrIn("name"), Out("out", Enum(color))),
            check,
            Fn("read_bytes", p + "read_bytes", FunctionKind.Free, ReturnShape.ResultBuffer, VecOut(p, "u8"), "upstream::read_bytes(n)",
                In("n", "n", Prim("usize")), Out("out", VecOut(p, "u8"), ParameterRole.OutBuffer)),
            Fn("shout", p + "shout", FunctionKind.Free, ReturnShape.ResultString, StringOut(p), "upstream::shout(s)",
                StrIn("s"), Out("out", StringOut(p), ParameterRole.OutBuffer)),
            minMax,
            divMod,
            Fn("first_even", p + "first_even", FunctionKind.Free, ReturnShape.OptionalValue, Prim("u32"), "upstream::first_even(xs)",
                SliceIn("xs", Slice("u32")), Out("out_present", Bool(), ParameterRole.OutPresent), Out("out", Prim("u32"))),
            noop,
            Release(plan, "free_buffer", MappingKind.PrimitiveVec),
            Release(plan, "free_string", MappingKind.String),
        };
        Finish(plan, functions);
        plan.Overrides.Add(new FacadeOverrideEntry
        {
            RustPath = "answer",
            RustSource = """
                /// A hand-written export.
                #[unsafe(no_mangle)]
                pub extern "C" fn shapecheck_answer() -> i32 {
                    helpers::forty_two()
                }
                """,
        });
        return plan;
    }

    /// <summary>The additional facade file the coverage plan copies in (src/helpers.rs).</summary>
    public const string HelpersRs = """
        //! A helper module of the consumer's own.

        /// The answer.
        pub fn forty_two() -> i32 {
            42
        }
        """;
}
