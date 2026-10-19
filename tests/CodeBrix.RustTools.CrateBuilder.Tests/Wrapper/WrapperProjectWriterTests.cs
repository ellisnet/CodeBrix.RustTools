using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

public class WrapperProjectWriterTests
{
    private static string Property(XDocument project, string name)
        => project.Descendants(name).Select(e => e.Value).FirstOrDefault();

    [Fact]
    public async Task the_wrapper_folder_holds_every_generated_file_beside_the_native_output()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        result.ProjectDirectory.Should().Be(bed.DefaultWrapperDirectory);
        result.ProjectPath.Should().Be(Path.Combine(bed.DefaultWrapperDirectory, "Fixture.DataCrate.csproj"));
        var relative = result.GeneratedFiles.Select(f => Path.GetRelativePath(result.ProjectDirectory, f).Replace('\\', '/')).ToList();
        relative.Should().Equal(
            "AGENT-README.txt",
            "Arrays/DoubleArray3.cs",
            "Arrays/DoubleArray3x3.cs",
            "Arrays/SingleArray4.cs",
            "Datacrate.cs",
            "DatacrateException.cs",
            "Decoder.cs",
            "Encoder.cs",
            "Enums/Mode.cs",
            "Fixture.DataCrate.csproj",
            "Mirrors/EncoderConfig.cs",
            "Mirrors/Rect.cs",
            "Mirrors/Settings.cs",
            "Mirrors/Tick.cs",
            "Mirrors/Vec2.cs",
            "Native/AssemblyInfo.cs",
            "Native/DatacrateNative.cs",
            "Overrides/Answer.cs",
            "README.md",
            "THIRD-PARTY-NOTICES.txt",
            "WRAP-REPORT.json",
            "WRAP-REPORT.txt",
            "generated-files.json",
            "licenses/LICENSE-APACHE",
            "licenses/LICENSE-MIT",
            "licenses/workspace-LICENSE-MIT",
            "runtimes/linux-x64/native/libdatacrate.so",
            "runtimes/win-x64/native/datacrate.dll");
        result.GeneratedFiles.Should().OnlyContain(f => File.Exists(f));
        result.WrapReportPath.Should().Be(Path.Combine(result.ProjectDirectory, "WRAP-REPORT.txt"));
        bed.Read(result, "WRAP-REPORT.txt").Should().Be("WRAP REPORT (fixture)\n");
        bed.Read(result, "WRAP-REPORT.json").Should().Be("{ \"fixture\": true }\n");
    }

    [Fact]
    public async Task without_packaging_the_csproj_is_a_plain_class_library_that_copies_the_runtimes()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var project = XDocument.Load(result.ProjectPath);
        project.Root.Attribute("Sdk").Value.Should().Be("Microsoft.NET.Sdk");
        Property(project, "TargetFramework").Should().Be("net10.0");
        Property(project, "AssemblyName").Should().Be("Fixture.DataCrate");
        Property(project, "RootNamespace").Should().Be("Fixture.DataCrate");
        Property(project, "AllowUnsafeBlocks").Should().Be("true");
        Property(project, "Nullable").Should().Be("disable");
        Property(project, "ImplicitUsings").Should().Be("disable");
        Property(project, "GenerateDocumentationFile").Should().Be("true");
        Property(project, "LangVersion").Should().Be("latest");
        Property(project, "IsPackable").Should().Be("false");
        Property(project, "PackageId").Should().BeNull();
        Property(project, "Version").Should().BeNull();
        project.Descendants("PackageReference").Should().BeEmpty();
        var runtimes = project.Descendants("None").Single(e => (string)e.Attribute("Update") == "runtimes/**/*");
        ((string)runtimes.Attribute("Pack")).Should().Be("true");
        ((string)runtimes.Attribute("PackagePath")).Should().Be("%(Identity)");
        ((string)runtimes.Attribute("CopyToOutputDirectory")).Should().Be("PreserveNewest");
        project.Descendants("Compile").Should().BeEmpty();
    }

    [Fact]
    public async Task packaging_makes_the_csproj_packable_with_the_date_stamped_version_and_the_readme()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        bed.Request.Wrapper.Packaging = new WrapperPackaging
        {
            PackageId = "Acme.DataCrate.MitLicense",
            LicenseExpression = "MIT OR Apache-2.0",
            Authors = "Ada & Grace",
            ProjectUrl = "https://example.org/datacrate",
            RepositoryUrl = "https://example.org/datacrate.git",
            Copyright = "Copyright (c) Acme <dev>",
            Tags = { "rust", "ffi", " " },
        };

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var text = File.ReadAllText(result.ProjectPath);
        var project = XDocument.Parse(text);
        Property(project, "IsPackable").Should().Be("true");
        Property(project, "PackageId").Should().Be("Acme.DataCrate.MitLicense");
        Property(project, "PackageLicenseExpression").Should().Be("MIT OR Apache-2.0");
        Property(project, "Authors").Should().Be("Ada & Grace");
        Property(project, "Copyright").Should().Be("Copyright (c) Acme <dev>");
        Property(project, "PackageProjectUrl").Should().Be("https://example.org/datacrate");
        Property(project, "RepositoryUrl").Should().Be("https://example.org/datacrate.git");
        Property(project, "PackageTags").Should().Be("rust;ffi");
        Property(project, "PackageReadmeFile").Should().Be("README.md");
        Property(project, "Description").Should().Contain("Rust crate datacrate 0.15.2");
        Property(project, "BuildVersion").Should().Be("1.$(_VersionYears).$(_VersionDayOfYear).$(_VersionMinuteOfDay)");
        Property(project, "Version").Should().Be("$(BuildVersion)");
        Property(project, "PackageIcon").Should().BeNull();
        text.Should().Contain("Ada &amp; Grace");
        project.Descendants("None").Should().Contain(e => (string)e.Attribute("Update") == "README.md" && (string)e.Attribute("Pack") == "true");
        project.Descendants("None").Should().Contain(e => (string)e.Attribute("Update") == "licenses/*" && (string)e.Attribute("PackagePath") == "");
    }

    [Fact]
    public async Task a_webgpu_linked_crate_references_the_native_package_and_loads_it_first()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        bed.Request.LinkToCodeBrixWebGpu = true;
        bed.Context.WebGpuNativeVersion = "1.0.290.1433";

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var reference = XDocument.Load(result.ProjectPath).Descendants("PackageReference").Single();
        ((string)reference.Attribute("Include")).Should().Be("CodeBrix.WebGpu.Native.MitLicenseForever");
        ((string)reference.Attribute("Version")).Should().Be("1.0.290.1433");
        var native = bed.Read(result, "Native/DatacrateNative.cs");
        native.Should().Contain("using CodeBrix.WebGpu.Native;");
        var ensure = native.IndexOf("CodeBrixWebGpuNative.EnsureLoaded();", StringComparison.Ordinal);
        var resolver = native.IndexOf("NativeLibrary.SetDllImportResolver(", StringComparison.Ordinal);
        ensure.Should().BeLessThan(resolver);
        ensure.Should().BeGreaterThan(native.IndexOf("static DatacrateNative()", StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_crate_that_does_not_link_webgpu_never_mentions_the_engine_in_code()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        bed.Read(result, "Native/DatacrateNative.cs").Should().NotContain("CodeBrixWebGpuNative");
    }

    [Fact]
    public async Task every_export_of_the_plan_has_a_library_import_declaration()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        using var bed = new WrapperTestBed(plan);

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var native = bed.Read(result, "Native/DatacrateNative.cs");
        var declared = Regex.Matches(native, "\\[LibraryImport\\(LibraryName, EntryPoint = \"([a-z0-9_]+)\"\\)\\]").Select(m => m.Groups[1].Value).ToList();
        var expected = plan.Functions.Select(f => f.CName).Concat(plan.Handles.Select(h => h.Destroy.CName)).Append("datacrate_last_error_message").ToList();
        declared.Should().OnlyHaveUniqueItems();
        declared.OrderBy(n => n, StringComparer.Ordinal).Should().Equal(expected.OrderBy(n => n, StringComparer.Ordinal));
        native.Should().Contain("internal static partial int datacrate_checksum(byte* data, nuint data_len, uint* out0);");
        native.Should().Contain("internal static partial int datacrate_divide(int a, int b, int* @out);");
        native.Should().Contain("internal static partial void datacrate_encoder_destroy(nint handle);");
        native.Should().Contain("internal static partial void datacrate_free_buffer(Buffer buffer);");
        native.Should().Contain("internal static partial int datacrate_last_error_message(byte* buffer, nuint capacity, nuint* written);");
        native.Should().Contain("internal static partial int datacrate_invert(DoubleArray3x3 m, DoubleArray3x3* out0);");
        bed.Read(result, "Native/AssemblyInfo.cs").Should().Contain("[assembly: DisableRuntimeMarshalling]");
    }

    [Fact]
    public async Task the_loader_resolves_by_rid_under_the_application_folder_and_names_the_missing_file()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var native = bed.Read(result, "Native/DatacrateNative.cs");
        native.Should().Contain("internal const string LibraryName = \"datacrate\";");
        native.Should().Contain("private static readonly string[] PackagedRuntimeIdentifiers = { \"linux-x64\", \"win-x64\" };");
        native.Should().Contain("\n        \"linux-x64\" => \"libdatacrate.so\",\n");
        native.Should().Contain("\"win-x64\" => \"datacrate.dll\",");
        native.Should().Contain("Path.Combine(AppContext.BaseDirectory, \"runtimes\", rid, \"native\", fileName)");
        native.Should().Contain("throw new DllNotFoundException(");
        native.Should().Contain("This wrapper includes native ");
    }

    [Fact]
    public async Task handles_become_disposable_classes_with_a_safe_handle_that_destroys()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var encoder = bed.Read(result, "Encoder.cs");
        encoder.Should().Contain("public sealed unsafe partial class Encoder : IDisposable");
        encoder.Should().Contain("internal sealed class EncoderSafeHandle : SafeHandleZeroOrMinusOneIsInvalid");
        encoder.Should().Contain("DatacrateNative.datacrate_encoder_destroy(handle);");
        encoder.Should().Contain("public Encoder(EncoderConfig cfg)");
        encoder.Should().Contain("public static unsafe Encoder Default()");
        encoder.Should().Contain("public static unsafe Encoder TryWithFrameSize(uint frameSize)");
        encoder.Should().Contain("public unsafe byte[] Encode(ReadOnlySpan<byte> input)");
        encoder.Should().Contain("public unsafe string Name()");
        encoder.Should().Contain("public unsafe void Absorb(Encoder other)");
        encoder.Should().Contain("public unsafe override string ToString()");
        encoder.Should().Contain("lock (_sync)");
        encoder.Should().Contain("ObjectDisposedException.ThrowIf(_handle.IsClosed, this);");
        encoder.Should().Contain("_consumed = true;");
        encoder.Should().Contain("This call CONSUMES the object");
        encoder.Should().Contain("public static unsafe uint Version()");
        encoder.Should().Contain("public unsafe void Attach(Encoder other)");
        encoder.Should().Contain("var __h2 = other?.AcquireNativeHandle() ?? 0;");
        encoder.Should().Contain("(byte)(other != null ? 1 : 0)");
        bed.Read(result, "Decoder.cs").Should().NotContain("_consumed", "Decoder has no consuming method, so the field would never be assigned");
    }

    [Fact]
    public async Task mirrors_enums_and_arrays_get_their_own_files_with_the_rust_docs()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var vec2 = bed.Read(result, "Mirrors/Vec2.cs");
        vec2.Should().Contain("/// A two-dimensional vector of <c>f64</c> components.");
        vec2.Should().Contain("public readonly partial record struct Vec2");
        vec2.Should().Contain("public double X { get; init; }");
        vec2.Should().Contain("public unsafe double Dot(Vec2 other)");
        vec2.Should().Contain("public static unsafe Vec2 New(double x, double y)");
        bed.Read(result, "Mirrors/Rect.cs").Should().Contain("<c>min</c> &lt;= <c>max</c> on both axes.");
        bed.Read(result, "Mirrors/Tick.cs").Should().Contain("public long Item0 { get; init; }");
        bed.Read(result, "Mirrors/Settings.cs").Should().Contain("enabled = (byte)(Enabled ? 1 : 0),");
        var mode = bed.Read(result, "Enums/Mode.cs");
        mode.Should().Contain("public enum Mode : int");
        mode.Should().Contain("A = 1,");
        mode.Should().Contain("B = 2,");
        bed.Read(result, "Arrays/DoubleArray3x3.cs").Should().Contain("[InlineArray(3)]");
        bed.Read(result, "Arrays/DoubleArray3x3.cs").Should().Contain("private DoubleArray3 _element0;");
        var library = bed.Read(result, "Datacrate.cs");
        library.Should().Contain("public static unsafe (int, int) ParsePair(string s)");
        library.Should().Contain("public static unsafe Mode? ModeFromName(string name)");
        library.Should().Contain("public static unsafe nuint? Find(ReadOnlySpan<int> xs, int x)");
        library.Should().Contain("public static unsafe void Scale(Span<float> xs, float k)");
        library.Should().Contain("public static unsafe uint[] Squares(uint n)");
        library.Should().Contain("public static unsafe Encoder Lookup(string name)");
        library.Should().Contain("var __u0 = Encoding.UTF8.GetBytes(name);");
        library.Should().Contain("public static unsafe double Clamp(double x, double? max, Mode? mode)");
        library.Should().Contain("max.HasValue ? max.Value : default");
        library.Should().Contain("mode.HasValue ? (int)mode.Value : default");
        library.Should().NotContain("Vec2Dot");
    }

    [Fact]
    public async Task a_csharp_override_is_written_verbatim_to_overrides()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        using var bed = new WrapperTestBed(plan);

        //Act
        var result = await bed.WriteAsync();

        //Assert
        bed.Read(result, "Overrides/Answer.cs").Should().Be(plan.Overrides[0].CSharpSource);
    }

    [Fact]
    public async Task smoke_tests_are_written_only_when_asked_and_kept_out_of_the_build()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        using var asked = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        asked.Request.Wrapper.GenerateSmokeTests = true;

        //Act
        var without = await bed.WriteAsync();
        var with = await asked.WriteAsync();

        //Assert
        File.Exists(Path.Combine(without.ProjectDirectory, "Tests", "DatacrateSmokeTests.cs")).Should().BeFalse();
        var smoke = asked.Read(with, "Tests/DatacrateSmokeTests.cs");
        smoke.Should().Contain("\"datacrate_encoder_new\",");
        smoke.Should().Contain("using (var value = Encoder.Default())");
        XDocument.Load(with.ProjectPath).Descendants("Compile").Single().Attribute("Remove").Value.Should().Be("Tests/**");
    }

    [Fact]
    public async Task the_readme_and_agent_readme_show_the_entry_class_in_their_first_example()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        plan.ManagedNamespace = "Acme.Datacrate";
        using var bed = new WrapperTestBed(plan, "Acme.Datacrate");

        //Act
        var result = await bed.WriteAsync();

        //Assert
        result.EntryClassName.Should().Be("DatacrateLibrary");
        var readme = bed.Read(result, "README.md");
        var firstCSharp = readme.Substring(readme.IndexOf("```csharp", StringComparison.Ordinal));
        firstCSharp.Substring(0, firstCSharp.IndexOf("```", 3, StringComparison.Ordinal)).Should().MatchRegex(@"DatacrateLibrary\.\w+\(/\* \.\.\. \*/\);");
        readme.Should().Contain("The entry class is DatacrateLibrary, not Datacrate: the root namespace ends in Datacrate");
        var agent = bed.Read(result, "AGENT-README.txt");
        agent.Should().Contain("    // The crate's free functions are static methods of DatacrateLibrary:");
        agent.Should().Contain("The entry class is DatacrateLibrary, not Datacrate");
        agent.IndexOf("DatacrateLibrary.", StringComparison.Ordinal).Should().BeLessThan(agent.IndexOf("CORE API REFERENCE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task the_readme_lists_the_included_and_the_missing_runtimes_and_the_licences_are_copied()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var readme = bed.Read(result, "README.md");
        readme.Should().Contain("| linux-x64 | runtimes/linux-x64/native/libdatacrate.so |");
        readme.Should().Contain("| win-x64 | runtimes/win-x64/native/datacrate.dll |");
        readme.Should().Contain("- osx-arm64: it was not built (see the build record)");
        readme.Should().Contain("`Decoder`, `Encoder` own native objects");
        readme.Should().Contain("WRAP-REPORT.txt");
        var notices = bed.Read(result, "THIRD-PARTY-NOTICES.txt");
        notices.Should().Contain("Licence declared by the crate (Cargo.toml): MIT OR Apache-2.0");
        notices.Should().Contain("licenses/workspace-LICENSE-MIT");
        bed.Read(result, "licenses/LICENSE-MIT").Should().Be("crate MIT licence text\n");
        bed.Read(result, "licenses/workspace-LICENSE-MIT").Should().Be("workspace MIT licence text\n");
        var agent = bed.Read(result, "AGENT-README.txt");
        agent.Should().Contain("static (int, int) Datacrate.ParsePair(string s)");
        agent.Should().Contain("sealed class Encoder : IDisposable");
        agent.Should().Contain("crate::time::Tick: 254016000000 ticks per second.");
        agent.Should().Contain("crate::for_each: takes a callback");
        agent.Should().NotMatchRegex("\\d+\\.\\d+\\.\\d+", "the generated AGENT-README carries no version numbers");
    }

    [Fact]
    public async Task generated_text_uses_lf_has_no_trailing_whitespace_and_ends_with_one_newline()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        bed.Request.Wrapper.GenerateSmokeTests = true;
        bed.Request.Wrapper.Packaging = new WrapperPackaging { PackageId = "Acme.DataCrate" };

        //Act
        var result = await bed.WriteAsync();

        //Assert
        foreach (var file in result.GeneratedFiles.Where(f => !f.Contains("runtimes") && !f.Contains("licenses") && !f.Contains("WRAP-REPORT")))
        {
            var text = File.ReadAllText(file);
            text.Should().NotContain("\r", file);
            text.Should().EndWith("\n", file);
            text.Should().NotEndWith("\n\n", file);
            text.Split('\n').Should().OnlyContain(line => line.Length == 0 || !char.IsWhiteSpace(line[line.Length - 1]), file);
            if (file.EndsWith(".cs", StringComparison.Ordinal) || file.EndsWith(".csproj", StringComparison.Ordinal))
            {
                text.Should().NotContain("\n\n\n", file);
            }
        }
    }

    [Fact]
    public async Task writing_twice_gives_identical_bytes_and_does_not_touch_unchanged_files()
    {
        //Arrange
        using var first = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        using var second = new WrapperTestBed(WrapperPlanFixtures.DataCrate());

        //Act
        var a = await first.WriteAsync();
        var b = await second.WriteAsync();
        var stamp = File.GetLastWriteTimeUtc(a.ProjectPath);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        await first.WriteAsync();

        //Assert
        var left = WrapperTestBed.Snapshot(a.ProjectDirectory);
        var right = WrapperTestBed.Snapshot(b.ProjectDirectory);
        left.Keys.Should().Equal(right.Keys);
        foreach (var key in left.Keys.Where(k => k != "WRAP-REPORT.txt"))
        {
            left[key].AsSpan().SequenceEqual(right[key]).Should().BeTrue(key);
        }

        File.GetLastWriteTimeUtc(a.ProjectPath).Should().Be(stamp);
    }

    [Fact]
    public async Task a_rewrite_removes_the_files_it_no_longer_generates_and_keeps_the_consumers_own()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        using var bed = new WrapperTestBed(plan);
        var result = await bed.WriteAsync();
        var mine = Path.Combine(result.ProjectDirectory, "MyAdditions.cs");
        File.WriteAllText(mine, "// mine\n");
        plan.Handles.Remove(plan.Handles.Single(h => h.ManagedName == "Decoder"));
        foreach (var function in plan.Functions.Where(f => f.Owner?.ManagedName == "Decoder").ToList())
        {
            plan.Functions.Remove(function);
        }

        plan.Overrides.Clear();

        //Act
        var again = await bed.WriteAsync();

        //Assert
        File.Exists(Path.Combine(result.ProjectDirectory, "Decoder.cs")).Should().BeFalse();
        Directory.Exists(Path.Combine(result.ProjectDirectory, "Overrides")).Should().BeFalse();
        File.Exists(mine).Should().BeTrue();
        again.GeneratedFiles.Should().NotContain(mine);
        File.ReadAllText(Path.Combine(result.ProjectDirectory, "generated-files.json")).Should().NotContain("Decoder.cs");
    }

    [Fact]
    public async Task a_non_empty_folder_not_written_by_crate_builder_is_refused_and_left_alone()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        Directory.CreateDirectory(bed.DefaultWrapperDirectory);
        var foreign = Path.Combine(bed.DefaultWrapperDirectory, "notes.txt");
        File.WriteAllText(foreign, "keep me");
        Func<Task> act = bed.WriteAsync;

        //Act
        var exception = (await act.Should().ThrowAsync<WrapperGenerationException>()).Which;

        //Assert
        exception.Message.Should().Contain("is not empty and was not written by CrateBuilder");
        exception.Stage.Should().Be(CrateBuildStage.Wrapping);
        Directory.GetFiles(bed.DefaultWrapperDirectory).Should().Equal(foreign);
    }

    [Fact]
    public async Task the_wrapper_options_output_directory_wins_over_the_sibling_default()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        var chosen = Path.Combine(bed.Root, "elsewhere", "Wrapper");
        bed.Request.Wrapper.OutputDirectory = chosen;

        //Act
        var result = await bed.WriteAsync();

        //Assert
        result.ProjectDirectory.Should().Be(chosen);
        Directory.Exists(bed.DefaultWrapperDirectory).Should().BeFalse();
    }

    [Theory]
    [InlineData("/a/out/native", "/a/out/My.Lib")]
    [InlineData("/a/out/native/", "/a/out/My.Lib")]
    public void the_default_folder_is_named_after_the_assembly_beside_the_output_folder(string output, string expected)
    {
        //Arrange
        var request = new CrateBuildRequest { OutputDirectory = output, Wrapper = new WrapperOptions { AssemblyName = "My.Lib", RootNamespace = "My.Lib" } };

        //Act
        var directory = WrapperProjectWriter.ResolveOutputDirectory(request);

        //Assert
        directory.Should().Be(Path.GetFullPath(expected));
    }

    [Fact]
    public async Task without_a_mapping_plan_the_writer_fails_with_a_wrapping_exception()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        bed.Facade.Plan = null;
        Func<Task> act = bed.WriteAsync;

        //Act
        var exception = (await act.Should().ThrowAsync<WrapperGenerationException>()).Which;

        //Assert
        exception.Message.Should().Contain("No mapping plan");
        Directory.Exists(bed.DefaultWrapperDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task invalid_wrapper_settings_become_a_wrapping_exception()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.DataCrate());
        bed.Request.Wrapper.RootNamespace = "not a namespace";
        Func<Task> act = bed.WriteAsync;

        //Act
        var exception = (await act.Should().ThrowAsync<WrapperGenerationException>()).Which;

        //Assert
        exception.InnerException.Should().BeOfType<ArgumentException>();
    }

    [Fact]
    public async Task out_parameters_the_plan_lists_must_match_the_result_shape()
    {
        //Arrange
        var plan = WrapperPlanFixtures.DataCrate();
        var divide = plan.Functions.Single(f => f.CName == "datacrate_divide");
        divide.Parameters.Add(WrapperPlanFixtures.P("extra", WrapperPlanFixtures.Prim("i32"), ParameterRole.OutValue));
        using var bed = new WrapperTestBed(plan);
        Func<Task> act = bed.WriteAsync;

        //Act
        var exception = (await act.Should().ThrowAsync<WrapperGenerationException>()).Which;

        //Assert
        exception.Message.Should().Contain("datacrate_divide");
        exception.Message.Should().Contain("out-parameter");
    }

    [Fact]
    public async Task without_a_wrap_report_from_the_facade_stage_a_short_one_is_rendered()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.PassThrough(), "Fixture.Adder", CrateTarget.LinuxX64);
        bed.Facade.WrapReportPath = null;

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var report = bed.Read(result, "WRAP-REPORT.txt");
        report.Should().Contain("static int TestProject13.CbAdd(int a, int b)");
        report.Should().Contain("<- cb_add");
        File.Exists(Path.Combine(result.ProjectDirectory, "WRAP-REPORT.json")).Should().BeFalse();
    }

    [Fact]
    public async Task pass_through_declares_the_crates_exports_one_to_one_without_handles_or_status_codes()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.PassThrough(), "Fixture.Adder", CrateTarget.LinuxX64);

        //Act
        var result = await bed.WriteAsync();

        //Assert
        var native = bed.Read(result, "Native/TestProject13Native.cs");
        native.Should().Contain("internal static partial int cb_add(int a, int b);");
        native.Should().NotContain("enum Status");
        native.Should().NotContain("struct Buffer");
        var library = bed.Read(result, "TestProject13.cs");
        library.Should().Contain("public static int CbAdd(int a, int b)");
        library.Should().Contain("return TestProject13Native.cb_add(a, b);");
        File.Exists(Path.Combine(result.ProjectDirectory, "TestProject13Exception.cs")).Should().BeFalse();
        Directory.Exists(Path.Combine(result.ProjectDirectory, "Mirrors")).Should().BeFalse();
    }

    [Fact]
    public async Task the_writer_reports_progress_in_the_wrapping_stage()
    {
        //Arrange
        using var bed = new WrapperTestBed(WrapperPlanFixtures.PassThrough(), "Fixture.Adder", CrateTarget.LinuxX64);
        var reports = new System.Collections.Generic.List<CrateBuildProgress>();
        var context = new CrateBuilderContext(new CrateBuilderOptions(), Path.Combine(bed.Root, "run"), null, null, reports.Add);

        //Act
        await new WrapperProjectWriter().WriteAsync(bed.Workspace, bed.CompiledTargets, bed.Facade, bed.Request, context, TestContext.Current.CancellationToken);

        //Assert
        reports.Should().NotBeEmpty();
        reports.Should().OnlyContain(r => r.Stage == CrateBuildStage.Wrapping);
    }
}
