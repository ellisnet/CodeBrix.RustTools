using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;

public class CrateApiModelTests
{
    public static TheoryData<string> Samples => new TheoryData<string>(ModelSamples.All);

    [Theory]
    [MemberData(nameof(Samples))]
    public void ToJson_round_trips_every_property_of_a_tool_document(string sample)
    {
        //Arrange
        var original = ModelSamples.Text(sample);

        //Act
        var written = CrateApiModel.Parse(original).ToJson();

        //Assert
        JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(written)).Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void Parse_of_ToJson_gives_the_same_json_again(string sample)
    {
        //Arrange
        var once = CrateApiModel.Parse(ModelSamples.Text(sample)).ToJson();

        //Act
        var twice = CrateApiModel.Parse(once).ToJson();

        //Assert
        twice.Should().Be(once);
    }

    [Fact]
    public void ToJson_uses_camel_case_names_snake_case_enum_values_and_omits_absent_values()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.HardCrate);

        //Act
        var json = model.ToJson();

        //Assert
        json.Should().Contain("\"schemaVersion\": 1");
        json.Should().Contain("\"typeAliases\": []");
        json.Should().Contain("\"kind\": \"impl_trait\"");
        json.Should().Contain("\"selfKind\": \"ref\"");
        json.Should().Contain("\"rootKind\": \"lib\"");
        json.Should().NotContain("null");
        json.Should().NotContain("\"isUnit\"");
        json.Should().NotContain("\"isReachable\"");
        json.Should().EndWith("}\n");
    }

    [Fact]
    public void Load_reads_the_crate_metadata_and_records_the_source_path()
    {
        //Act
        var model = ModelSamples.Load(ModelSamples.DataCrate);

        //Assert
        model.SchemaVersion.Should().Be(1);
        model.Tool.Name.Should().Be("codebrix-api-extractor");
        model.Crate.Name.Should().Be("datacrate");
        model.Crate.Edition.Should().Be("2024");
        model.Crate.LibName.Should().Be("datacrate");
        model.Crate.CrateTypes.Should().Equal("lib");
        model.Crate.IsCdylib.Should().BeFalse();
        model.Crate.RootKind.Should().Be(RustRootKind.Lib);
        model.Crate.RootFile.Should().Be("src/lib.rs");
        model.Crate.Docs.Should().Contain("plain Rust library");
        model.SourcePath.Should().Be(Path.GetFullPath(ModelSamples.PathOf(ModelSamples.DataCrate)));
        model.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Lookups_find_items_by_name_canonical_path_and_public_path()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);

        //Act
        var byName = model.FindStruct("Rect");
        var byCanonical = model.FindStruct("crate::shapes::Rect");
        var byReexport = model.FindStruct("crate::Rect");

        //Assert
        byName.Should().BeSameAs(byCanonical);
        byReexport.Should().BeSameAs(byCanonical);
        byName.PublicPaths.Should().Equal("crate::Rect", "crate::shapes::Rect");
        byName.PrimaryPublicPath.Should().Be("crate::Rect");
        model.FindById("struct:crate::shapes::Rect").Should().BeSameAs(byName);
        model.FindByPath("crate::Rect").Should().ContainSingle();
        model.FindFunction("checksum").Signature.Should().Be("pub fn checksum(data: &[u8]) -> u32");
        model.FindFunction("crate::Vec2::dot").Should().BeNull();
    }

    [Fact]
    public void MethodsOf_returns_a_types_inherent_methods_with_their_receivers()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        var encoder = model.FindStruct("Encoder");

        //Act
        var methods = model.MethodsOf(encoder);

        //Assert
        methods.Select(m => m.Name).Should().Equal("new", "encode", "frames");
        methods.Select(m => m.SelfKind).Should().Equal(RustSelfKind.None, RustSelfKind.RefMut, RustSelfKind.Ref);
        methods[0].ReturnType.IsSelf.Should().BeTrue();
        methods.Should().OnlyContain(m => m.IsMethod && m.Owner.ResolvedPath == "crate::Encoder");
        model.FindMethod(encoder, "frames").PublicPaths.Should().Equal("crate::Encoder::frames");
        model.FreeFunctions.Should().NotContain(f => f.Name == "encode");
    }

    [Fact]
    public void Struct_and_enum_helpers_report_traits_repr_and_shapes()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);

        //Act
        var vec2 = model.FindStruct("Vec2");
        var encoder = model.FindStruct("Encoder");
        var tick = model.FindStruct("Tick");
        var mode = model.FindEnum("Mode");
        var parseError = model.FindEnum("ParseError");

        //Assert
        vec2.IsCopy.Should().BeTrue();
        vec2.IsReprC.Should().BeTrue();
        vec2.AllFieldsPublic.Should().BeTrue();
        encoder.IsCopy.Should().BeFalse();
        encoder.AllFieldsPublic.Should().BeFalse();
        tick.Shape.Should().Be(RustItemShape.Tuple);
        tick.TupleArity.Should().Be(1);
        mode.IsCLike.Should().BeTrue();
        mode.HasDataVariants.Should().BeFalse();
        mode.FindVariant("B").DiscriminantValue.Should().Be(2);
        mode.Repr.Should().Equal("i32");
        parseError.Implements("Display").Should().BeTrue();
        model.TraitImplsOf("crate::ParseError").Select(t => t.TraitName).Should().Equal("Display", "Error");
    }

    [Fact]
    public void Edge_case_document_keeps_diagnostics_generics_traits_and_associated_consts()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.EdgeCases);

        //Act
        var g = model.FindStruct("G");
        var speak = model.Traits.Single(t => t.Name == "Speak");

        //Assert
        model.Crate.Version.Should().Be("workspace");
        model.Crate.CrateTypes.Should().Equal("cdylib", "rlib");
        model.Crate.IsCdylib.Should().BeTrue();
        model.Errors.Select(d => d.Code).Should().Equal("parse_error");
        model.Diagnostics.Select(d => d.Code).Should().Contain("module_file_not_found");
        g.HasGenerics.Should().BeTrue();
        g.Generics.Lifetimes.Should().Equal("'a");
        g.Generics.ConstParams.Single().Default.Should().Be("3");
        model.AssociatedConstsOf(g).Single().Value.Should().Be("42");
        speak.Methods.Select(m => m.HasDefault).Should().Equal(false, true);
        model.FindFunction("c_export").IsExternC.Should().BeTrue();
        model.FindFunction("c_export").IsNoMangle.Should().BeTrue();
        model.FindFunction("hidden_one").IsDocHidden.Should().BeTrue();
        model.FindFunction("hidden_one").HasAttribute("#[deprecated").Should().BeTrue();
        model.FindStruct("Dog").ImplementedTraits.Should().Equal("Default", "Send", "Speak");
        model.FindEnum("Signed").Variants.Select(v => v.DiscriminantValue).Should().Equal(-1L, 16L, null);
    }

    [Fact]
    public void Parse_rejects_another_schema_version()
    {
        //Act
        var act = () => CrateApiModel.Parse("{ \"schemaVersion\": 2 }");

        //Assert
        act.Should().Throw<InvalidDataException>().WithMessage("*schema version 2*");
    }

    [Fact]
    public void Parse_rejects_text_that_is_not_json()
    {
        //Act
        var act = () => CrateApiModel.Parse("not json");

        //Assert
        act.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void Parse_of_a_minimal_document_gives_empty_lists_not_null()
    {
        //Act
        var model = CrateApiModel.Parse("{ \"schemaVersion\": 1, \"crate\": { \"name\": \"x\" } }");

        //Assert
        model.Functions.Should().BeEmpty();
        model.Diagnostics.Should().BeEmpty();
        model.Crate.CrateTypes.Should().BeEmpty();
        model.AllItems.Should().BeEmpty();
    }
}
