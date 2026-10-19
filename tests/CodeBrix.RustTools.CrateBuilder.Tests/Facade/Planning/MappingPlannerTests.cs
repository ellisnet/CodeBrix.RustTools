using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Planning;

public class MappingPlannerTests
{
    // ---- datacrate (rust-samples/test_project_15) ----

    [Theory]
    [InlineData("thiserror::Error", "#[error(\"bad input: {0}\")]", true)]
    [InlineData("Error", "#[error(transparent)]", true)]
    [InlineData("thiserror::Error", "#[serde(rename_all = \"lowercase\")]", false)]
    [InlineData("Debug", "#[error(\"never\")]", false)]
    public void DerivesErrorWithMessages_recognises_an_error_derive_that_writes_display(string derive, string attribute, bool expected)
        => PlannerRun.DerivesErrorWithMessages(new[] { "Debug", derive }, new[] { attribute }).Should().Be(expected);

    /// <summary>The datacrate model plus <c>Holder</c>: a struct that is NOT Copy (Clone,
    /// Debug, Default) with the two public f64 fields of Vec2 and no methods, so it is an
    /// object whose only members are its field accessors and its constructor from the
    /// fields.</summary>
    private static CrateApiModel DataCrateWithHolder()
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(ModelSamples.Text(ModelSamples.DataCrate));
        var structs = root["structs"].AsArray();
        var holder = structs.First(n => (string)n["name"] == "Vec2").DeepClone();
        holder["id"] = "struct:crate::Holder";
        holder["name"] = "Holder";
        holder["canonicalPath"] = "crate::Holder";
        holder["publicPaths"] = new System.Text.Json.Nodes.JsonArray("crate::Holder");
        holder["derives"] = new System.Text.Json.Nodes.JsonArray("Clone", "Debug", "Default");
        holder["implementedTraits"] = new System.Text.Json.Nodes.JsonArray();
        holder["attributes"] = new System.Text.Json.Nodes.JsonArray();
        holder["repr"] = new System.Text.Json.Nodes.JsonArray();
        structs.Add(holder);
        return CrateApiModel.Parse(root.ToJsonString());
    }

    [Fact]
    public void Plan_gives_an_objects_public_fields_getters_setters_and_a_constructor_by_default()
    {
        //Act
        var plan = PlannerFixtures.Plan(DataCrateWithHolder());

        //Assert
        plan.Handles.Select(h => h.RustPath).Should().Contain("Holder");
        plan.Functions.Select(f => f.RustPath).Should().Contain(new[] { "Holder::x", "Holder::set_x", "Holder::y", "Holder::set_y", "Holder::from_fields" });
        plan.FieldAccessorsOmitted.Should().BeNull();
        WrapReportWriter.Render(plan).Should().NotContain("Field accessors:");
        MappingPlanJson.Serialize(plan).Should().NotContain("fieldAccessorsOmitted");
    }

    [Fact]
    public void Plan_without_field_accessors_keeps_the_constructor_from_the_fields_and_counts_what_it_left_out()
    {
        //Arrange
        var request = new CrateBuildRequest { Wrapper = new WrapperOptions { AssemblyName = "Data", RootNamespace = "Data", GenerateFieldAccessors = false } };

        //Act
        var options = PlannerOptions.FromRequest(request);
        var plan = PlannerFixtures.Plan(DataCrateWithHolder(), null, options);

        //Assert
        options.GenerateFieldAccessors.Should().BeFalse();
        plan.Handles.Select(h => h.RustPath).Should().Contain("Holder", "the constructor from the fields still makes it");
        var holder = plan.Functions.Where(f => f.RustPath.StartsWith("Holder::", StringComparison.Ordinal)).Select(f => f.RustPath).ToList();
        holder.Should().Contain("Holder::from_fields");
        holder.Should().NotContain(new[] { "Holder::x", "Holder::set_x", "Holder::y", "Holder::set_y" });
        plan.FieldAccessorsOmitted.Should().Be(4);
        var report = WrapReportWriter.Render(plan);
        report.Should().Contain("Field accessors:   off - 4 getters and setters of objects' public fields not generated, because WrapperOptions.GenerateFieldAccessors is false");
        MappingPlanJson.Serialize(plan).Should().Contain("\"fieldAccessorsOmitted\": 4");
        plan.Functions.Where(f => !f.RustPath.StartsWith("Holder::", StringComparison.Ordinal)).Select(f => f.CName)
            .Should().Equal(PlannerFixtures.Plan(DataCrateWithHolder()).Functions.Where(f => !f.RustPath.StartsWith("Holder::", StringComparison.Ordinal)).Select(f => f.CName),
                "nothing but the field accessors changes");
    }

    [Fact]
    public void A_default_struct_whose_public_fields_cannot_cross_c_is_not_said_to_lack_default()
    {
        //Arrange - Tagged: Clone, Debug, Default; a private Rc<Vec<String>> and a public
        // Arc<String> (the field shapes of hardcrate's Names), no methods.
        var root = System.Text.Json.Nodes.JsonNode.Parse(ModelSamples.Text(ModelSamples.DataCrate));
        var names = System.Text.Json.Nodes.JsonNode.Parse(ModelSamples.Text(ModelSamples.HardCrate))["structs"].AsArray().First(n => (string)n["name"] == "Names");
        var tagged = names.DeepClone();
        tagged["id"] = "struct:crate::Tagged";
        tagged["name"] = "Tagged";
        tagged["canonicalPath"] = "crate::Tagged";
        tagged["module"] = "crate";
        tagged["publicPaths"] = new System.Text.Json.Nodes.JsonArray("crate::Tagged");
        tagged["implementedTraits"] = new System.Text.Json.Nodes.JsonArray("Clone", "Debug", "Default");
        root["structs"].AsArray().Add(tagged);
        var model = CrateApiModel.Parse(root.ToJsonString());

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        plan.Handles.Select(h => h.RustPath).Should().NotContain("Tagged");
        plan.SkippedItem("Tagged").Reason.Should().Be(
            "has no wrappable members: it has no public methods or constructors, it cannot be built from its fields (some are private), and although it has Default, none of its public fields can cross C (see the field entries), so an object made by its Default could not be read or changed, and it cannot be passed to C as a plain value because it is not Copy (it may own memory or other resources). Add a FacadeOverride for it if you need it.");
        plan.SkippedItem("Tagged").Reason.Should().NotContain("has no Default");
    }

    [Fact]
    public void Planner_options_generate_field_accessors_unless_the_wrapper_turns_them_off()
    {
        //Assert
        new PlannerOptions().GenerateFieldAccessors.Should().BeTrue();
        PlannerOptions.FromRequest(new CrateBuildRequest()).GenerateFieldAccessors.Should().BeTrue("a build without a wrapper keeps the default");
        PlannerOptions.FromRequest(new CrateBuildRequest { Wrapper = new WrapperOptions() }).GenerateFieldAccessors.Should().BeTrue();
    }

    [Fact]
    public void Plan_names_the_facade_after_the_cargo_library()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        plan.CrateName.Should().Be("datacrate");
        plan.LibName.Should().Be("datacrate");
        plan.FacadeCrateName.Should().Be("datacrate_facade");
        plan.SymbolPrefix.Should().Be("datacrate_");
        plan.ManagedNamespace.Should().Be("Datacrate");
        plan.ManagedAssemblyName.Should().Be("Datacrate");
        plan.UpstreamIsCdylib.Should().BeFalse();
        plan.LinkToCodeBrixWebGpu.Should().BeFalse();
        plan.Warnings.Should().BeEmpty();
    }

    [Fact]
    public void Plan_uses_the_wrapper_names_and_webgpu_flag_from_the_options()
    {
        //Arrange
        var request = new CrateBuildRequest
        {
            LinkToCodeBrixWebGpu = true,
            Targets = new List<CrateTarget> { CrateTarget.LinuxX64 },
            Wrapper = new WrapperOptions { AssemblyName = "MyCompany.Data", RootNamespace = "MyCompany.Data.Native" },
        };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, null, PlannerOptions.FromRequest(request));

        //Assert
        plan.ManagedNamespace.Should().Be("MyCompany.Data.Native");
        plan.ManagedAssemblyName.Should().Be("MyCompany.Data");
        plan.LinkToCodeBrixWebGpu.Should().BeTrue();
    }

    [Fact]
    public void Plan_mirrors_the_copy_structs_by_value()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        plan.Mirrors.Select(m => m.RustPath).Should().Equal("EncoderConfig", "Rect", "Tick", "Vec2");
        var vec2 = plan.Mirrors.Single(m => m.RustPath == "Vec2");
        vec2.CName.Should().Be("datacrate_vec2");
        vec2.ManagedName.Should().Be("Vec2");
        vec2.Fields.Select(f => f.CName + ":" + f.ManagedName + ":" + f.Type.CType + ":" + f.Type.ManagedType).Should().Equal("x:X:double:double", "y:Y:double:double");
        vec2.ByteSize.Should().Be(16);
        vec2.Alignment.Should().Be(8);
        vec2.Doc.Should().Contain("two-dimensional");

        var rect = plan.Mirrors.Single(m => m.RustPath == "Rect");
        rect.Fields.Select(f => f.ManagedName).Should().Equal("Left", "Top", "Right", "Bottom");
        rect.Fields.Select(f => f.Offset).Should().Equal(0, 8, 16, 24);

        var tick = plan.Mirrors.Single(m => m.RustPath == "Tick");
        tick.IsTupleStruct.Should().BeTrue();
        tick.TupleArity.Should().Be(1);
        tick.Fields.Should().ContainSingle();
        tick.Fields[0].Name.Should().Be("0");
        tick.Fields[0].CName.Should().Be("item0");
        tick.Fields[0].ManagedName.Should().Be("Item0");
        tick.Fields[0].Type.Kind.Should().Be(MappingKind.Primitive);
        tick.Fields[0].Type.CType.Should().Be("int64_t");

        var config = plan.Mirrors.Single(m => m.RustPath == "EncoderConfig");
        config.CName.Should().Be("datacrate_encoder_config");
        config.Fields[0].Type.Kind.Should().Be(MappingKind.Enum);
        config.Fields[0].Type.Enum.Should().BeSameAs(plan.Enums.Single(e => e.RustPath == "Mode"));
        config.Fields[0].Type.CType.Should().Be("int32_t");
        config.Fields[0].Type.ManagedType.Should().Be("Mode");
        config.ByteSize.Should().Be(8);
    }

    [Fact]
    public void Plan_maps_c_like_enums_with_their_discriminants()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        plan.Enums.Select(e => e.RustPath).Should().Equal("Mode", "ParseError");
        var mode = plan.Enums[0];
        mode.CName.Should().Be("datacrate_mode");
        mode.Variants.Select(v => v.RustName + "=" + v.Value + ":" + v.CName + ":" + v.ManagedName).Should().Equal("A=1:DATACRATE_MODE_A:A", "B=2:DATACRATE_MODE_B:B");
        plan.Enums[1].Variants.Select(v => v.Value).Should().Equal(0L, 1L);
    }

    [Fact]
    public void Plan_wraps_the_encoder_as_a_handle_with_constructor_methods_and_destroy()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        var encoder = plan.Handles.Should().ContainSingle().Which;
        encoder.RustPath.Should().Be("Encoder");
        encoder.CName.Should().Be("datacrate_encoder");
        encoder.ManagedName.Should().Be("Encoder");
        encoder.IsCopy.Should().BeFalse();
        encoder.HasDefault.Should().BeFalse();
        encoder.HasClone.Should().BeFalse();
        encoder.Methods.Select(m => m.CName + ":" + m.Kind).Should().Equal(
            "datacrate_encoder_destroy:Destroy", "datacrate_encoder_encode:MutMethod", "datacrate_encoder_frames:Method", "datacrate_encoder_new:Constructor");
        encoder.Methods.Should().OnlyContain(m => m.Owner == encoder && plan.Functions.Contains(m));
        encoder.Destroy.Should().BeSameAs(plan.Function("Encoder::destroy"));
        encoder.Destroy.ReturnsStatus.Should().BeFalse();
        encoder.Destroy.Parameters.Should().ContainSingle().Which.Role.Should().Be(ParameterRole.Handle);

        var create = plan.Function("Encoder::new");
        create.Return.Shape.Should().Be(ReturnShape.Handle);
        create.Roles().Should().Equal(ParameterRole.Value, ParameterRole.OutValue);
        create.Parameters[0].Type.Mirror.RustPath.Should().Be("EncoderConfig");
        create.Parameters[1].Type.CType.Should().Be("datacrate_encoder*");
        create.RustCallExpression.Should().Be("upstream::Encoder::new(cfg)");

        var encode = plan.Function("Encoder::encode");
        encode.Roles().Should().Equal(ParameterRole.Handle, ParameterRole.SliceData, ParameterRole.SliceLength, ParameterRole.OutBuffer);
        encode.Parameters.Select(p => p.CName).Should().Equal("handle", "input_ptr", "input_len", "out");
        encode.Return.Shape.Should().Be(ReturnShape.OwnedBuffer);
        encode.Return.Type.ManagedType.Should().Be("byte[]");
        encode.Return.Type.ManagedMarshalType.Should().Be("Buffer");
        encode.RustCallExpression.Should().Be("upstream::Encoder::encode(this, input)");

        var frames = plan.Function("Encoder::frames");
        frames.Kind.Should().Be(FunctionKind.Method);
        frames.Return.Shape.Should().Be(ReturnShape.Value);
        frames.Return.Type.CType.Should().Be("uint32_t");
    }

    [Fact]
    public void Plan_maps_strings_slices_options_results_tuples_and_arrays()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        var greet = plan.Function("greet");
        greet.CName.Should().Be("datacrate_greet");
        greet.ManagedName.Should().Be("Greet");
        greet.Kind.Should().Be(FunctionKind.Free);
        greet.Owner.Should().BeNull();
        greet.ReturnsStatus.Should().BeTrue();
        greet.Roles().Should().Equal(ParameterRole.StringData, ParameterRole.StringLength, ParameterRole.OutBuffer);
        greet.Parameters[0].Type.Kind.Should().Be(MappingKind.Str);
        greet.Parameters[0].Type.CType.Should().Be("const uint8_t*");
        greet.Parameters[1].Type.CType.Should().Be("size_t");
        greet.Return.Shape.Should().Be(ReturnShape.OwnedString);
        greet.Return.Type.ManagedType.Should().Be("string");

        var checksum = plan.Function("checksum");
        checksum.Roles().Should().Equal(ParameterRole.SliceData, ParameterRole.SliceLength, ParameterRole.OutValue);
        checksum.Parameters[0].Type.Kind.Should().Be(MappingKind.PrimitiveSlice);
        checksum.Parameters[0].Type.ManagedMarshalType.Should().Be("ReadOnlySpan<byte>");
        checksum.Parameters[0].Type.IsMutable.Should().BeFalse();
        checksum.Return.Shape.Should().Be(ReturnShape.Value);

        var parsePair = plan.Function("parse_pair");
        parsePair.Return.Shape.Should().Be(ReturnShape.ResultTuple);
        parsePair.Return.Type.Should().BeNull();
        parsePair.Return.TupleItems.Select(t => t.CType).Should().Equal("int32_t", "int32_t");
        parsePair.Return.ErrorTypeDisplay.Should().Be("ParseError");
        parsePair.Return.ErrorFormat.Should().Be("Display");
        parsePair.Parameters.Select(p => p.CName).Should().Equal("s_ptr", "s_len", "out0", "out1");

        var invert = plan.Function("invert");
        invert.Roles().Should().Equal(ParameterRole.Value, ParameterRole.OutValue);
        var array = invert.Parameters[0].Type;
        array.Kind.Should().Be(MappingKind.FixedArray);
        array.ArrayLengths.Should().Equal(3, 3);
        array.Element.Kind.Should().Be(MappingKind.FixedArray);
        array.Element.ArrayLengths.Should().Equal(3);
        array.Element.Element.CType.Should().Be("double");
        array.ByteSize.Should().Be(72);
        array.CType.Should().Be("datacrate_array_f64_3x3");
        array.ManagedType.Should().Be("F64Array3x3");
        invert.Return.Shape.Should().Be(ReturnShape.Value);

        var find = plan.Function("find");
        find.Return.Shape.Should().Be(ReturnShape.OptionalValue);
        find.Return.Type.CType.Should().Be("size_t");
        find.Roles().Should().Equal(ParameterRole.SliceData, ParameterRole.SliceLength, ParameterRole.Value, ParameterRole.OutPresent, ParameterRole.OutValue);
        find.Parameters[0].Type.ManagedType.Should().Be("ReadOnlySpan<int>");
    }

    [Fact]
    public void Plan_turns_mirror_and_enum_functions_into_free_functions_owned_by_the_type()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        var dot = plan.Function("Vec2::dot");
        dot.Kind.Should().Be(FunctionKind.Free);
        dot.Owner.Should().BeNull();
        dot.OwnerRustPath.Should().Be("Vec2");
        dot.CName.Should().Be("datacrate_vec2_dot");
        dot.ManagedName.Should().Be("Vec2Dot");
        dot.Parameters.Select(p => p.CName + ":" + p.Role).Should().Equal("self_value:Value", "other:Value", "out:OutValue");
        dot.RustCallExpression.Should().Be("upstream::Vec2::dot(&this, other)");

        var union = plan.Function("Rect::union");
        union.RustCallExpression.Should().Be("upstream::Rect::union(&this, &other)");
        plan.Function("Vec2::new").Return.Type.Mirror.RustPath.Should().Be("Vec2");
        plan.Function("Vec2::new").Kind.Should().Be(FunctionKind.Free);

        var fromName = plan.Function("Mode::from_name");
        fromName.OwnerRustPath.Should().Be("Mode");
        fromName.Return.Shape.Should().Be(ReturnShape.OptionalValue);
        fromName.Return.Type.Kind.Should().Be(MappingKind.Enum);
    }

    [Fact]
    public void Plan_adds_one_release_function_per_owned_output_kind()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        var releases = plan.Functions.Where(f => f.Kind == FunctionKind.Release).ToList();
        releases.Select(f => f.CName).Should().Equal("datacrate_free_buffer", "datacrate_free_string");
        releases.Should().OnlyContain(f => !f.ReturnsStatus && f.Return.Shape == ReturnShape.Void && f.Parameters.Count == 1 && f.Parameters[0].Type.CType == "datacrate_buffer");
    }

    [Fact]
    public void Plan_skips_the_wasm_only_function_and_leaves_crate_private_items_out()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);
        var json = MappingPlanJson.Serialize(plan);

        //Assert
        var wasm = plan.SkippedItem("wasm_only");
        wasm.Kind.Should().Be("function");
        wasm.Reason.Should().Contain("only built for target_arch = \"wasm32\"");
        wasm.OverrideHint.Should().Contain("pub unsafe extern \"C\" fn datacrate_wasm_only(");
        json.Should().NotContain("crate_only_helper");
        json.Should().NotContain("private_helper");
        json.Should().NotContain("reset_internal");
        plan.Skipped.Select(s => s.RustPath).Should().Equal("wasm_only");
        var ticks = plan.Constants.Should().ContainSingle().Which;
        ticks.RustPath.Should().Be("TICKS_PER_SECOND");
        ticks.ManagedName.Should().Be("TicksPerSecond");
        ticks.ManagedType.Should().Be("long");
        ticks.ManagedLiteral.Should().Be("254016000000L");
    }

    [Fact]
    public void Plan_lifts_unit_notes_from_the_documentation()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        plan.DocNotes.Should().Contain(n => n.RustPath == "Tick" && n.Text.Contains("254016000000"));
        plan.DocNotes.Should().Contain(n => n.RustPath == "EncoderConfig::frame_size" && n.Text.StartsWith("Bytes per frame"));
        plan.DocNotes.Should().Contain(n => n.RustPath == "invert" && n.Text.Contains("row-major"));
        plan.DocNotes.Select(n => n.RustPath).Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Plan_keeps_one_c_namespace_so_a_function_never_takes_a_type_or_fixed_name()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        PlannerFixtures.AddFreeFunctionCopy(model, "greet", "encoder_config");
        PlannerFixtures.AddFreeFunctionCopy(model, "greet", "last_error_message");

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        plan.Mirrors.Single(m => m.RustTypeName == "EncoderConfig").CName.Should().Be("datacrate_encoder_config");
        plan.Function("encoder_config").CName.Should().Be("datacrate_encoder_config_2");
        plan.Function("last_error_message").CName.Should().Be("datacrate_last_error_message_2");
        var names = plan.Handles.Select(h => h.CName)
            .Concat(plan.Mirrors.Select(m => m.CName))
            .Concat(plan.Enums.Select(e => e.CName))
            .Concat(plan.Enums.SelectMany(e => e.Variants).Select(v => v.CName))
            .Concat(plan.Functions.Select(f => f.CName))
            .ToList();
        names.Should().OnlyHaveUniqueItems();
        names.Should().NotContain("datacrate_last_error_message");
        names.Should().NotContain("datacrate_buffer");
        names.Should().NotContain("DATACRATE_STATUS_OK");
        Action render = () => new CodeBrix.RustTools.CrateBuilder.Facade.Rendering.FacadeSourceBuilder(plan).Build("datacrate", "0.15.2", "{ path = \"../../src\" }", Array.Empty<string>());
        render.Should().NotThrow();
    }

    [Fact]
    public void Plan_lists_every_export_ordinal_by_c_name()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate);

        //Assert
        plan.Functions.Should().HaveCount(21);
        plan.Functions.Select(f => f.CName).Should().BeInAscendingOrder(StringComparer.Ordinal);
        plan.Functions.Select(f => f.CName).Should().OnlyHaveUniqueItems();
        plan.Functions.Where(f => f.Kind is not FunctionKind.Destroy and not FunctionKind.Release).Should().OnlyContain(f => f.ReturnsStatus && f.RustCallExpression.StartsWith("upstream::"));
    }

    // ---- hardcrate (rust-samples/test_project_17) ----

    [Fact]
    public void Plan_of_the_hard_crate_wraps_only_the_two_mappable_functions_and_the_copyable_part_of_names()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.HardCrate);

        //Assert
        // Names is Default + Clone, its borrowed `first` is returned as an owned copy and its
        // `new` takes a list of strings; Shape (data-carrying) is an object with variant
        // constructors because the list functions use it.
        plan.Functions.Select(f => f.RustPath).Should().Equal("add", "free_buffer", "free_string", "Names::clone", "Names::default", "Names::destroy", "Names::first", "Names::new", "scale", "Shape::Circle", "Shape::clone", "Shape::destroy", "Shape::Rect", "Shape::variant", "total_area", "unit_squares");
        plan.Function("add").CName.Should().Be("hardcrate_add");
        plan.Function("add").Roles().Should().Equal(ParameterRole.Value, ParameterRole.Value, ParameterRole.OutValue);
        plan.Function("scale").RustCallExpression.Should().Be("upstream::scale(value, factor)");
        plan.Function("Names::first").RustCallExpression.Should().Be("::std::string::String::from(upstream::Names::first(this))");
        plan.Function("Names::first").Return.Shape.Should().Be(ReturnShape.OwnedString);
        plan.Handles.Select(h => h.RustPath).Should().Equal("Names", "Shape");
        plan.Function("Shape::Rect").RustCallExpression.Should().Be("upstream::Shape::Rect { w: w, h: h }");
        plan.Mirrors.Should().BeEmpty();
        plan.Enums.Should().BeEmpty();
    }

    [Fact]
    public void Plan_of_the_hard_crate_skips_every_hard_item_with_a_reason_and_a_hint()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.HardCrate);

        //Assert
        plan.Skipped.Select(s => s.RustPath).Should().Equal(
            "Names::tag", "apply", "describe", "doubled_later", "each", "evens", "max_of");
        plan.Skipped.Should().OnlyContain(s => s.Reason.Length > 20 && s.OverrideHint.Contains("extern \"C\" fn "));
        plan.SkippedItem("each").Reason.Should().StartWith("takes a callback (impl FnMut(i32))");
        plan.SkippedItem("each").Reason.Should().Contain("Add a FacadeOverride for it if you need it.");
        plan.SkippedItem("apply").Reason.Should().Contain("a callback (dyn Fn(i32) -> i32)");
        plan.SkippedItem("max_of").Reason.Should().Contain("is generic (<T: PartialOrd>)");
        plan.SkippedItem("describe").Reason.Should().Contain("impl std::fmt::Display");
        plan.SkippedItem("evens").Reason.Should().Contain("returns an iterator");
        plan.SkippedItem("doubled_later").Reason.Should().Contain("is async");
        plan.SkippedItem("Names::tag").Kind.Should().Be("field");
        plan.SkippedItem("Names::tag").Reason.Should().Contain("shared ownership (Arc<String>)");
        plan.SkippedItem("doubled_later").Reason.Should().Contain("an executor");
        plan.SkippedItem("each").OverrideHint.Should().Contain("pub unsafe extern \"C\" fn hardcrate_each(");
        plan.SkippedItem("each").OverrideHint.Should().Contain("// Upstream: pub fn each(xs: &[i32], mut f: impl FnMut(i32))");
    }

    // ---- edge cases (pass-through and facade) ----

    [Fact]
    public void Plan_of_a_cdylib_with_c_exports_passes_them_through()
    {
        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.EdgeCases);

        //Assert
        plan.UpstreamIsCdylib.Should().BeTrue();
        plan.LibName.Should().Be("alpha");
        plan.Handles.Should().BeEmpty();
        plan.Mirrors.Should().BeEmpty();
        plan.Enums.Should().BeEmpty();
        var export = plan.Functions.Should().ContainSingle().Which;
        export.Kind.Should().Be(FunctionKind.PassThrough);
        export.CName.Should().Be("c_export");
        export.ManagedName.Should().Be("CExport");
        export.ReturnsStatus.Should().BeFalse();
        export.RustCallExpression.Should().BeNull();
        export.Parameters.Should().ContainSingle().Which.Type.CType.Should().Be("int32_t");
        export.Return.Shape.Should().Be(ReturnShape.Value);
        plan.Warnings.Should().Contain(w => w.Contains("already exports C functions"));
        plan.Warnings.Should().Contain(w => w.Contains("src/broken.rs"));
        plan.Warnings.Should().Contain(w => w.Contains("bitflags"));
    }

    [Fact]
    public void Plan_with_ForceFacade_generates_a_facade_over_a_cdylib_with_c_exports()
    {
        //Arrange
        var options = PlannerOptions.FromRequest(new CrateBuildRequest { FacadeMode = FacadeMode.Always });

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.EdgeCases, options: options);

        //Assert
        options.ForceFacade.Should().BeTrue();
        PlannerOptions.FromRequest(new CrateBuildRequest()).ForceFacade.Should().BeFalse();
        plan.UpstreamIsCdylib.Should().BeFalse();
        plan.Functions.Should().NotContain(f => f.Kind == FunctionKind.PassThrough);
        plan.Handles.Should().NotBeEmpty();
        plan.Warnings.Should().Contain(w => w.StartsWith("FacadeMode.Always:", System.StringComparison.Ordinal));
    }

    [Fact]
    public void Plan_of_the_edge_cases_as_a_plain_library_handles_odd_shapes()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.EdgeCases);
        model.Crate.CrateTypes = new List<string> { "rlib" };

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        plan.UpstreamIsCdylib.Should().BeFalse();
        var dog = plan.Handles.Should().ContainSingle().Which;
        dog.RustPath.Should().Be("Dog");
        dog.HasDefault.Should().BeTrue();
        dog.Methods.Select(m => m.CName).Should().Equal("alpha_dog_default", "alpha_dog_destroy");
        plan.Function("Dog::default").RustCallExpression.Should().Be("<upstream::Dog as ::core::default::Default>::default()");
        plan.Function("d::deeper::in_deeper").Return.Type.Handle.Should().BeSameAs(dog);
        plan.Function("d::deeper::in_deeper").CName.Should().Be("alpha_d_deeper_in_deeper");
        plan.Function("inline::alias_fn").ManagedName.Should().Be("AliasFn");
        plan.Function("inline::alias_fn").RustCallExpression.Should().Be("upstream::inline::alias_fn()");
        plan.SkippedItem("G").Reason.Should().Contain("is generic");
        plan.SkippedItem("G::get").Reason.Should().Contain("belongs to G, a generic type");
        plan.SkippedItem("U").Kind.Should().Be("union");
        plan.SkippedItem("Signed").Reason.Should().Contain("not a plain number");
        plan.SkippedItem("hidden_one").Reason.Should().Contain("#[doc(hidden)]");
        plan.SkippedItem("odd_types").Reason.Should().Contain("associated type");
        plan.SkippedItem("ptrs").Reason.Should().Contain("raw pointer");
        plan.SkippedItem("tuple_pat").Reason.Should().Contain("a tuple");
        plan.SkippedItem("GlobStruct").Reason.Should().Contain("no wrappable members");
        plan.Functions.Select(f => f.RustPath).Should().NotContain("renamed::sub::sub_fn");
    }

    [Fact]
    public void Plan_lists_an_associated_constant_as_not_wrapped_with_its_reason_and_obeys_ExcludeFunctions()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.EdgeCases);
        model.Crate.CrateTypes = new List<string> { "rlib" };
        var excluded = ModelSamples.Load(ModelSamples.EdgeCases);
        excluded.Crate.CrateTypes = new List<string> { "rlib" };

        //Act
        var plan = PlannerFixtures.Plan(model);
        var withExclude = PlannerFixtures.Plan(excluded, new WrapPlan { ExcludeFunctions = new List<string> { "G::*" } });

        //Assert
        var answer = plan.SkippedItem("G::ANSWER");
        answer.Should().NotBeNull();
        answer.Kind.Should().Be("constant");
        answer.Reason.Should().Be("is an associated constant of G (u32 = 42); only free constants (outside an impl block) are copied into C# automatically. Copy the value into your C# code, or add a FacadeOverride that returns it.");
        answer.OverrideHint.Should().Contain("alpha_g_answer");
        plan.Constants.Select(c => c.RustPath).Should().NotContain("G::ANSWER");
        withExclude.SkippedItem("G::ANSWER").Reason.Should().Be("is excluded by your WrapPlan's ExcludeFunctions.");
    }

    [Fact]
    public void Plan_of_a_binary_crate_fails_in_the_planning_stage()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.HardCrate);
        model.Crate.RootKind = RustRootKind.Bin;

        //Act
        var act = () => PlannerFixtures.Plan(model);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Stage.Should().Be(CrateBuildStage.Planning);
    }

    [Fact]
    public void Plan_without_a_model_throws()
    {
        //Act
        var act = () => new MappingPlanner().Plan(null, null, null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    // ---- WrapPlan behaviour ----

    [Fact]
    public void ExcludeFunctions_removes_matching_functions_and_lists_them()
    {
        //Arrange
        var wrap = new WrapPlan { ExcludeFunctions = new List<string> { "Vec2::*", "crate::greet" } };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        plan.Functions.Select(f => f.RustPath).Should().NotContain(new[] { "Vec2::dot", "Vec2::new", "Vec2::length", "Vec2::lerp", "greet" });
        plan.SkippedItem("Vec2::dot").Reason.Should().Contain("ExcludeFunctions");
        plan.SkippedItem("greet").Reason.Should().Contain("ExcludeFunctions");
        plan.Mirrors.Select(m => m.RustPath).Should().Contain("Vec2");
        plan.Functions.Select(f => f.CName).Should().NotContain("datacrate_free_string");
    }

    [Fact]
    public void IncludeTypes_keeps_only_the_selected_types()
    {
        //Arrange
        var wrap = new WrapPlan { IncludeTypes = new List<string> { "Encoder*", "Mode" } };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        plan.Handles.Select(h => h.RustPath).Should().Equal("Encoder");
        plan.Mirrors.Select(m => m.RustPath).Should().Equal("EncoderConfig");
        plan.Enums.Select(e => e.RustPath).Should().Equal("Mode");
        plan.SkippedItem("Vec2").Reason.Should().Contain("IncludeTypes");
        plan.SkippedItem("Rect").Kind.Should().Be("struct");
        plan.Skipped.Select(s => s.RustPath).Should().NotContain("Vec2::dot");
        plan.Function("Encoder::new").Should().NotBeNull();
        plan.Function("checksum").Should().NotBeNull();
        plan.Function("Mode::from_name").Should().NotBeNull();
    }

    [Fact]
    public void IncludeFunctions_drops_a_handle_that_no_selected_function_uses()
    {
        //Arrange
        var wrap = new WrapPlan { IncludeFunctions = new List<string> { "checksum" } };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        plan.Functions.Select(f => f.RustPath).Should().Equal("checksum");
        plan.Handles.Should().BeEmpty();
        plan.SkippedItem("Encoder").Reason.Should().Contain("no wrappable members");
        plan.SkippedItem("greet").Reason.Should().Contain("IncludeFunctions");
    }

    [Fact]
    public void Renames_change_managed_names_only()
    {
        //Arrange
        var wrap = new WrapPlan
        {
            Renames = new Dictionary<string, string>
            {
                ["Vec2"] = "Vector2",
                ["crate::checksum"] = "Sum",
                ["Vec2::x"] = "Horizontal",
                ["Mode::A"] = "Plain",
                ["Encoder::frames"] = "FrameCount",
                ["Nope"] = "Nothing",
                ["greet"] = "1bad",
            },
        };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        var vec = plan.Mirrors.Single(m => m.RustPath == "Vec2");
        vec.ManagedName.Should().Be("Vector2");
        vec.CName.Should().Be("datacrate_vec2");
        vec.Fields.Select(f => f.ManagedName).Should().Equal("Horizontal", "Y");
        plan.Function("Vec2::dot").ManagedName.Should().Be("Vector2Dot");
        plan.Function("Vec2::dot").Parameters[0].Type.ManagedType.Should().Be("Vector2");
        plan.Function("checksum").ManagedName.Should().Be("Sum");
        plan.Function("checksum").CName.Should().Be("datacrate_checksum");
        plan.Function("Encoder::frames").ManagedName.Should().Be("FrameCount");
        plan.Enums.Single(e => e.RustPath == "Mode").Variants[0].ManagedName.Should().Be("Plain");
        plan.Function("greet").ManagedName.Should().Be("Greet");
        plan.Warnings.Should().Contain("The rename of 'Nope' matches no wrapped item.");
        plan.Warnings.Should().Contain(w => w.Contains("'greet'") && w.Contains("plain C# identifier"));
    }

    [Fact]
    public void Overrides_replace_skipped_and_generated_items_and_are_copied_in_order()
    {
        //Arrange
        var wrap = new WrapPlan
        {
            Overrides = new List<FacadeOverride>
            {
                new FacadeOverride { RustPath = "wasm_only", RustSource = "// rust wasm", CSharpSource = "// cs wasm" },
                new FacadeOverride { RustPath = "greet", RustSource = "// rust greet" },
                new FacadeOverride { RustPath = "nothing_here", RustSource = "// none" },
            },
            AdditionalFacadeFiles = new List<string> { "helpers/b.rs", "helpers/a.rs" },
            Notes = "Example plan.",
        };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        plan.Skipped.Select(s => s.RustPath).Should().NotContain("wasm_only");
        plan.Function("greet").Should().BeNull();
        plan.Functions.Select(f => f.CName).Should().NotContain("datacrate_free_string");
        plan.Functions.Select(f => f.CName).Should().Contain("datacrate_free_buffer");
        plan.Overrides.Select(o => o.RustPath).Should().Equal("greet", "nothing_here", "wasm_only");
        plan.Overrides[2].RustSource.Should().Be("// rust wasm");
        plan.Overrides[2].CSharpSource.Should().Be("// cs wasm");
        plan.Overrides[0].CSharpSource.Should().BeNull();
        plan.Warnings.Should().Contain(w => w.Contains("'nothing_here' matches no item"));
        plan.AdditionalFacadeFiles.Should().Equal("helpers/b.rs", "helpers/a.rs");
        plan.WrapPlanNotes.Should().Be("Example plan.");
    }

    [Fact]
    public void An_override_never_removes_a_handles_destroy_function()
    {
        //Arrange
        var wrap = new WrapPlan { Overrides = new List<FacadeOverride> { new FacadeOverride { RustPath = "Encoder::*", RustSource = "// x" } } };

        //Act
        var plan = PlannerFixtures.Plan(ModelSamples.DataCrate, wrap);

        //Assert
        plan.Handles.Single().Destroy.Should().NotBeNull();
        plan.Function("Encoder::destroy").Should().NotBeNull();
    }

    // ---- options ----

    [Fact]
    public void Unsafe_functions_are_skipped_unless_included()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "raw_checksum").IsUnsafe = true;

        //Act
        var skippedPlan = PlannerFixtures.Plan(model);
        var includedPlan = PlannerFixtures.Plan(model, null, new PlannerOptions { IncludeUnsafeFunctions = true });

        //Assert
        skippedPlan.SkippedItem("raw_checksum").Reason.Should().Contain("IncludeUnsafeFunctions");
        includedPlan.Function("raw_checksum").IsUnsafeUpstream.Should().BeTrue();
    }

    [Fact]
    public void Targets_decide_which_cfg_items_are_skipped()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "windows_checksum").Cfg = new List<string> { "windows" };
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "unix_checksum").Cfg = new List<string> { "all(unix, not(target_os = \"macos\"))" };
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "feature_checksum").Cfg = new List<string> { "feature = \"fast\"" };

        //Act
        var linux = PlannerFixtures.Plan(model, null, new PlannerOptions { Targets = new List<CrateTarget> { CrateTarget.LinuxX64, CrateTarget.AndroidArm64 } });
        var all = PlannerFixtures.Plan(model);

        //Assert
        linux.SkippedItem("windows_checksum").Reason.Should().Contain("only built for windows");
        linux.Function("unix_checksum").Should().NotBeNull();
        linux.Function("feature_checksum").Should().NotBeNull();
        all.Function("windows_checksum").Should().NotBeNull();
        all.Function("unix_checksum").Should().NotBeNull();
    }

    [Fact]
    public void A_result_whose_error_has_no_display_uses_debug_and_warns()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        model.FindEnum("ParseError").ImplementedTraits.Remove("Display");

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        plan.Function("parse_pair").Return.ErrorFormat.Should().Be("Debug");
        plan.Warnings.Should().Contain(w => w.StartsWith("parse_pair: the error type ParseError has no Display"));
    }

    // ---- naming ----

    [Fact]
    public void C_names_are_deduplicated_with_numeric_suffixes()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "encoder_new");
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "free_buffer");

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        plan.Function("Encoder::new").CName.Should().Be("datacrate_encoder_new");
        plan.Function("encoder_new").CName.Should().Be("datacrate_encoder_new_2");
        plan.Functions.Single(f => f.Kind == FunctionKind.Free && f.RustPath == "free_buffer").CName.Should().Be("datacrate_free_buffer_2");
        plan.Functions.Single(f => f.Kind == FunctionKind.Release && f.RustPath == "free_buffer").CName.Should().Be("datacrate_free_buffer");
        plan.Functions.Select(f => f.CName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Parameter_names_avoid_keywords_on_both_sides()
    {
        //Arrange
        var model = ModelSamples.Load(ModelSamples.DataCrate);
        PlannerFixtures.AddFreeFunctionCopy(model, "checksum", "keyed").Params[0].Name = "default";
        PlannerFixtures.AddFreeFunctionCopy(model, "find", "lookup").Params[1].Name = "default";

        //Act
        var plan = PlannerFixtures.Plan(model);

        //Assert
        var keyed = plan.Function("keyed");
        keyed.Parameters.Select(p => p.CName).Should().Equal("default_ptr", "default_len", "out");
        keyed.Parameters.Select(p => p.ManagedName).Should().Equal("@default", "defaultLength", "result");
        keyed.RustCallExpression.Should().Be("upstream::keyed(default)");
        var lookup = plan.Function("lookup");
        lookup.Parameters.Select(p => p.CName).Should().Equal("xs_ptr", "xs_len", "default_", "out_present", "out");
        lookup.Parameters[2].ManagedName.Should().Be("@default");
    }

    // ---- determinism ----

    [Theory]
    [MemberData(nameof(Samples))]
    public void Plan_twice_gives_identical_json_and_reports(string sample)
    {
        //Act
        var first = PlannerFixtures.Plan(sample);
        var second = PlannerFixtures.Plan(sample);

        //Assert
        MappingPlanJson.Serialize(second).Should().Be(MappingPlanJson.Serialize(first));
        WrapReportWriter.Render(second).Should().Be(WrapReportWriter.Render(first));
    }

    public static TheoryData<string> Samples => new TheoryData<string>(ModelSamples.All);

    // ---- live, host toolchain ----

    [Theory]
    [InlineData(15, ModelSamples.DataCrate)]
    [InlineData(17, ModelSamples.HardCrate)]
    public async Task live_plan_of_a_fresh_extraction_equals_the_plan_of_the_checked_in_sample(int sample, string document)
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(sample);
        var context = ExtractionTestContext.Create(ExtractionTestContext.SharedScratchRoot, sandbox.CreateDirectory("run"));
        var request = sample == 17 ? new CrateBuildRequest { Features = new List<string> { "extra" } } : new CrateBuildRequest();
        var model = await new ApiExtractor().ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory), request, context, TestContext.Current.CancellationToken);

        //Act
        var live = PlannerFixtures.Plan(model);

        //Assert
        MappingPlanJson.Serialize(live).Should().Be(MappingPlanJson.Serialize(PlannerFixtures.Plan(document)));
    }
}
