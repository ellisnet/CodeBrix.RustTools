using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// End-to-end host-mode build of a synthetic data-shaped crate that fences the default-wrap
/// rules: public fields of objects (getters, setters, a constructor from the fields), results
/// that borrow (copied), paths and OS strings as UTF-8 text, text-bounded generic and
/// <c>impl</c> parameters, slices and lists of plain value types, constants copied into C#,
/// and a free function named like a type (the generated C# must still compile). The wrapper
/// is built with warnings as errors and called from a console app; the wrap report must give
/// the reasons for what stays unwrapped. Skips by name without cargo or the dotnet CLI.
/// </summary>
public class DefaultWrapRulesEndToEndTests
{
    [Fact]
    public async Task a_data_shaped_crate_is_wrapped_with_fields_copies_paths_lists_and_constants()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("datakit/Cargo.toml", "[package]\nname = \"datakit\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[dependencies]\n");
        sandbox.WriteFile("datakit/src/lib.rs", DatakitLib);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "datakit", "Cargo.toml")),
            Mode = BuildMode.Host,
            Targets = { target },
            OutputDirectory = Path.Combine(sandbox.Directory, "out"),
            Offline = true,
            Wrapper = new WrapperOptions { AssemblyName = "E2E.Data", RootNamespace = "E2E.Data" },
        };
        using var builder = EndToEndKit.NewBuilder(keepScratch: true);

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        var header = await CheckAndRemoveKeptRunAsync("datakit.h");
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(sandbox.Directory, "app"), result.Wrapper.ProjectPath, DatakitProgram);

        //Assert
        EndToEndKit.Lines(output).Where(l => Regex.IsMatch(l, "^[a-z ]+=")).Should().Equal(
            "consts=1000 2.5 65280 -7 True 9",
            "greeting=hi \"there\"|A",
            "vector=1.5,2",
            "settings=job 3 0.5 1,2 4,5 Slow none /tmp/a.txt nohint",
            "updated=renamed 7 null 9 0,0 Fast tag /srv/b Fast",
            "default=0 Fast",
            "holder=7 secret=42 title=box",
            "child=3 found=3 missing=null",
            "lists=a,b 3,4 3,4 spare=none",
            "spare=7",
            "cleared=True",
            "absorb=7 total=10",
            "words=7 5",
            "family=smith 7,3 x,y",
            "family after=z 0 none",
            "library first=alpha data=1,2,3 mode=Slow",
            "file name=c.txt",
            "join=/srv/x/y.bin",
            "stem=report",
            "shout=HEY",
            "total=5",
            "byte sum=6",
            "sum points=4,6",
            "count fast=2",
            "grid=0,0;1,1;2,2",
            "flags=True,False,True",
            "static=datakit",
            "longest=banana",
            "shapes=Circle 12 Rect 1",
            "variants=Rect 6 Empty 0",
            "badge=gold 3",
            "odd path=DatakitException");
        var report = File.ReadAllText(result.WrapReportPath);
        report.Should().Contain("COMPUTED", "a constant whose value is an expression is listed as not wrapped");
        report.Should().Contain("its value is not a plain literal");
        report.Should().Contain("is async");
        report.Should().Contain("an executor");
        report.Should().Contain("a mutable reference");
        report.Should().Contain("pick", "a generic function with an ordinary type parameter stays unwrapped");
        report.Should().Contain("CONSTANTS");
        report.Should().Contain("Settings::DEFAULT_COUNT");
        report.Should().Contain("is an associated constant of Settings (u32 = 3)");
        if (header.Compiler != null)
        {
            header.ExitCode.Should().Be(0, header.Compiler + " rejected the generated datakit.h:\n" + header.Output);
        }

        EndToEndKit.Log(nameof(a_data_shaped_crate_is_wrapped_with_fields_copies_paths_lists_and_constants), stopwatch);
    }

    [Fact]
    public async Task with_field_accessors_turned_off_an_object_keeps_its_field_constructor_and_the_wrapper_compiles()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("fieldkit/Cargo.toml", "[package]\nname = \"fieldkit\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[dependencies]\n");
        sandbox.WriteFile("fieldkit/src/lib.rs", FieldkitLib);
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Path.Combine(sandbox.Directory, "fieldkit", "Cargo.toml")),
            Mode = BuildMode.Host,
            Targets = { target },
            OutputDirectory = Path.Combine(sandbox.Directory, "out"),
            Offline = true,
            Wrapper = new WrapperOptions { AssemblyName = "E2E.Fields", RootNamespace = "E2E.Fields", GenerateFieldAccessors = false },
        };
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));
        var wrapperBuild = await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build());
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(sandbox.Directory, "app"), result.Wrapper.ProjectPath, FieldkitProgram);

        //Assert
        wrapperBuild.Should().Contain("0 Warning(s)");
        EndToEndKit.Lines(output).Where(l => Regex.IsMatch(l, "^[a-z ]+=")).Should().Equal(
            "holder=7 box",
            "shape=Rect 6");
        var holderSource = File.ReadAllText(Path.Combine(result.Wrapper.ProjectDirectory, "Holder.cs"));
        holderSource.Should().Contain("public Holder(");
        holderSource.Should().NotContain("GetId");
        holderSource.Should().NotContain("SetTitle");
        result.Record.Facade.Exports.Should().NotContain(e => e.Contains("_get_", StringComparison.Ordinal) || e.Contains("_set_", StringComparison.Ordinal));
        var report = File.ReadAllText(result.WrapReportPath);
        report.Should().Contain("Field accessors:   off - 4 getters and setters of objects' public fields not generated, because WrapperOptions.GenerateFieldAccessors is false");
        var flat = Regex.Replace(report, "\\s+", " ");
        flat.Should().Contain("Settings (struct) Why: it has no wrappable members: it has no public methods or constructors, it cannot be built from its fields (some are private), its public fields get no getters or setters because WrapperOptions.GenerateFieldAccessors is false (so an object made by its Default could not be read or changed)");
        var agentReadme = File.ReadAllText(Path.Combine(result.Wrapper.ProjectDirectory, "AGENT-README.txt"));
        agentReadme.Should().NotContain("Get<Field>() getters, and methods");
        agentReadme.Should().Contain("getters and setters of public fields were turned off");
        EndToEndKit.Log(nameof(with_field_accessors_turned_off_an_object_keeps_its_field_constructor_and_the_wrapper_compiles), stopwatch);
    }

    /// <summary>A crate whose objects have public fields: Holder (every field public, so a
    /// constructor from the fields), Settings (one private field and Default, so only its
    /// accessors could keep it) and a data-carrying enum (Variant() and per-variant
    /// constructors, which stay).</summary>
    private const string FieldkitLib = """
        /// A labelled box.
        #[derive(Clone, Debug, Default)]
        pub struct Holder {
            /// Its number.
            pub id: u32,
            /// Its label.
            pub title: String,
        }

        impl Holder {
            /// "<id> <title>".
            pub fn describe(&self) -> String {
                format!("{} {}", self.id, self.title)
            }
        }

        /// Settings with a private part.
        #[derive(Clone, Debug, Default)]
        pub struct Settings {
            pub name: String,
            count: u32,
        }

        /// A shape.
        #[derive(Clone, Debug)]
        pub enum Shape {
            Circle(f64),
            Rect { w: f64, h: f64 },
            Empty,
        }

        impl Shape {
            /// The area.
            pub fn area(&self) -> f64 {
                match self {
                    Shape::Circle(r) => 3.0 * r * r,
                    Shape::Rect { w, h } => w * h,
                    Shape::Empty => 0.0,
                }
            }
        }

        """;

    private const string FieldkitProgram = """
        using System;
        using E2E.Fields;

        using var holder = new Holder(7, "box");
        Console.WriteLine("holder=" + holder.Describe());
        using var shape = Shape.Rect(2, 3);
        Console.WriteLine("shape=" + shape.Variant() + " " + shape.Area());

        """;

    /// <summary>Compiles the generated header of the newest kept run with the machine's C
    /// compiler (when there is one; otherwise Compiler is null), then deletes that run folder.</summary>
    private static async Task<(string Compiler, int ExitCode, string Output)> CheckAndRemoveKeptRunAsync(string headerName)
    {
        var runs = Path.Combine(EndToEndKit.ScratchRoot, "runs");
        var header = Directory.GetFiles(runs, headerName, SearchOption.AllDirectories)
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .First();
        var run = Path.Combine(runs, Path.GetRelativePath(runs, header.FullName).Split(Path.DirectorySeparatorChar)[0]);
        try
        {
            var compiler = CCompiler.Find();
            if (compiler == null)
            {
                return (null, 0, null);
            }

            var (exitCode, output) = await CCompiler.CheckHeaderAsync(compiler, header.FullName, header.DirectoryName);
            return (compiler, exitCode, output);
        }
        finally
        {
            Directory.Delete(run, true);
        }
    }

    /// <summary>The root of the data-shaped crate.</summary>
    private const string DatakitLib = """
        use std::path::{Path, PathBuf};

        /// The most items.
        pub const MAX_ITEMS: usize = 1_000;
        pub const SCALE: f64 = 2.5;
        pub const MASK: u32 = 0xFF_00;
        pub const NEG: i16 = -7;
        pub const ENABLED: bool = true;
        pub const GREETING: &str = "hi \"there\"|\u{41}";
        pub const COMPUTED: u32 = MASK >> 4;
        pub static LIMIT: u8 = 9u8;

        #[derive(Clone, Copy, Debug, PartialEq, Default)]
        pub struct Vec2 {
            pub x: f64,
            pub y: f64,
        }

        /// Named like the type it returns.
        pub fn vec2(x: f64, y: f64) -> Vec2 {
            Vec2 { x, y }
        }

        #[derive(Clone, Copy, Debug, PartialEq, Eq, Default)]
        pub enum Mode {
            #[default]
            Fast,
            Slow,
        }

        /// A plain data struct: no methods, not Copy.
        #[derive(Clone, Debug, Default)]
        pub struct Settings {
            pub name: String,
            pub count: u32,
            pub ratio: Option<f32>,
            pub tags: Vec<u8>,
            pub origin: Vec2,
            pub mode: Mode,
            pub label: Option<String>,
            pub file: PathBuf,
            pub hint: Option<Mode>,
        }

        impl Settings {
            /// An associated constant: listed in the wrap report, not copied.
            pub const DEFAULT_COUNT: u32 = 3;
        }

        #[derive(Clone, Debug)]
        pub struct Child {
            pub depth: u8,
        }

        impl Child {
            /// Adds the depths of `others` (which may include this child itself).
            pub fn absorb(&mut self, others: &[Child]) {
                self.depth += others.iter().map(|c| c.depth).sum::<u8>();
            }
        }

        /// A data struct with lists of strings and of objects.
        #[derive(Clone, Debug)]
        pub struct Family {
            pub name: String,
            pub kids: Vec<Child>,
            pub nicknames: Vec<String>,
        }

        pub fn join_words(words: &[&str]) -> String {
            words.join(" ")
        }

        pub fn count_chars(words: Vec<String>) -> usize {
            words.iter().map(|w| w.chars().count()).sum()
        }

        pub fn total_depth(children: &[Child]) -> u32 {
            children.iter().map(|c| c.depth as u32).sum()
        }

        pub fn oldest(family: &Family) -> Option<Child> {
            family.kids.iter().max_by_key(|c| c.depth).cloned()
        }

        pub struct Holder {
            pub child: Child,
            pub tags: Vec<String>,
            pub spare: Option<Child>,
            secret: u32,
            title: String,
            children: Vec<Child>,
        }

        impl Holder {
            pub fn new(depth: u8) -> Holder {
                Holder { child: Child { depth }, tags: vec!["a".to_string(), "b".to_string()], spare: None, secret: 42, title: "box".to_string(), children: vec![Child { depth: 3 }, Child { depth: 4 }] }
            }

            pub fn secret(&self) -> &u32 {
                &self.secret
            }

            pub fn title(&self) -> &str {
                &self.title
            }

            pub fn first_child(&self) -> &Child {
                &self.children[0]
            }

            pub fn find(&self, depth: u8) -> Option<&Child> {
                self.children.iter().find(|c| c.depth == depth)
            }

            pub fn children(&self) -> Vec<Child> {
                self.children.clone()
            }

            pub fn kids(&self) -> &[Child] {
                &self.children
            }

            pub fn secret_mut(&mut self) -> &mut u32 {
                &mut self.secret
            }
        }

        pub struct Library {
            names: Vec<String>,
            data: Vec<u16>,
            mode: Mode,
        }

        impl Library {
            pub fn new() -> Library {
                Library { names: vec!["alpha".to_string()], data: vec![1, 2, 3], mode: Mode::Slow }
            }

            pub fn first(&self) -> Option<&str> {
                self.names.first().map(|s| s.as_str())
            }

            pub fn data(&self) -> &[u16] {
                &self.data
            }

            pub fn mode(&self) -> &Mode {
                &self.mode
            }
        }

        pub fn file_name(path: &Path) -> String {
            path.file_name().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
        }

        pub fn join(base: &Path, name: &str) -> PathBuf {
            base.join(name)
        }

        pub fn stem<P: AsRef<Path>>(path: P) -> String {
            path.as_ref().file_stem().map(|n| n.to_string_lossy().into_owned()).unwrap_or_default()
        }

        pub fn shout(text: impl AsRef<str>) -> String {
            text.as_ref().to_uppercase()
        }

        pub fn total_len(text: impl Into<String>) -> usize {
            text.into().len()
        }

        pub fn byte_sum<B: AsRef<[u8]>>(bytes: B) -> u32 {
            bytes.as_ref().iter().map(|b| *b as u32).sum()
        }

        pub fn sum_points(points: &[Vec2]) -> Vec2 {
            points.iter().fold(Vec2 { x: 0.0, y: 0.0 }, |a, p| Vec2 { x: a.x + p.x, y: a.y + p.y })
        }

        pub fn count_fast(modes: Vec<Mode>) -> usize {
            modes.iter().filter(|m| **m == Mode::Fast).count()
        }

        pub fn grid(n: u32) -> Vec<Vec2> {
            (0..n).map(|i| Vec2 { x: i as f64, y: i as f64 }).collect()
        }

        pub fn flags(n: u32) -> Vec<bool> {
            (0..n).map(|i| i % 2 == 0).collect()
        }

        pub fn static_name() -> &'static str {
            "datakit"
        }

        pub fn longest<'a>(a: &'a str, b: &'a str) -> &'a str {
            if a.len() >= b.len() { a } else { b }
        }

        #[cfg(unix)]
        pub fn odd_path() -> PathBuf {
            use std::os::unix::ffi::OsStringExt;
            PathBuf::from(std::ffi::OsString::from_vec(vec![0x66, 0xff]))
        }

        /// A data-carrying enum: an object with its methods and a generated Variant().
        #[derive(Clone, Debug)]
        pub enum Shape {
            Circle(f64),
            Rect { w: f64, h: f64 },
            Empty,
        }

        impl Shape {
            pub fn circle(r: f64) -> Shape {
                Shape::Circle(r)
            }

            pub fn area(&self) -> f64 {
                match self {
                    Shape::Circle(r) => 3.0 * r * r,
                    Shape::Rect { w, h } => w * h,
                    Shape::Empty => 0.0,
                }
            }
        }

        pub fn unit_square() -> Shape {
            Shape::Rect { w: 1.0, h: 1.0 }
        }

        /// A struct with a 'static str field (no lifetime parameter).
        pub struct Badge {
            pub text: &'static str,
            pub level: u8,
        }

        pub fn badge() -> Badge {
            Badge { text: "gold", level: 3 }
        }

        pub fn pick<T: Clone>(value: T) -> T {
            value
        }

        pub async fn fetch() -> u32 {
            1
        }
        """;

    /// <summary>The consumer app of the data-shaped crate.</summary>
    private const string DatakitProgram = """
        using System;
        using System.Globalization;
        using System.Linq;
        using E2E.Data;

        static string F(double v) => v.ToString(CultureInfo.InvariantCulture);

        Console.WriteLine("consts=" + Datakit.MaxItems + " " + F(Datakit.Scale) + " " + Datakit.Mask + " " + Datakit.Neg + " " + Datakit.Enabled + " " + Datakit.Limit);
        Console.WriteLine("greeting=" + Datakit.Greeting);
        var v = Datakit.Vec2(1.5, 2.0);
        Console.WriteLine("vector=" + F(v.X) + "," + F(v.Y));

        using var settings = new Settings("job", 3, 0.5f, new byte[] { 1, 2 }, new Vec2(4.0, 5.0), Mode.Slow, null, "/tmp/a.txt", null);
        Console.WriteLine("settings=" + settings.GetName() + " " + settings.GetCount() + " " + F(settings.GetRatio().Value) + " " + string.Join(",", settings.GetTags()) + " "
            + F(settings.GetOrigin().X) + "," + F(settings.GetOrigin().Y) + " " + settings.GetMode() + " " + (settings.GetLabel() ?? "none") + " " + settings.GetFile() + " " + (settings.GetHint()?.ToString() ?? "nohint"));
        settings.SetHint(Mode.Fast);
        settings.SetName("renamed");
        settings.SetCount(7);
        settings.SetRatio(null);
        settings.SetTags(new byte[] { 9 });
        settings.SetOrigin(new Vec2(0.0, 0.0));
        settings.SetMode(Mode.Fast);
        settings.SetFile("/srv/b");
        Console.WriteLine("updated=" + settings.GetName() + " " + settings.GetCount() + " " + (settings.GetRatio().HasValue ? "value" : "null") + " " + string.Join(",", settings.GetTags()) + " "
            + F(settings.GetOrigin().X) + "," + F(settings.GetOrigin().Y) + " " + settings.GetMode() + " " + (settings.GetLabel() ?? "tag") + " " + settings.GetFile() + " " + settings.GetHint());
        using var fresh = Settings.CreateDefault();
        Console.WriteLine("default=" + fresh.GetCount() + " " + fresh.GetMode());

        using var holder = new Holder(7);
        using var child = holder.GetChild();
        Console.WriteLine("holder=" + child.GetDepth() + " secret=" + holder.Secret() + " title=" + holder.Title());
        using var first = holder.FirstChild();
        using var found = holder.Find(3);
        using var missing = holder.Find(9);
        Console.WriteLine("child=" + first.GetDepth() + " found=" + found.GetDepth() + " missing=" + (missing == null ? "null" : "value"));

        var kids = holder.Kids();
        var children = holder.Children();
        Console.WriteLine("lists=" + string.Join(",", holder.GetTags()) + " " + string.Join(",", children.Select(c => c.GetDepth())) + " " + string.Join(",", kids.Select(c => c.GetDepth())) + " spare=" + (holder.GetSpare() == null ? "none" : "some"));
        foreach (var c in children.Concat(kids))
        {
            c.Dispose();
        }

        holder.SetSpare(child);
        using var spare = holder.GetSpare();
        Console.WriteLine("spare=" + spare.GetDepth());
        holder.SetSpare(null);
        Console.WriteLine("cleared=" + (holder.GetSpare() == null));
        using var a = new Child(2);
        using var b = new Child(3);
        a.Absorb(new[] { a, b });
        Console.WriteLine("absorb=" + a.GetDepth() + " total=" + Datakit.TotalDepth(new[] { a, b }));
        Console.WriteLine("words=" + Datakit.JoinWords(new[] { "to", "be", "\u00e9" }).Length + " " + Datakit.CountChars(new[] { "ab", "\u00e9t\u00e9" }));
        using var family = new Family("smith", new[] { a, b }, new[] { "x", "y" });
        var familyKids = family.GetKids();
        Console.WriteLine("family=" + family.GetName() + " " + string.Join(",", familyKids.Select(k => k.GetDepth())) + " " + string.Join(",", family.GetNicknames()));
        foreach (var k in familyKids)
        {
            k.Dispose();
        }

        family.SetNicknames(new[] { "z" });
        family.SetKids(new Child[0]);
        using var none = Datakit.Oldest(family);
        Console.WriteLine("family after=" + string.Join(",", family.GetNicknames()) + " " + family.GetKids().Length + " " + (none == null ? "none" : "some"));
        using var library = new Library();
        Console.WriteLine("library first=" + library.First() + " data=" + string.Join(",", library.Data()) + " mode=" + library.Mode());

        Console.WriteLine("file name=" + Datakit.FileName("/a/b/c.txt"));
        Console.WriteLine("join=" + Datakit.Join("/srv/x", "y.bin"));
        Console.WriteLine("stem=" + Datakit.Stem("/home/report.pdf"));
        Console.WriteLine("shout=" + Datakit.Shout("hey"));
        Console.WriteLine("total=" + Datakit.TotalLen("hello"));
        Console.WriteLine("byte sum=" + Datakit.ByteSum(new byte[] { 1, 2, 3 }));
        var sum = Datakit.SumPoints(new[] { new Vec2(1.0, 2.0), new Vec2(3.0, 4.0) });
        Console.WriteLine("sum points=" + F(sum.X) + "," + F(sum.Y));
        Console.WriteLine("count fast=" + Datakit.CountFast(new[] { Mode.Fast, Mode.Slow, Mode.Fast }));
        Console.WriteLine("grid=" + string.Join(";", Datakit.Grid(3).Select(p => F(p.X) + "," + F(p.Y))));
        Console.WriteLine("flags=" + string.Join(",", Datakit.Flags(3)));
        Console.WriteLine("static=" + Datakit.StaticName());
        Console.WriteLine("longest=" + Datakit.Longest("fig", "banana"));
        using var circle = Shape.Circle(2.0);
        using var square = Datakit.UnitSquare();
        Console.WriteLine("shapes=" + circle.Variant() + " " + F(circle.Area()) + " " + square.Variant() + " " + F(square.Area()));
        using var rect = Shape.Rect(2.0, 3.0);
        using var empty = Shape.Empty();
        Console.WriteLine("variants=" + rect.Variant() + " " + F(rect.Area()) + " " + empty.Variant() + " " + F(empty.Area()));
        using var badge = Datakit.Badge();
        Console.WriteLine("badge=" + badge.GetText() + " " + badge.GetLevel());
        try
        {
            Console.WriteLine("odd path=" + Datakit.OddPath());
        }
        catch (DatakitException)
        {
            Console.WriteLine("odd path=DatakitException");
        }
        """;
}
