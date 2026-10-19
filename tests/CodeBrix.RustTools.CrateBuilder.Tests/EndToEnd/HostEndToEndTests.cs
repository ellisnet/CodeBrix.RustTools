using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;
using CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// End-to-end host-mode builds through the public <see cref="CrateBuilder"/>: a sandbox copy of
/// a sample crate goes in, native files, the build record, the wrap report and a wrapper project
/// come out, and the wrapper is built and called from a console app. Each test skips by name
/// without cargo (and without the dotnet CLI where a wrapper is built).
/// </summary>
public class HostEndToEndTests
{
    private const string BuildScriptLinkTooDeep = "the MSVC linker cannot open the cargo build scripts it links (LNK1104)";

    private static CrateBuildRequest Request(string manifest, string output, params CrateTarget[] targets) => new CrateBuildRequest
    {
        Source = new LocalManifestSource(manifest),
        Mode = BuildMode.Host,
        Targets = targets.ToList(),
        OutputDirectory = output,
    };

    private static void ShouldSucceed(CrateBuildResult result)
        => result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));

    [Fact]
    public async Task an_rlib_crate_becomes_a_native_library_and_a_wrapper_a_console_app_calls()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var rid = CrateTargets.Rid(target);
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, "out", "native"), target);
        request.Wrapper = new WrapperOptions { AssemblyName = "E2E.DataWrapper", RootNamespace = "E2E.DataWrapper" };
        var reported = new List<CrateBuildProgress>();
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(reported.Add), TestContext.Current.CancellationToken);
        ShouldSucceed(result);
        var wrapperBuild = await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build());
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath, DatacrateProgram);

        //Assert
        var native = Path.Combine(request.OutputDirectory, "runtimes", rid, "native", CrateTargets.NativeFileName(target, "datacrate"));
        result.Targets.Single().ArtifactPaths.Should().Equal(native);
        var exports = NativeArtifactReader.Read(native).ExportedSymbols;
        exports.Should().Contain(new[] { "datacrate_last_error_message", "datacrate_greet", "datacrate_encoder_new", "datacrate_encoder_destroy", "datacrate_free_buffer", "datacrate_free_string" });

        File.Exists(result.BuildRecordPath).Should().BeTrue();
        var record = BuildRecord.Load(result.BuildRecordPath);
        record.Source.CrateName.Should().Be("datacrate");
        record.Source.SourceHash.Should().MatchRegex("^[0-9a-f]{64}$");
        record.Targets.Single().Outcome.Should().Be(CrateTargetOutcome.Compiled);
        record.Targets.Single().Artifacts.Single().Path.Should().Be("runtimes/" + rid + "/native/" + Path.GetFileName(native));
        record.Facade.Generated.Should().BeTrue();
        record.Facade.LibName.Should().Be("datacrate");
        record.Facade.PackageName.Should().Be("datacrate_facade");
        record.Facade.Exports.Should().Contain("datacrate_parse_pair");
        record.Facade.ExportCount.Should().Be(record.Facade.Exports.Count);
        record.Facade.Exports.Should().OnlyContain(e => exports.Contains(e));
        record.Wrapper.AssemblyName.Should().Be("E2E.DataWrapper");
        record.ToolchainDescription.Should().StartWith("rustc ");
        record.WrapReportFile.Should().Be("WRAP-REPORT.txt");

        result.WrapReportPath.Should().Be(Path.Combine(request.OutputDirectory, "WRAP-REPORT.txt"));
        var report = File.ReadAllText(result.WrapReportPath);
        report.Should().Contain("Encoder");
        report.Should().Contain("Vec2");
        report.Should().Contain("wasm_only");
        File.Exists(Path.Combine(request.OutputDirectory, "WRAP-REPORT.json")).Should().BeTrue();

        result.Wrapper.ProjectPath.Should().Be(Path.Combine(root, "out", "E2E.DataWrapper", "E2E.DataWrapper.csproj"));
        File.Exists(result.Wrapper.ProjectPath).Should().BeTrue();
        File.Exists(Path.Combine(result.Wrapper.ProjectDirectory, "runtimes", rid, "native", Path.GetFileName(native))).Should().BeTrue();
        File.ReadAllText(Path.Combine(result.Wrapper.ProjectDirectory, "WRAP-REPORT.txt")).Should().Be(report);
        File.ReadAllText(Path.Combine(result.Wrapper.ProjectDirectory, "Native", "DatacrateNative.cs")).Should().Contain("internal const string LibraryName = \"datacrate\";");
        wrapperBuild.Should().Contain("0 Warning(s)");

        EndToEndKit.Lines(output).Should().Equal(
            "dot=3",
            "length=5",
            "encoded=255,0,254",
            "frames=2",
            "greet=Hello, Rust!",
            "pair=3,-4",
            "parse error=-3: missing comma",
            "invert11=0.5",
            "find=2",
            "find none=null",
            "mode=B",
            "checksum=6",
            "disposed=ObjectDisposedException");

        reported.Select(p => p.Stage).Distinct().Should().Equal(
            CrateBuildStage.Readiness, CrateBuildStage.ResolvingSource, CrateBuildStage.Staging, CrateBuildStage.ExtractingApi, CrateBuildStage.Planning,
            CrateBuildStage.RenderingFacade, CrateBuildStage.Building, CrateBuildStage.Verifying, CrateBuildStage.Wrapping, CrateBuildStage.Recording, CrateBuildStage.CleaningUp);
        reported.Should().Contain(p => p.Stage == CrateBuildStage.Building && p.Target == target);
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse("cargo ran in the staged copy, never in the source folder");
        EndToEndKit.Log(nameof(an_rlib_crate_becomes_a_native_library_and_a_wrapper_a_console_app_calls), stopwatch);
    }

    [Fact]
    public async Task every_mapped_shape_crosses_the_generated_facade_and_wrapper_unchanged()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("shapecheck/Cargo.toml", CoverageCrate.CargoToml + "\n");
        sandbox.WriteFile("shapecheck/src/lib.rs", CoverageCrate.LibRs + "\n");
        var request = Request(Path.Combine(sandbox.Directory, "shapecheck", "Cargo.toml"), Path.Combine(sandbox.Directory, "out", "native"), target);
        request.Offline = true;
        request.Wrapper = new WrapperOptions { AssemblyName = "E2E.Shapes", RootNamespace = "E2E.Shapes" };
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        ShouldSucceed(result);
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(sandbox.Directory, "app"), result.Wrapper.ProjectPath, ShapesProgram);

        //Assert
        // The panic test makes Rust's default panic hook print to standard error, which the
        // captured output also holds; only the program's own "name=value" lines are compared.
        EndToEndKit.Lines(output).Where(l => System.Text.RegularExpressions.Regex.IsMatch(l, "^[a-z ]+=")).Should().Equal(
            "get=8",
            "history=3,1,65535",
            "fill=8,8,8",
            "half=4",
            "split=4",
            "merged=12",
            "maybe merged=12",
            "explode=-4: panic: counter exploded at 12",
            "after panic=-4",
            "total=20",
            "after consume=InvalidOperationException",
            "try new=-3: start -1 is negative",
            "negate=False",
            "next=Red",
            "flags=True Blue 10",
            "describe=true Blue 10",
            "scaled=2,4,6,8",
            "unscaled=1,2,3,4",
            "bytes=104,105",
            "sum=6",
            "parse=Green",
            "parse error=unknown colour 'purple'",
            "read=0,1,2",
            "shout=HEY",
            "minmax=-1,5",
            "divmod=3,1",
            "divmod error=division by zero",
            "first even=4",
            "no even=null",
            "noop=done");
        EndToEndKit.Log(nameof(every_mapped_shape_crosses_the_generated_facade_and_wrapper_unchanged), stopwatch);
    }

    /// <summary>The consumer app of the every-shape crate (the rendering tests' coverage crate,
    /// planned by the real planner): object life cycle, panics, consuming methods, Vec of u16
    /// (byte buffers), bool and enum fields of a by-value struct, arrays, options, results,
    /// tuples.</summary>
    private const string ShapesProgram = """
        using System;
        using System.Globalization;
        using System.Linq;
        using E2E.Shapes;

        static string F(float v) => v.ToString(CultureInfo.InvariantCulture);

        var counter = new Counter(5);
        counter.Add(3);
        counter.AddAll(new long[] { 1, -1 });
        Console.WriteLine("get=" + counter.Get());
        Console.WriteLine("history=" + string.Join(",", counter.History()));
        var slots = new long[3];
        counter.Fill(slots);
        Console.WriteLine("fill=" + string.Join(",", slots));
        Console.WriteLine("half=" + counter.Half());
        using (var half = counter.Split())
        {
            Console.WriteLine("split=" + half.Get());
            counter.Merge(half);
        }

        Console.WriteLine("merged=" + counter.Get());
        counter.MaybeMerge(null);
        Console.WriteLine("maybe merged=" + counter.Get());
        var broken = new Counter(12);
        try
        {
            broken.Explode();
        }
        catch (ShapecheckException ex)
        {
            Console.WriteLine("explode=" + ex.Status + ": " + ex.Message);
        }

        try
        {
            broken.Get();
        }
        catch (ShapecheckException ex)
        {
            Console.WriteLine("after panic=" + ex.Status);
        }

        broken.Dispose();
        var consumed = new Counter(20);
        Console.WriteLine("total=" + consumed.IntoTotal());
        try
        {
            consumed.Get();
        }
        catch (InvalidOperationException)
        {
            Console.WriteLine("after consume=InvalidOperationException");
        }

        consumed.Dispose();
        try
        {
            Counter.TryNew(-1);
        }
        catch (ShapecheckException ex)
        {
            Console.WriteLine("try new=" + ex.Status + ": " + ex.Message);
        }

        Console.WriteLine("negate=" + Shapecheck.Negate(true));
        Console.WriteLine("next=" + Shapecheck.NextColor(Color.Blue));
        var flags = Shapecheck.FlagsOf(true, Color.Blue);
        Console.WriteLine("flags=" + flags.On + " " + flags.Color + " " + F(flags.Weights[0] + flags.Weights[1] + flags.Weights[2] + flags.Weights[3]));
        Console.WriteLine("describe=" + Shapecheck.Describe(flags));
        var v = new F32Array4();
        v[0] = 1f;
        v[1] = 2f;
        v[2] = 3f;
        v[3] = 4f;
        var scaled = Shapecheck.Scale(v, 2f);
        Console.WriteLine("scaled=" + F(scaled[0]) + "," + F(scaled[1]) + "," + F(scaled[2]) + "," + F(scaled[3]));
        var unscaled = Shapecheck.Scale(v, null);
        Console.WriteLine("unscaled=" + F(unscaled[0]) + "," + F(unscaled[1]) + "," + F(unscaled[2]) + "," + F(unscaled[3]));
        Console.WriteLine("bytes=" + string.Join(",", Shapecheck.BytesOf("hi")));
        Console.WriteLine("sum=" + Shapecheck.SumVec(new[] { 1, 2, 3 }));
        Console.WriteLine("parse=" + Shapecheck.ParseColor("green"));
        try
        {
            Shapecheck.ParseColor("purple");
        }
        catch (ShapecheckException ex)
        {
            Console.WriteLine("parse error=" + ex.Message);
        }

        Console.WriteLine("read=" + string.Join(",", Shapecheck.ReadBytes(3)));
        Console.WriteLine("shout=" + Shapecheck.Shout("hey"));
        var minMax = Shapecheck.MinMax(new[] { 2.0, -1.0, 5.0 });
        Console.WriteLine("minmax=" + minMax.Item1.ToString(CultureInfo.InvariantCulture) + "," + minMax.Item2.ToString(CultureInfo.InvariantCulture));
        var divMod = Shapecheck.DivMod(7, 2);
        Console.WriteLine("divmod=" + divMod.Item1 + "," + divMod.Item2);
        try
        {
            Shapecheck.DivMod(1, 0);
        }
        catch (ShapecheckException ex)
        {
            Console.WriteLine("divmod error=" + ex.Message);
        }

        Console.WriteLine("first even=" + Shapecheck.FirstEven(new uint[] { 3, 4, 6 }));
        Console.WriteLine("no even=" + (Shapecheck.FirstEven(new uint[] { 1 }).HasValue ? "value" : "null"));
        Shapecheck.Noop();
        Console.WriteLine("noop=done");
        counter.Dispose();

        """;

    [Fact]
    public async Task a_cdylib_with_c_exports_is_passed_through_for_two_linux_targets_and_its_wrapper_packs()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        if (EndToEndKit.HostTargetOrSkip() != CrateTarget.LinuxX64)
        {
            Assert.Skip("This test builds linux-x64 natively and linux-arm64 with the cross gcc; it needs a linux-x64 host.");
        }

        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        sandbox.WriteFile("LICENSE-MIT", "MIT licence of the sandbox copy\n");
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, "out", "native"), CrateTarget.LinuxX64, CrateTarget.LinuxArm64);
        request.Locked = true;
        request.Wrapper = new WrapperOptions
        {
            AssemblyName = "E2E.Adder",
            RootNamespace = "E2E.Adder",
            Packaging = new WrapperPackaging { PackageId = "E2E.Adder.Package", Authors = "CrateBuilder tests", Tags = { "rust" } },
        };
        using var builder = EndToEndKit.NewBuilder();
        var readiness = await builder.CheckReadinessAsync(request, TestContext.Current.CancellationToken);
        if (!readiness.BuildableTargets.Contains(CrateTarget.LinuxArm64))
        {
            Assert.Skip("linux-arm64 is not buildable in host mode here: " + readiness);
        }

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        ShouldSucceed(result);
        await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, EndToEndKit.Build("-c", "Release"));
        var packages = Path.Combine(root, "packages");
        await EndToEndKit.DotnetAsync(result.Wrapper.ProjectDirectory, new[] { "pack", "-c", "Release", "--no-build", "-o", packages }.Concat(DotnetCli.BuildFlags()).ToArray());
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath,
            "using System;\nusing E2E.Adder;\n\nConsole.WriteLine(\"add(2,3)=\" + TestProject13.CbAdd(2, 3));\nConsole.WriteLine(\"add(max,1)=\" + TestProject13.CbAdd(int.MaxValue, 1));\n");

        //Assert
        result.Record.Facade.Generated.Should().BeFalse("the crate already is a cdylib with C exports");
        result.Record.Facade.LibName.Should().Be("test_project_13");
        result.Record.Facade.Exports.Should().Equal("cb_add");
        result.Targets.Select(t => t.Outcome).Should().Equal(CrateTargetOutcome.Compiled, CrateTargetOutcome.Compiled);
        NativeArtifactReader.Read(result.Targets[1].ArtifactPaths.Single()).Architecture.Should().Be("aarch64");
        var package = Directory.GetFiles(packages, "E2E.Adder.Package.*.nupkg").Single();
        using (var zip = ZipFile.OpenRead(package))
        {
            var entries = zip.Entries.Select(e => e.FullName).ToList();
            entries.Should().Contain("runtimes/linux-x64/native/libtest_project_13.so");
            entries.Should().Contain("runtimes/linux-arm64/native/libtest_project_13.so");
            entries.Should().Contain("README.md");
            entries.Should().Contain("lib/net10.0/E2E.Adder.dll");
        }

        EndToEndKit.Lines(output).Should().Equal("add(2,3)=5", "add(max,1)=-2147483648");
        EndToEndKit.Log(nameof(a_cdylib_with_c_exports_is_passed_through_for_two_linux_targets_and_its_wrapper_packs), stopwatch);
    }

    [Fact]
    public async Task a_mostly_unmappable_crate_still_builds_and_its_two_mappable_functions_are_callable()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(17);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, "out", "native"), target);
        request.Wrapper = new WrapperOptions { AssemblyName = "E2E.Hard", RootNamespace = "E2E.Hard" };
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        ShouldSucceed(result);
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath,
            "using System;\nusing System.Globalization;\nusing E2E.Hard;\n\nConsole.WriteLine(\"add=\" + Hardcrate.Add(40, 2));\nConsole.WriteLine(\"scale=\" + Hardcrate.Scale(1.5, 4.0).ToString(CultureInfo.InvariantCulture));\n"
            + "using (var names = new Names(new[] { \"b\", \"c\" })) { Console.WriteLine(\"names=\" + names.First()); }\n"
            + "var squares = Hardcrate.UnitSquares(3);\nConsole.WriteLine(\"squares=\" + squares.Length + \" \" + squares[0].Variant() + \" \" + Hardcrate.TotalArea(squares).ToString(CultureInfo.InvariantCulture));\n"
            + "using (var circle = Shape.Circle(1.0)) { Console.WriteLine(\"circle=\" + circle.Variant()); }\n");

        //Assert
        var report = File.ReadAllText(result.WrapReportPath);
        foreach (var skipped in new[] { "Names::tag", "apply", "describe", "doubled_later", "each", "evens", "max_of" })
        {
            report.Should().Contain(skipped);
        }

        // Names (Default + Clone, `new` from a list of strings) is an object whose borrowed
        // `first` comes back as a copy; Shape (data-carrying) is an object the list functions use.
        result.Record.Facade.Exports.Should().Equal("hardcrate_last_error_message", "hardcrate_add", "hardcrate_free_buffer", "hardcrate_free_string", "hardcrate_names_clone", "hardcrate_names_default", "hardcrate_names_destroy", "hardcrate_names_first", "hardcrate_names_new", "hardcrate_scale", "hardcrate_shape_circle", "hardcrate_shape_clone", "hardcrate_shape_destroy", "hardcrate_shape_rect", "hardcrate_shape_variant", "hardcrate_total_area", "hardcrate_unit_squares");
        EndToEndKit.Lines(output).Should().Equal("add=42", "scale=6", "names=b", "squares=3 Rect 3", "circle=Circle");
        EndToEndKit.Log(nameof(a_mostly_unmappable_crate_still_builds_and_its_two_mappable_functions_are_callable), stopwatch);
    }

    [Fact]
    public async Task a_workspace_member_with_a_path_dependency_outside_the_workspace_builds_offline()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(18);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var outside = Path.Combine(root, "outside");
        Directory.CreateDirectory(outside);
        Directory.Move(Path.Combine(sandbox.Directory, "external", "gamma"), Path.Combine(outside, "gamma"));
        var alpha = Path.Combine(sandbox.Directory, "crates", "alpha", "Cargo.toml");
        File.WriteAllText(alpha, File.ReadAllText(alpha).Replace("../../external/gamma", "../../../outside/gamma"));
        var request = Request(alpha, Path.Combine(root, "out"), target);
        request.Offline = true;
        request.Locked = true;
        var reported = new List<CrateBuildProgress>();
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, new SynchronousProgress<CrateBuildProgress>(reported.Add), TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        result.Source.CrateName.Should().Be("alpha");
        result.Source.CrateVersion.Should().Be("0.18.2");
        result.Record.Facade.Exports.Should().Contain("alpha_transform");
        NativeArtifactReader.Read(result.Targets.Single().ArtifactPaths.Single()).ExportedSymbols.Should().Contain("alpha_transform");
        Path.GetFileName(result.Targets.Single().ArtifactPaths.Single()).Should().Be(CrateTargets.NativeFileName(target, "alpha"));
        reported.Should().Contain(p => p.Stage == CrateBuildStage.Staging && p.Message.Contains("Vendoring the path dependency"));
        result.Wrapper.Should().BeNull();
        EndToEndKit.Log(nameof(a_workspace_member_with_a_path_dependency_outside_the_workspace_builds_offline), stopwatch);
    }

    [Fact]
    public async Task a_webgpu_linked_cdylib_is_verified_recorded_and_called_after_the_engine_is_loaded()
    {
        //Arrange
        Capabilities.SkipOnWindowsPathLimit(BuildScriptLinkTooDeep);
        await Capabilities.SkipIfCargoMissingAsync();
        DotnetCli.SkipIfMissing();
        WebGpuTestPackage.LocateOrSkip();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(16);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, "out", "native"), target);
        request.LinkToCodeBrixWebGpu = true;
        request.Wrapper = new WrapperOptions { AssemblyName = "E2E.Probe", RootNamespace = "E2E.Probe" };
        using var builder = EndToEndKit.NewBuilder();
        var readiness = await builder.CheckReadinessAsync(new CrateBuildRequest { Source = request.Source, Targets = { CrateTarget.AndroidArm64 }, OutputDirectory = request.OutputDirectory }, TestContext.Current.CancellationToken);
        var withAndroid = target != CrateTarget.AndroidArm64 && readiness.BuildableTargets.Contains(CrateTarget.AndroidArm64);
        if (withAndroid)
        {
            request.Targets.Add(CrateTarget.AndroidArm64);
        }

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        ShouldSucceed(result);
        var output = await EndToEndKit.RunConsumerAppAsync(Path.Combine(root, "app"), result.Wrapper.ProjectPath,
            "using System;\nusing E2E.Probe;\n\nConsole.WriteLine(\"api=\" + WgpuLinkProbe.ProbeApiVersion());\nConsole.WriteLine(\"built=\" + WgpuLinkProbe.ProbeBuiltApiVersion());\n");

        //Assert
        result.Record.WebGpuNativeVersion.Should().Be(WebGpuTestPackage.LocateOrSkip().Version);
        foreach (var built in result.Targets)
        {
            built.Outcome.Should().Be(CrateTargetOutcome.Compiled);
            built.VerificationNotes.Should().Contain(n => n.StartsWith("WebGPU link: links libcodebrix_wgpu.so"));
        }

        result.Warnings.Should().NotContain(w => w.Contains("does not reference the engine"));
        var csproj = File.ReadAllText(result.Wrapper.ProjectPath);
        csproj.Should().Contain("<PackageReference Include=\"CodeBrix.WebGpu.Native.MitLicenseForever\" Version=\"" + result.Record.WebGpuNativeVersion + "\" />");
        var loader = File.ReadAllText(Path.Combine(result.Wrapper.ProjectDirectory, "Native", "WgpuLinkProbeNative.cs"));
        loader.IndexOf("CodeBrixWebGpuNative.EnsureLoaded();", StringComparison.Ordinal).Should().BeLessThan(loader.IndexOf("NativeLibrary.SetDllImportResolver", StringComparison.Ordinal));
        EndToEndKit.Lines(output).Should().Equal("api=2", "built=2");
        EndToEndKit.Log(nameof(a_webgpu_linked_cdylib_is_verified_recorded_and_called_after_the_engine_is_loaded), stopwatch, withAndroid ? "with android-arm64" : "host target only");
    }

    [Fact]
    public async Task a_webgpu_linked_facade_that_calls_no_engine_function_passes_with_a_warning()
    {
        //Arrange
        Capabilities.SkipOnWindowsPathLimit(BuildScriptLinkTooDeep);
        await Capabilities.SkipIfCargoMissingAsync();
        WebGpuTestPackage.LocateOrSkip();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, "out"), target);
        request.LinkToCodeBrixWebGpu = true;
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        result.Targets.Single().VerificationNotes.Should().Contain(n => n.StartsWith("WARNING: WebGPU link: ") && n.Contains("does not reference the engine"));
        result.Warnings.Should().Contain(w => w.StartsWith(CrateTargets.Rid(target) + ": WebGPU link: ") && w.Contains("does not reference the engine"));
        result.Record.Warnings.Should().Contain(w => w.Contains("does not reference the engine"));
        NativeArtifactReader.Read(result.Targets.Single().ArtifactPaths.Single()).ExportedSymbols.Should().NotContain(s => s.StartsWith("wgpu", StringComparison.Ordinal));
        EndToEndKit.Log(nameof(a_webgpu_linked_facade_that_calls_no_engine_function_passes_with_a_warning), stopwatch);
    }

    [Fact]
    public async Task readiness_in_host_mode_names_the_buildable_targets_of_this_machine()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(13);
        var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), CrateTargets.All.ToArray());
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var readiness = await builder.CheckReadinessAsync(request, TestContext.Current.CancellationToken);

        //Assert
        readiness.Mode.Should().Be(BuildMode.Host);
        readiness.IsReady.Should().BeTrue(readiness.ToString());
        readiness.BuildableTargets.Should().Contain(target);
        readiness.BuildableTargets.Concat(readiness.UnbuildableTargets).Should().BeEquivalentTo(CrateTargets.All);
        readiness.BuildableTargets.Intersect(readiness.UnbuildableTargets).Should().BeEmpty();
        foreach (var unbuildable in readiness.UnbuildableTargets)
        {
            readiness.Issues.Should().Contain(i => i.Target == unbuildable, CrateTargets.Rid(unbuildable) + " has a reason");
        }

        if (!OperatingSystem.IsMacOS() && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CODEBRIX_CRATEBUILDER_MACOS_SYSROOT")))
        {
            readiness.UnbuildableTargets.Should().Contain(new[] { CrateTarget.OsxArm64, CrateTarget.OsxX64 }, "macOS needs a sysroot on a host that is not a Mac");
        }

        EndToEndKit.Log(nameof(readiness_in_host_mode_names_the_buildable_targets_of_this_machine), stopwatch,
            "buildable " + string.Join(" ", readiness.BuildableTargets.Select(CrateTargets.Rid)) + "; not buildable " + string.Join(" ", readiness.UnbuildableTargets.Select(CrateTargets.Rid)));
    }

    [Fact]
    public async Task a_git_source_whose_manifest_path_leaves_the_repository_is_refused()
    {
        //Arrange
        using var builder = EndToEndKit.NewBuilder();
        var request = new CrateBuildRequest
        {
            Source = new GitRepositorySource("https://github.com/ellisnet/CodeBrix.RustTools", "../Cargo.toml"),
            Targets = { CrateTarget.LinuxX64 },
            OutputDirectory = Path.Combine(EndToEndKit.ScratchRoot, "never-written"),
        };

        //Act
        Func<Task> act = () => builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
        Directory.Exists(request.OutputDirectory).Should().BeFalse();
    }

    [Fact]
    public async Task a_crate_that_does_not_compile_fails_in_the_building_stage_with_a_record()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("broken/Cargo.toml", "[package]\nname = \"broken\"\nversion = \"0.1.0\"\nedition = \"2024\"\nlicense = \"MIT\"\n\n[lib]\ncrate-type = [\"cdylib\"]\n\n[dependencies]\n");
        sandbox.WriteFile("broken/src/lib.rs", "#[unsafe(no_mangle)]\npub extern \"C\" fn broken_value() -> i32 {\n    \"not a number\"\n}\n");
        var request = Request(Path.Combine(sandbox.Directory, "broken", "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), target);
        request.Offline = true;
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.FailedStage.Should().Be(CrateBuildStage.Building);
        result.FailureMessage.Should().Contain("No target compiled");
        result.Targets.Single().Outcome.Should().Be(CrateTargetOutcome.Failed);
        result.Targets.Single().Message.Should().Contain("mismatched types");
        File.Exists(result.BuildRecordPath).Should().BeTrue();
        var record = BuildRecord.Load(result.BuildRecordPath);
        record.Targets.Single().Outcome.Should().Be(CrateTargetOutcome.Failed);
        record.Warnings.Should().Contain(w => w.StartsWith("Failed at Building: "));
        record.Facade.Should().BeNull("a cdylib without a wrapper is built as it is");
        EndToEndKit.Log(nameof(a_crate_that_does_not_compile_fails_in_the_building_stage_with_a_record), stopwatch);
    }

    [Fact]
    public async Task a_cdylib_that_exports_nothing_is_routed_through_the_facade_stages_in_the_same_run()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("plain/Cargo.toml", "[package]\nname = \"plaincdylib\"\nversion = \"0.1.0\"\nedition = \"2024\"\nlicense = \"MIT\"\n\n[lib]\ncrate-type = [\"cdylib\", \"rlib\"]\n\n[dependencies]\n");
        sandbox.WriteFile("plain/src/lib.rs", "pub fn add(a: i32, b: i32) -> i32 {\n    a + b\n}\n");
        var request = Request(Path.Combine(sandbox.Directory, "plain", "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), target);
        var never = request.Clone();
        never.FacadeMode = FacadeMode.Never;
        never.OutputDirectory = Path.Combine(sandbox.Directory, "out-never");
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);
        var kept = await builder.BuildAsync(never, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        result.Warnings.Should().Contain(w => w.Contains("routed through the facade stages", StringComparison.Ordinal));
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.Facade.Reason.Should().Be(CrateBuildPipeline.AutoFallbackReason);
        result.Record.Facade.Exports.Should().Contain("plaincdylib_add");
        NativeArtifactReader.Read(result.Targets.Single().ArtifactPaths.Single()).ExportedSymbols.Should().Contain("plaincdylib_add");
        kept.Success.Should().BeFalse();
        kept.FailureMessage.Should().Contain(DefaultArtifactVerifier.NoExportsNote);
        kept.Record.Facade.Should().BeNull();
        EndToEndKit.Log(nameof(a_cdylib_that_exports_nothing_is_routed_through_the_facade_stages_in_the_same_run), stopwatch);
    }

    /// <summary>The consumer app of the datacrate wrapper (sample 15): every shape once.</summary>
    [Fact]
    public async Task a_shared_cargo_target_directory_is_used_by_every_cargo_run_and_reused_by_the_next_build()
    {
        //Arrange
        Capabilities.SkipOnWindowsPathLimit(BuildScriptLinkTooDeep);
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var triple = CrateTargets.Triple(target);
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.FromSample(15);
        var root = Path.GetDirectoryName(sandbox.Directory);
        var shared = Path.Combine(root, "shared-target");
        var hostFolder = Path.Combine(shared, "host");
        var extractorFolder = Path.Combine(shared, "api-extractor", ApiExtractorToolSource.ContentHash.Substring(0, ApiExtractorToolSource.FolderHashLength));
        var built = Path.Combine(hostFolder, triple, "release", CrateTargets.NativeFileName(target, "datacrate"));
        var tool = Path.Combine(extractorFolder, "release", OperatingSystem.IsWindows() ? ApiExtractorToolSource.ToolName + ".exe" : ApiExtractorToolSource.ToolName);
        CrateBuildRequest RequestFor(string output)
        {
            var request = Request(Path.Combine(sandbox.Directory, "Cargo.toml"), Path.Combine(root, output, "native"), target);
            request.Wrapper = new WrapperOptions { AssemblyName = "E2E.SharedTarget", RootNamespace = "E2E.SharedTarget" };
            return request;
        }

        using var builder = new CrateBuilder(new CrateBuilderOptions { ScratchRootDirectory = EndToEndKit.ScratchRoot, CargoTargetDirectory = shared });

        //Act
        var first = await builder.BuildAsync(RequestFor("first"), cancellationToken: TestContext.Current.CancellationToken);
        ShouldSucceed(first);
        var builtAfterFirst = File.Exists(built);
        var toolAfterFirst = File.Exists(tool) ? File.GetLastWriteTimeUtc(tool) : DateTime.MinValue;
        var second = await builder.BuildAsync(RequestFor("second"), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        builtAfterFirst.Should().BeTrue("the first build's cargo output is in the shared folder: " + built);
        toolAfterFirst.Should().NotBe(DateTime.MinValue, "the API extractor tool was built into " + extractorFolder);
        ShouldSucceed(second);
        File.Exists(built).Should().BeTrue();
        File.Exists(Path.Combine(hostFolder, "CACHEDIR.TAG")).Should().BeTrue("cargo marks the folder as its target folder");
        File.GetLastWriteTimeUtc(tool).Should().Be(toolAfterFirst, "the second build reuses the tool built in the shared folder");
        foreach (var result in new[] { first, second })
        {
            var files = result.Targets.Single().CargoResult.Artifacts.SelectMany(a => a.Filenames).ToList();
            files.Should().NotBeEmpty();
            files.Should().OnlyContain(f => Path.GetFullPath(f).StartsWith(hostFolder + Path.DirectorySeparatorChar, StringComparison.Ordinal), "every cargo output of the build lands in the shared host/ folder");
            result.Source.CloneReused.Should().BeFalse("a local source is never a reused clone");
            File.ReadAllText(result.BuildRecordPath).Should().Contain("\"cloneReused\": false");
        }

        File.Exists(Path.Combine(root, "second", "native", "runtimes", CrateTargets.Rid(target), "native", CrateTargets.NativeFileName(target, "datacrate"))).Should().BeTrue();
        EndToEndKit.Log(nameof(a_shared_cargo_target_directory_is_used_by_every_cargo_run_and_reused_by_the_next_build), stopwatch);
    }

    private const string DatacrateProgram = """
        using System;
        using System.Globalization;
        using E2E.DataWrapper;

        var a = new Vec2(3.0, 4.0);
        Console.WriteLine("dot=" + a.Dot(new Vec2(1.0, 0.0)).ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("length=" + a.Length().ToString(CultureInfo.InvariantCulture));
        var encoder = new Encoder(new EncoderConfig(Mode.B, 2));
        using (encoder)
        {
            Console.WriteLine("encoded=" + string.Join(",", encoder.Encode(new byte[] { 0, 255, 1 })));
            Console.WriteLine("frames=" + encoder.Frames());
        }

        Console.WriteLine("greet=" + Datacrate.Greet("Rust"));
        var pair = Datacrate.ParsePair("3,-4");
        Console.WriteLine("pair=" + pair.Item1 + "," + pair.Item2);
        try
        {
            Datacrate.ParsePair("3");
            Console.WriteLine("parse error=none");
        }
        catch (DatacrateException ex)
        {
            Console.WriteLine("parse error=" + ex.Status + ": " + ex.Message);
        }

        var m = new F64Array3x3();
        m[0][0] = 2.0;
        m[1][1] = 2.0;
        m[2][2] = 2.0;
        var inverse = Datacrate.Invert(m);
        Console.WriteLine("invert11=" + inverse[1][1].ToString(CultureInfo.InvariantCulture));
        Console.WriteLine("find=" + Datacrate.Find(new[] { 5, 6, 7 }, 7));
        Console.WriteLine("find none=" + (Datacrate.Find(new[] { 5 }, 9).HasValue ? "value" : "null"));
        Console.WriteLine("mode=" + Datacrate.ModeFromName("b"));
        Console.WriteLine("checksum=" + Datacrate.Checksum(new byte[] { 1, 2, 3 }));
        try
        {
            encoder.Frames();
            Console.WriteLine("disposed=no exception");
        }
        catch (ObjectDisposedException)
        {
            Console.WriteLine("disposed=ObjectDisposedException");
        }

        """;
}
