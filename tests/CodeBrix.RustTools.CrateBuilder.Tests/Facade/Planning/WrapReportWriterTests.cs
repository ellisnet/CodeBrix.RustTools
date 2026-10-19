using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class WrapReportWriterTests
{
    [Fact]
    public void Render_describes_the_wrapped_and_skipped_items_in_plain_language()
    {
        //Arrange
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Act
        var text = WrapReportWriter.Render(plan);

        //Assert
        text.Should().StartWith("WRAP REPORT: datacrate\n======================\n");
        text.Should().Contain("a C layer (\"datacrate_facade\") was generated over the crate's Rust API");
        text.Should().Contain("Wrapped types:     7 (1 object to dispose, 4 value types, 2 enums)");
        text.Should().Contain("Wrapped functions: 18");
        text.Should().Contain("Dispose it when you are done");
        text.Should().Contain("  Encoder   (Rust: Encoder)");
        text.Should().Contain("  Vec2 { double X; double Y }   (Rust: Vec2)");
        text.Should().Contain("  Mode { A = 1, B = 2 }   (Rust: Mode)");
        text.Should().Contain("  static uint Checksum(ReadOnlySpan<byte> data)\n");
        text.Should().Contain("  static (int, int) ParsePair(string s)   (can fail)\n");
        text.Should().Contain("  static nuint? Find(ReadOnlySpan<int> xs, int x)   (null when there is no result)\n");
        text.Should().Contain("  byte[] Encode(ReadOnlySpan<byte> input)\n");
        text.Should().Contain("  static Encoder New(EncoderConfig cfg)   (returns a new object you must dispose)\n");
        text.Should().Contain("  void Dispose()\n");
        text.Should().Contain("  wasm_only (function)\n    Why: it is only built for target_arch = \"wasm32\"");
        text.Should().Contain("Starting point:");
        text.Should().Contain("NOTES FROM THE CRATE'S OWN DOCUMENTATION");
        text.Should().Contain("Tick: A point in time, in ticks (ticks per second = 254016000000");
        text.Should().Contain("WARNINGS\n--------\n  (none)\n");
        text.Should().NotContain("\r");
        text.Should().NotContain("datacrate_free_buffer");
    }

    [Fact]
    public void Render_of_the_hard_crate_lists_what_to_do()
    {
        //Arrange
        var plan = PlannerFixtures.Plan(ModelSamples.HardCrate);

        //Act
        var text = WrapReportWriter.Render(plan);

        //Assert
        text.Should().Contain("Wrapped types:     2 (2 objects to dispose, 0 value types, 0 enums)");
        text.Should().Contain("  static int Add(int a, int b)\n");
        text.Should().Contain("adding a FacadeOverride");
        text.Should().Contain("  each (function)\n    Why: it takes a callback (impl FnMut(i32)) in its parameter 'f', which is not");
        text.Should().Contain("      pub unsafe extern \"C\" fn hardcrate_each(");
        text.Should().Contain("  Names::tag (field)\n    Why: it is a public field whose type is shared ownership");
    }

    [Fact]
    public void Render_of_a_pass_through_crate_says_so_and_shows_the_warnings()
    {
        //Arrange
        var plan = PlannerFixtures.Plan(ModelSamples.EdgeCases);

        //Act
        var text = WrapReportWriter.Render(plan);

        //Assert
        text.Should().Contain("the crate already exports C functions; they are used as they are");
        text.Should().Contain("  static int CExport(int a)\n    C export c_export.");
        text.Should().Contain("  - Part of the crate could not be read (src/broken.rs:1)");
        text.Should().NotContain("Errors: every call can throw");
    }

    [Fact]
    public void Render_shows_overrides_and_plan_notes()
    {
        //Arrange
        var wrap = new WrapPlan { Notes = "Kept for the demo.\nSecond line.", Overrides = { new FacadeOverride { RustPath = "wasm_only", RustSource = "// r", CSharpSource = "// c" } } };
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Act
        var text = WrapReportWriter.Render(plan);

        //Assert
        text.Should().Contain("HAND-WRITTEN OVERRIDES\n----------------------\n  wasm_only (Rust and C# source)\n");
        text.Should().EndWith("YOUR WRAPPLAN NOTES\n-------------------\n  Kept for the demo.\n  Second line.\n");
        text.Should().NotContain("  wasm_only (function)");
    }

    [Theory]
    [InlineData("// r", null, "  wasm_only (Rust source)\n")]
    [InlineData(null, "// c", "  wasm_only (C# source)\n")]
    [InlineData("// r", "// c", "  wasm_only (Rust and C# source)\n")]
    public void Render_names_which_sources_an_override_has(string rustSource, string csharpSource, string expected)
    {
        //Arrange
        var wrap = new WrapPlan { Overrides = { new FacadeOverride { RustPath = "wasm_only", RustSource = rustSource, CSharpSource = csharpSource } } };
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Act
        var text = WrapReportWriter.Render(plan);

        //Assert
        text.Should().Contain("HAND-WRITTEN OVERRIDES\n----------------------\n" + expected);
    }

    [Fact]
    public void Write_writes_the_report_and_its_json_twin_without_a_byte_order_mark()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);
        var path = Path.Combine(sandbox.Directory, "out", WrapReportWriter.DefaultFileName);

        //Act
        WrapReportWriter.Write(plan, path);

        //Assert
        var jsonPath = WrapReportWriter.JsonPathFor(path);
        jsonPath.Should().Be(Path.Combine(sandbox.Directory, "out", "WRAP-REPORT.json"));
        File.ReadAllText(path, Encoding.UTF8).Should().Be(WrapReportWriter.Render(plan));
        File.ReadAllText(jsonPath, Encoding.UTF8).Should().Be(WrapReportWriter.RenderJson(plan));
        File.ReadAllBytes(path)[0].Should().Be((byte)'W');
        File.ReadAllBytes(jsonPath)[0].Should().Be((byte)'{');
        JsonNode.Parse(File.ReadAllText(jsonPath))["libName"].GetValue<string>().Should().Be("datacrate");
    }

    [Fact]
    public void JsonPathFor_appends_json_to_a_path_without_an_extension()
        => WrapReportWriter.JsonPathFor("/tmp/report").Should().Be("/tmp/report.json");
}
