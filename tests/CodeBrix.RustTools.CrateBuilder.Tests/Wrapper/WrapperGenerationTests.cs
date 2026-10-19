using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Generation;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Model;
using CodeBrix.RustTools.CrateBuilder.Wrapper.Rendering;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

public class WrapperGenerationTests
{
    [Theory]
    [InlineData("frame_size", "FrameSize")]
    [InlineData("test_project_13", "TestProject13")]
    [InlineData("Vec2", "Vec2")]
    [InlineData("datacrate", "Datacrate")]
    [InlineData("my-crate", "MyCrate")]
    public void Pascal_turns_rust_names_into_csharp_type_names(string rust, string expected) =>
        CSharpNames.Pascal(rust).Should().Be(expected);

    [Theory]
    [InlineData("frame_size", "frameSize")]
    [InlineData("in", "@in")]
    [InlineData("event", "@event")]
    [InlineData("x", "x")]
    [InlineData("URLPath", "urlPath")]
    public void Camel_gives_escaped_parameter_names(string rust, string expected) =>
        CSharpNames.Camel(rust).Should().Be(expected);

    [Theory]
    [InlineData("[[f64; 3]; 4]", "double", new[] { 4, 3 })]
    [InlineData("[u8; 16]", "byte", new[] { 16 })]
    [InlineData("[f32;2]", "float", new[] { 2 })]
    public void ParseArray_reads_lengths_outermost_first(string rust, string element, int[] lengths)
    {
        //Arrange
        var found = new List<int>();

        //Act
        var parsed = ManagedTypes.ParseArray(rust, found);

        //Assert
        parsed.Should().Be(element);
        found.Should().Equal(lengths);
    }

    [Fact]
    public void ArrayType_registers_one_inline_array_per_level()
    {
        //Arrange
        var types = new ManagedTypes("XNative");

        //Act
        var name = types.ArrayType(WrapperPlanFixtures.Array("[[f64; 3]; 4]"));

        //Assert
        name.Should().Be("DoubleArray4x3");
        types.Arrays.Select(a => a.Name + ":" + a.Length + ":" + a.ElementType).Should().Equal("DoubleArray3:3:double", "DoubleArray4x3:4:DoubleArray3");
    }

    [Fact]
    public void XmlDocText_makes_a_summary_remarks_and_code_from_rust_docs()
    {
        //Act
        var lines = XmlDocText.Lines("Parses `a,b`; see [`Rect`].\n\nSecond <paragraph> & more.\n\n```\nlet x = 1;\n# hidden\n```", "fallback");

        //Assert
        lines.Should().Equal(
            "<summary>",
            "Parses <c>a,b</c>; see <c>Rect</c>.",
            "</summary>",
            "<remarks>",
            "<para>",
            "Second &lt;paragraph&gt; &amp; more.",
            "</para>",
            "<code>",
            "let x = 1;",
            "</code>",
            "</remarks>");
        XmlDocText.Lines(null, "Calls <c>x</c>.").Should().Equal("<summary>Calls <c>x</c>.</summary>");
        XmlDocText.Summary("Parses [`Rect`] values.\nTwo lines.\n\nMore.", "f").Should().Be("Parses Rect values. Two lines.");
    }

    [Fact]
    public void NameScope_renames_a_member_that_collides()
    {
        //Arrange
        var scope = new NameScope(new[] { "Length", "ToString" });

        //Act
        var names = new[] { scope.Claim("Length"), scope.Claim("Length"), scope.Claim("Area"), scope.Claim("ToString") };

        //Assert
        names.Should().Equal("GetLength", "GetLength2", "Area", "GetToString");
    }

    [Fact]
    public void every_embedded_template_parses_and_the_set_is_complete()
    {
        //Act
        var names = WrapperTemplates.EmbeddedNames();

        //Assert
        names.Should().Equal(
            "AGENT-README.txt.sbn", "AssemblyInfo.cs.sbn", "Enum.cs.sbn", "Exception.cs.sbn", "Handle.cs.sbn", "InlineArray.cs.sbn", "Library.cs.sbn",
            "Mirror.cs.sbn", "Native.cs.sbn", "Project.csproj.sbn", "README.md.sbn", "SmokeTests.cs.sbn", "THIRD-PARTY-NOTICES.txt.sbn", "WRAP-REPORT.txt.sbn");
        foreach (var name in names)
        {
            CodeBrix.Templating.Template.Parse(WrapperTemplates.Text(name), name).HasErrors.Should().BeFalse(name);
        }
    }

    [Fact]
    public void a_template_variable_the_model_does_not_have_is_an_error_not_an_empty_string()
    {
        //Arrange
        var model = new WrapperModel { AssemblyName = "A", RootNamespace = "A" };

        //Act
        var act = () => WrapperTemplates.Render(WrapperTemplates.Mirror, model, new { name = "X" }, CancellationToken.None);

        //Assert
        act.Should().Throw<WrapperGenerationException>().Which.Message.Should().Contain("Mirror.cs.sbn");
    }

    [Fact]
    public void the_model_builder_places_mirror_methods_on_the_mirror_and_enum_functions_in_the_library_class()
    {
        //Arrange
        var input = new WrapperInput { Plan = WrapperPlanFixtures.DataCrate(), Options = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" } };

        //Act
        var model = WrapperModelBuilder.Build(input);

        //Assert
        model.LibClassName.Should().Be("Datacrate");
        model.NativeClassName.Should().Be("DatacrateNative");
        model.ExceptionClassName.Should().Be("DatacrateException");
        model.Mirrors.Single(m => m.Name == "Vec2").BodyLines.Should().Contain(l => l.Contains("public unsafe double Dot(Vec2 other)"));
        model.Mirrors.Single(m => m.Name == "Rect").BodyLines.Should().Contain(l => l.Contains("public unsafe bool Contains(Vec2 p)"));
        model.FreeFunctionLines.Should().Contain(l => l.Contains("ModeFromName(string name)"));
        model.FreeFunctionLines.Should().NotContain(l => l.Contains(" Dot("));
        model.Handles.Select(h => h.Name).Should().Equal("Decoder", "Encoder");
        model.ParameterlessConstructions.Should().Equal("Encoder.Default()");
        model.ApiSections.First().Title.Should().Be("static class Datacrate");
    }

    [Fact]
    public void a_library_class_named_like_a_type_gets_a_suffix()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        plan.Handles.Single(h => h.ManagedName == "Decoder").ManagedName = "Datacrate";
        var input = new WrapperInput { Plan = plan, Options = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" } };

        //Act
        var model = WrapperModelBuilder.Build(input);

        //Assert
        model.LibClassName.Should().Be("DatacrateLibrary");
        model.LibClassNote.Should().Contain("the crate has a type named Datacrate");
    }

    [Fact]
    public void a_root_namespace_ending_in_the_library_name_gives_the_library_suffix_with_a_note_and_a_first_example()
    {
        //Arrange
        var input = new WrapperInput { Plan = WrapperPlanFixtures.DataCrate(), Options = new WrapperOptions { AssemblyName = "Acme.Datacrate", RootNamespace = "Acme.Datacrate" } };

        //Act
        var model = WrapperModelBuilder.Build(input);
        var plain = WrapperModelBuilder.Build(new WrapperInput { Plan = WrapperPlanFixtures.DataCrate(), Options = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" } });

        //Assert
        model.LibClassName.Should().Be("DatacrateLibrary");
        model.LibClassNote.Should().Contain("the root namespace ends in Datacrate");
        model.FirstEntryMethodName.Should().NotBeNullOrWhiteSpace();
        model.FirstEntrySignature.Should().Contain("DatacrateLibrary." + model.FirstEntryMethodName + "(");
        plain.LibClassNote.Should().BeNull();
        WrapperModelBuilder.EntryMethodName("static (int, int) Datacrate.ParsePair(string s)").Should().Be("ParsePair");
        WrapperModelBuilder.EntryMethodName(null).Should().BeNull();
    }

    [Fact]
    public void the_first_example_skips_constants_and_the_readme_signatures_are_not_global_qualified()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        plan.Constants.Add(new ConstantMapping { RustPath = "crate::REFERENCE_LEVEL", RustType = "f64", RustValue = "0.26496256", ManagedType = "double", ManagedLiteral = "0.26496256d", ManagedName = "ReferenceLevel" });
        plan.Functions.Single(f => f.RustPath == "default_settings" || f.RustPath == "crate::default_settings").ManagedName = "Settings";
        var input = new WrapperInput { Plan = plan, Options = new WrapperOptions { AssemblyName = "Acme.Data", RootNamespace = "Acme.Data" } };

        //Act
        var model = WrapperModelBuilder.Build(input);
        var readme = WrapperTemplates.Render(WrapperTemplates.Readme, model, null, CancellationToken.None);
        var agentReadme = WrapperTemplates.Render(WrapperTemplates.AgentReadme, model, null, CancellationToken.None);

        //Assert
        model.ApiSections[0].Entries[0].Signature.Should().StartWith("const double Datacrate.ReferenceLevel = ");
        model.FirstEntrySignature.Should().NotStartWith("const ");
        model.FirstEntryMethodName.Should().MatchRegex("^[A-Z][A-Za-z0-9]*$");
        model.FirstEntrySignature.Should().Contain("Datacrate." + model.FirstEntryMethodName + "(");
        model.ApiSections.SelectMany(s => s.Entries).Should().Contain(e => e.Signature == "static Settings Datacrate.Settings()");
        model.ApiSections.SelectMany(s => s.Entries).Should().NotContain(e => e.Signature.Contains("global::", System.StringComparison.Ordinal));
        model.FreeFunctionLines.Should().Contain(l => l.Contains("global::Acme.Data.Settings", System.StringComparison.Ordinal), "the generated code keeps the qualification it needs");
        readme.Should().Contain("Datacrate." + model.FirstEntryMethodName + "(/* ... */);");
        readme.Should().NotContain("Datacrate.26496256d");
        agentReadme.Should().Contain("Datacrate." + model.FirstEntryMethodName + "(/* ... */);");
        agentReadme.Should().NotContain("global::");
        WrapperModelBuilder.IsConstantEntry("const int X.Y = 1").Should().BeTrue();
        WrapperModelBuilder.IsConstantEntry("static int X.Constant()").Should().BeFalse();
        WrapperModelBuilder.DocSignature("static global::A.B.Vec2 C.Vec2(global::A.B.Vec2 v, global::A.Bx.Q q)", "A.B").Should().Be("static Vec2 C.Vec2(Vec2 v, global::A.Bx.Q q)");
        WrapperModelBuilder.DocSignature(null, "A").Should().BeNull();
    }

    [Fact]
    public void a_plan_without_a_lib_name_is_rejected()
    {
        //Arrange
        var plan = new MappingPlan();
        var input = new WrapperInput { Plan = plan, Options = new WrapperOptions { AssemblyName = "A", RootNamespace = "A" } };

        //Act
        var act = () => WrapperModelBuilder.Build(input);

        //Assert
        act.Should().Throw<WrapperGenerationException>().Which.Message.Should().Contain("LibName");
    }
}
