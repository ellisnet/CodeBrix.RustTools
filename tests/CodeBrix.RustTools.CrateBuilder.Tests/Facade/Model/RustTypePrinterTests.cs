using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;

public class RustTypePrinterTests
{
    public static TheoryData<string> Samples => new TheoryData<string>(ModelSamples.All);

    private static IEnumerable<RustType> TopLevelTypes(CrateApiModel model)
        => model.Functions.SelectMany(f => f.Params.Select(p => p.Type).Append(f.ReturnType))
            .Concat(model.Structs.SelectMany(s => s.Fields.Select(x => x.Type)))
            .Concat(model.Enums.SelectMany(e => e.Variants.SelectMany(v => v.Fields.Select(x => x.Type))))
            .Concat(model.Constants.Select(c => c.Type))
            .Concat(model.TypeAliases.Select(a => a.Type));

    [Theory]
    [MemberData(nameof(Samples))]
    public void Print_reproduces_the_tools_text_for_every_type_without_a_qualified_path(string sample)
    {
        //Arrange
        var model = ModelSamples.Load(sample);
        var types = TopLevelTypes(model).SelectMany(t => t.DescendantsAndSelf()).Where(t => !t.DescendantsAndSelf().Any(d => d.Qself != null)).ToList();

        //Act
        var mismatches = types.Where(t => RustTypePrinter.Print(t) != t.Text).Select(t => t.Text + " => " + RustTypePrinter.Print(t)).ToList();

        //Assert
        types.Should().NotBeEmpty();
        mismatches.Should().BeEmpty();
    }

    [Fact]
    public void Print_spells_each_kind_like_rustfmt()
    {
        //Arrange
        var u8 = new RustType { Kind = RustTypeKind.Primitive, Name = "u8" };
        var str = new RustType { Kind = RustTypeKind.Primitive, Name = "str" };

        //Act
        var printed = new[]
        {
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Reference, Mutable = true, Inner = new RustType { Kind = RustTypeKind.Slice, Elem = u8 } }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Reference, Lifetime = "'a", Mutable = false, Inner = str }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Array, Elem = u8, Len = "4" }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Tuple, Elems = new List<RustType> { u8 } }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Ptr, Mutable = false, Inner = u8 }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.FnPtr, Abi = "C", IsUnsafe = true, Inputs = new List<RustType> { u8 }, Output = new RustType { Kind = RustTypeKind.Unit } }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.Path, Segments = new List<RustPathSegment> { new RustPathSegment { Name = "Option", Args = new List<RustType> { u8 } } } }),
            RustTypePrinter.Print(new RustType { Kind = RustTypeKind.DynTrait, Bounds = new List<string> { "Fn(i32) -> i32", "Send" } }),
            RustTypePrinter.Print(null),
        };

        //Assert
        printed.Should().Equal("&mut [u8]", "&'a str", "[u8; 4]", "(u8,)", "*const u8", "unsafe extern \"C\" fn(u8)", "Option<u8>", "dyn Fn(i32) -> i32 + Send", string.Empty);
    }

    [Fact]
    public void Type_helpers_classify_the_fixture_signatures()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);

        //Act
        var greet = model.FindFunction("greet");
        var find = model.FindFunction("find");
        var invert = model.FindFunction("invert");

        //Assert
        greet.Params.Single().Type.IsStrReference.Should().BeTrue();
        greet.ReturnType.IsPathNamed("String").Should().BeTrue();
        find.Params[0].Type.IsSliceReference.Should().BeTrue();
        find.ReturnType.IsPathNamed("Option").Should().BeTrue();
        find.ReturnType.GenericArguments.Single().IsPrimitiveNamed("usize").Should().BeTrue();
        invert.ReturnType.Kind.Should().Be(RustTypeKind.Array);
        invert.ReturnType.LenValue.Should().Be(3UL);
        invert.ReturnType.Elem.Kind.Should().Be(RustTypeKind.Array);
        invert.ReturnType.ToString().Should().Be("[[f64; 3]; 3]");
        model.ResolveLocalType(model.FindFunction("parse_pair").ReturnType.GenericArguments[1]).Should().BeSameAs(model.FindEnum("ParseError"));
    }
}
