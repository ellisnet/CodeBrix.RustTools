using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

/// <summary>
/// The wrapper writer over plans the real <see cref="MappingPlanner"/> makes from the
/// checked-in extractor documents (Facade/Model/samples/; no cargo).
/// </summary>
public class WrapperRealPlanTests
{
    internal static MappingPlan Plan(string sample) => new MappingPlanner().Plan(ModelSamples.Load(sample), null, new PlannerOptions { ManagedNamespace = "Planned.Wrapper", ManagedAssemblyName = "Planned.Wrapper" });

    [Fact]
    public async Task the_planned_datacrate_wrapper_has_the_idiomatic_surface()
    {
        //Arrange
        using var bed = new WrapperTestBed(Plan(ModelSamples.DataCrate), "Planned.Wrapper");

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var encoder = bed.Read(result, "Encoder.cs");
        encoder.Should().Contain("public Encoder(EncoderConfig cfg)");
        encoder.Should().Contain("public unsafe byte[] Encode(ReadOnlySpan<byte> input)");
        encoder.Should().Contain("public unsafe uint Frames()");
        var vec2 = bed.Read(result, "Mirrors/Vec2.cs");
        vec2.Should().Contain("public unsafe double Dot(Vec2 other)");
        vec2.Should().Contain("public static unsafe Vec2 New(double x, double y)");
        bed.Read(result, "Mirrors/Rect.cs").Should().Contain("public unsafe Rect Union(Rect other)");
        var library = bed.Read(result, "Datacrate.cs");
        library.Should().Contain("public static unsafe (int, int) ParsePair(string s)");
        library.Should().Contain("public static unsafe string Greet(string name)");
        library.Should().Contain("public static unsafe F64Array3x3 Invert(F64Array3x3 m)");
        library.Should().Contain("public static unsafe Mode? ModeFromName(string name)");
        library.Should().Contain("public static unsafe nuint? Find(ReadOnlySpan<int> xs, int x)");
        var native = bed.Read(result, "Native/DatacrateNative.cs");
        native.Should().Contain("internal static partial int datacrate_parse_pair(byte* s_ptr, nuint s_len, int* out0, int* out1);");
        native.Should().Contain("internal static partial int datacrate_find(int* xs_ptr, nuint xs_len, int x, byte* out_present, nuint* @out);");
        native.Should().Contain("internal static partial void datacrate_free_string(Buffer buffer);");
        native.Should().Contain("internal static partial int datacrate_last_error_message(");
        native.Should().Contain("var status = datacrate_last_error_message(null, 0, &length);");
        native.Should().Contain("internal static T[] TakeArray<T>(Buffer buffer)");
        native.Should().Contain("var count = checked((int)(buffer.Length / (nuint)sizeof(T)));");
        File.Exists(Path.Combine(result.ProjectDirectory, "Arrays", "F64Array3x3.cs")).Should().BeTrue();
        File.Exists(Path.Combine(result.ProjectDirectory, "Enums", "ParseError.cs")).Should().BeTrue();
    }

    [Fact]
    public async Task the_native_mirror_structs_are_pinned_to_the_planned_c_layout()
    {
        //Arrange
        var plan = Plan(ModelSamples.DataCrate);
        using var bed = new WrapperTestBed(plan, "Planned.Wrapper");

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var native = bed.Read(result, "Native/DatacrateNative.cs");
        foreach (var mirror in plan.Mirrors)
        {
            mirror.ByteSize.Should().BeGreaterThan(0);
            var start = native.IndexOf("internal struct " + mirror.ManagedName + "Native", StringComparison.Ordinal);
            start.Should().BeGreaterThan(0, mirror.ManagedName);
            var header = native.LastIndexOf("[StructLayout(", start, StringComparison.Ordinal);
            native.Substring(header, start - header).Should().Contain("[StructLayout(LayoutKind.Explicit, Size = " + mirror.ByteSize + ")]");
            var body = native.Substring(start, native.IndexOf("    }", start, StringComparison.Ordinal) - start);
            foreach (var field in mirror.Fields)
            {
                body.Should().Contain("[FieldOffset(" + field.Offset + ")]");
            }
        }

        var vec2 = plan.Mirrors.Single(m => m.ManagedName == "Vec2");
        vec2.ByteSize.Should().Be(16);
        vec2.Fields.Select(f => f.Offset).Should().Equal(0, 8);
    }

    [Fact]
    public async Task the_planned_hardcrate_wrapper_has_only_the_mappable_functions_and_no_array_helper()
    {
        //Arrange
        using var bed = new WrapperTestBed(Plan(ModelSamples.HardCrate), "Planned.Wrapper");

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var native = bed.Read(result, "Native/HardcrateNative.cs");
        native.Should().Contain("TakeArray", "unit_squares returns a list of Shape objects");
        native.Should().Contain("TakeString", "Names.First returns a copy of a borrowed string");
        System.Text.RegularExpressions.Regex.Matches(native, "\\[LibraryImport\\(").Count.Should().Be(17, "add, scale, total_area, unit_squares, last_error_message, free_buffer, free_string, Names' default, clone, first, new and destroy, Shape's Circle, Rect, variant, clone and destroy");
        bed.Read(result, "Names.cs").Should().Contain("public unsafe string First()");
        bed.Read(result, "AGENT-README.txt").Should().Contain("NOT WRAPPED");
    }

    [Fact]
    public async Task the_planned_edge_case_crate_is_a_pass_through_wrapper()
    {
        //Arrange
        var plan = Plan(ModelSamples.EdgeCases);
        using var bed = new WrapperTestBed(plan, "Planned.Wrapper");

        //Act
        var result = await bed.WriteAsync();

        //Assert
        plan.UpstreamIsCdylib.Should().BeTrue();
        bed.Read(result, "Native/AlphaNative.cs").Should().Contain("internal static partial int c_export(int a);");
        bed.Read(result, "Alpha.cs").Should().Contain("public static int CExport(int a)");
    }

    [Theory]
    [InlineData(ModelSamples.DataCrate)]
    [InlineData(ModelSamples.HardCrate)]
    [InlineData(ModelSamples.EdgeCases)]
    public async Task a_wrapper_from_a_real_plan_builds_with_warnings_as_errors(string sample)
    {
        //Arrange
        DotnetCli.SkipIfMissing();
        using var bed = new WrapperTestBed(Plan(sample), "Planned.Wrapper");
        bed.Request.Wrapper.GenerateSmokeTests = true;
        var result = await bed.WriteAsync();

        //Act
        var (exitCode, output) = await DotnetCli.RunAsync(result.ProjectDirectory, System.TimeSpan.FromMinutes(5), new[] { "build", "-warnaserror" }.Concat(DotnetCli.BuildFlags()).ToArray());

        //Assert
        exitCode.Should().Be(0, output);
        output.Should().Contain("0 Warning(s)");
    }
}
