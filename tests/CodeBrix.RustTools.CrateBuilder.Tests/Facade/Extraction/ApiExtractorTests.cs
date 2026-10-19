using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Extraction;

public class ApiExtractorTests
{
    // ---- unit ----

    [Fact]
    public void BuildArguments_passes_manifest_out_crate_and_features()
    {
        //Arrange
        var workspace = ExtractionTestContext.Workspace("/stage", "datacrate");
        var request = new CrateBuildRequest { Features = new List<string> { "a", " b ", "" } };

        //Act
        var arguments = ApiExtractor.BuildArguments("/stage/Cargo.toml", "-", workspace, request);

        //Assert
        arguments.Should().Equal("--manifest", "/stage/Cargo.toml", "--out", "-", "--crate", "datacrate", "--features", "a,b");
    }

    [Fact]
    public void BuildArguments_leaves_out_the_crate_of_a_facade_and_empty_features()
    {
        //Arrange
        var workspace = ExtractionTestContext.Workspace("/stage", "facade_crate");
        workspace.IsFacade = true;

        //Act
        var arguments = ApiExtractor.BuildArguments("/stage/Cargo.toml", "/run/api.json", workspace, new CrateBuildRequest());

        //Assert
        arguments.Should().Equal("--manifest", "/stage/Cargo.toml", "--out", "/run/api.json");
    }

    [Fact]
    public async Task ChooseRouteAsync_uses_the_host_in_host_mode()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ExtractionTestContext.Create(sandbox.Directory, sandbox.Directory);

        //Act
        var route = await new ApiExtractor().ChooseRouteAsync(new CrateBuildRequest { Mode = BuildMode.Host }, context, TestContext.Current.CancellationToken);

        //Assert
        route.Should().Be(ApiExtractor.ExtractionRoute.Host);
    }

    [Fact]
    public async Task ChooseRouteAsync_honours_a_forced_docker_route()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ExtractionTestContext.Create(sandbox.Directory, sandbox.Directory);
        var extractor = new ApiExtractor(new ApiExtractorOptions { PreferHostToolchain = false, DockerRoute = ApiExtractorDockerRoute.RunInContainer });

        //Act
        var route = await extractor.ChooseRouteAsync(new CrateBuildRequest { Mode = BuildMode.Docker }, context, TestContext.Current.CancellationToken);

        //Assert
        route.Should().Be(ApiExtractor.ExtractionRoute.InContainer);
    }

    [Fact]
    public async Task ExtractAsync_without_a_staged_manifest_fails_in_the_extracting_api_stage()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var context = ExtractionTestContext.Create(sandbox.Directory, sandbox.Directory);

        //Act
        var act = () => new ApiExtractor().ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory), new CrateBuildRequest(), context, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<FacadeGenerationException>();
        thrown.Which.Stage.Should().Be(CrateBuildStage.ExtractingApi);
    }

    // ---- live, host toolchain ----

    private static async Task<(CrateApiModel Model, CrateBuilderContext Context)> ExtractSampleAsync(int sample, CrateBuildRequest request = null)
    {
        using var sandbox = SampleSandbox.FromSample(sample);
        var context = ExtractionTestContext.Create(ExtractionTestContext.SharedScratchRoot, sandbox.CreateDirectory("run"));
        var model = await new ApiExtractor().ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory), request ?? new CrateBuildRequest(), context, TestContext.Current.CancellationToken);
        return (model, context);
    }

    [Fact]
    public async Task live_host_extracts_the_data_crate_fixture()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();

        //Act
        var (model, context) = await ExtractSampleAsync(15);

        //Assert
        model.Crate.Name.Should().Be("datacrate");
        model.Crate.LibName.Should().Be("datacrate");
        model.Diagnostics.Should().BeEmpty();
        context.Warnings.Should().BeEmpty();
        model.Functions.Should().HaveCount(19);
        model.FreeFunctions.Select(f => f.Name).Should().Equal("checksum", "greet", "parse_pair", "invert", "find", "wasm_only");
        model.Structs.Select(s => s.Name).Should().Equal("Rect", "Tick", "Vec2", "EncoderConfig", "Encoder");
        model.Enums.Select(e => e.Name).Should().Equal("Mode", "ParseError");

        var vec2 = model.FindStruct("Vec2");
        vec2.Derives.Should().Contain("Copy");
        vec2.IsCopy.Should().BeTrue();
        vec2.Fields.Select(f => f.Name + ":" + f.Type.Text).Should().Equal("x:f64", "y:f64");
        model.MethodsOf(vec2).Select(m => m.Name).Should().Equal("new", "dot", "length", "lerp");

        var encoder = model.FindStruct("Encoder");
        encoder.IsCopy.Should().BeFalse();
        var methods = model.MethodsOf(encoder);
        methods.Should().HaveCount(3);
        methods.Select(m => m.Name + ":" + m.SelfKind).Should().Equal("new:None", "encode:RefMut", "frames:Ref");
        methods[1].Params.Single().Type.IsSliceReference.Should().BeTrue();
        methods[1].ReturnType.Text.Should().Be("Vec<u8>");

        var mode = model.FindEnum("Mode");
        mode.IsCLike.Should().BeTrue();
        mode.Variants.Select(v => v.Name + "=" + v.DiscriminantValue).Should().Equal("A=1", "B=2");
        model.MethodsOf(mode).Single().ReturnType.Text.Should().Be("Option<Mode>");

        var wasm = model.FindFunction("wasm_only");
        wasm.Cfg.Should().Equal("target_arch = \"wasm32\"");
        wasm.CfgMentions("wasm32").Should().BeTrue();

        model.AllItems.Select(i => i.Name).Should().NotContain(new[] { "crate_only_helper", "private_helper", "reset_internal" });

        var rect = model.FindStruct("Rect");
        rect.PublicPaths.Should().Equal("crate::Rect", "crate::shapes::Rect");
        model.MethodsOf(rect).Select(m => m.Name).Should().Equal("from_size", "width", "union");
        model.FindMethod(rect, "union").Params.Single().Type.Text.Should().Be("&Rect");

        var tick = model.FindStruct("Tick");
        tick.Shape.Should().Be(RustItemShape.Tuple);
        tick.TupleArity.Should().Be(1);
        tick.Docs.Should().Contain("254016000000");
        tick.PublicPaths.Should().Equal("crate::Tick");
        tick.CanonicalPath.Should().Be("crate::time::Tick");
        model.Constants.Single().Value.Should().Be("254_016_000_000");

        model.FindFunction("parse_pair").ReturnType.Text.Should().Be("Result<(i32, i32), ParseError>");
        model.FindFunction("invert").Params.Single().Type.Text.Should().Be("[[f64; 3]; 3]");
        model.FindFunction("find").ReturnType.Text.Should().Be("Option<usize>");
        model.FindEnum("ParseError").Implements("Display").Should().BeTrue();
        model.AllItems.Where(i => i is RustFunction || i is RustStruct || i is RustEnum).Should().OnlyContain(i => i.Docs.Length > 0);
    }

    [Fact]
    public async Task live_host_extracts_the_unmappable_shapes_fixture()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();

        //Act
        var (model, _) = await ExtractSampleAsync(17, new CrateBuildRequest { Features = new List<string> { "extra" } });

        //Assert
        model.Crate.Name.Should().Be("hardcrate");
        model.Crate.DeclaredFeatures.Should().Equal("default", "extra");
        model.Crate.RequestedFeatures.Should().Equal("extra");
        model.FindFunction("max_of").HasGenerics.Should().BeTrue();
        model.FindFunction("max_of").Generics.TypeParams.Single().Bounds.Should().Equal("PartialOrd");
        model.FindFunction("describe").Params.Single().Type.Kind.Should().Be(RustTypeKind.ImplTrait);
        model.FindFunction("each").Params[1].Type.Kind.Should().Be(RustTypeKind.ImplTrait);
        model.FindFunction("each").Params[1].Type.Bounds.Should().Equal("FnMut(i32)");
        model.FindFunction("apply").Params[0].Type.Inner.Kind.Should().Be(RustTypeKind.DynTrait);
        model.FindFunction("evens").ReturnType.Kind.Should().Be(RustTypeKind.ImplTrait);
        model.FindFunction("doubled_later").IsAsync.Should().BeTrue();
        model.FindFunction("total_area").Params.Single().Type.GenericArguments.Single().IsPathNamed("Shape").Should().BeTrue();
        model.FindFunction("unit_squares").ReturnType.Text.Should().Be("Vec<Shape>");

        var shape = model.FindEnum("Shape");
        shape.IsCLike.Should().BeFalse();
        shape.Variants.Select(v => v.Name + ":" + v.Shape).Should().Equal("Circle:Tuple", "Rect:Named");
        shape.FindVariant("Rect").Fields.Select(f => f.Name).Should().Equal("w", "h");

        var names = model.FindStruct("Names");
        names.Fields.Select(f => f.Type.PathName).Should().Equal("Rc", "Arc");
        model.FindMethod(names, "first").ReturnType.IsStrReference.Should().BeTrue();

        model.FindFunction("add").Params.Should().OnlyContain(p => p.Type.IsPrimitiveNamed("i32"));
        model.FindFunction("scale").ReturnType.IsPrimitiveNamed("f64").Should().BeTrue();
    }

    [Fact]
    public async Task live_host_smoke_on_the_binary_only_sample_02_reads_the_binary_root_and_warns()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();

        //Act
        var (model, context) = await ExtractSampleAsync(2);

        //Assert
        model.Crate.Name.Should().Be("test_project_02");
        model.Crate.RootKind.Should().Be(RustRootKind.Bin);
        model.Crate.RootFile.Should().Be("src/main.rs");
        model.Diagnostics.Select(d => d.Code).Should().Equal("no_library_target");
        context.Warnings.Should().ContainSingle(w => w.Contains("no_library_target"));
        model.AllItems.Should().BeEmpty();
        model.Modules.Select(m => m.Path).Should().Contain("crate");
    }

    [Fact]
    public async Task live_host_a_crate_without_a_root_fails_with_the_tools_message()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", "[package]\nname = \"noroot\"\nversion = \"0.1.0\"\nedition = \"2024\"\n");
        var context = ExtractionTestContext.Create(ExtractionTestContext.SharedScratchRoot, sandbox.CreateDirectory("run"));

        //Act
        var act = () => new ApiExtractor().ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory), new CrateBuildRequest(), context, TestContext.Current.CancellationToken);

        //Assert
        var thrown = await act.Should().ThrowAsync<FacadeGenerationException>();
        thrown.Which.Stage.Should().Be(CrateBuildStage.ExtractingApi);
        thrown.Which.Message.Should().Contain("no crate root");
    }

    [Fact]
    public async Task live_host_the_document_is_kept_in_the_run_folder()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(15);
        var run = sandbox.CreateDirectory("run");
        var context = ExtractionTestContext.Create(ExtractionTestContext.SharedScratchRoot, run);

        //Act
        var model = await new ApiExtractor().ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory, "datacrate"), new CrateBuildRequest(), context, TestContext.Current.CancellationToken);

        //Assert
        model.SourcePath.Should().Be(Path.Combine(run, "api", ApiExtractor.DocumentFileName));
        File.Exists(model.SourcePath).Should().BeTrue();
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }
}
