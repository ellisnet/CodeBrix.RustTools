using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Rendering;
using CodeBrix.RustTools.CrateBuilder.Sources;
using CodeBrix.RustTools.CrateBuilder.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.Tests.Staging;
using CodeBrix.RustTools.CrateBuilder.Tests.WebGpu;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering;

/// <summary>
/// Live: stage a crate with the WorkspaceStager, render its facade, build it with RustWorkbench
/// (release, offline), check the exports with NativeArtifactReader, load it and call it. Skips
/// by name without cargo.
/// </summary>
public class FacadeRendererLiveTests
{
    private sealed class LiveBuild
    {
        public FacadeRenderResult Render { get; init; }

        public string ArtifactPath { get; init; }

        public NativeArtifactInfo Info { get; init; }
    }

    private static string HostFileName(string lib)
        => OperatingSystem.IsWindows() ? lib + ".dll" : OperatingSystem.IsMacOS() ? "lib" + lib + ".dylib" : "lib" + lib + ".so";

    private static async Task<LiveBuild> StageRenderBuildAsync(SampleSandbox sandbox, string manifestPath, MappingPlan plan, FacadeRenderOptions options = null, CrateTarget? webGpuTarget = null)
    {
        // The run folder sits beside the crate folder, never inside the tree that is staged.
        var context = StagingContexts.Create(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)), "run"));
        var resolved = await new SourceResolver().ResolveAsync(new LocalManifestSource(manifestPath), context, TestContext.Current.CancellationToken);
        var staged = await new WorkspaceStager().StageAsync(resolved, new CrateBuildRequest(), context, TestContext.Current.CancellationToken);
        var render = new FacadeRenderer().Render(plan, staged, options);
        var workbench = new RustWorkbench();
        var project = workbench.OpenProject(render.ManifestPath);
        var cargo = new CargoBuildOptions { Profile = CargoProfile.Release, Offline = true };
        if (webGpuTarget != null)
        {
            var contribution = await new WebGpuLinkStager().PrepareAsync(staged, webGpuTarget.Value, BuildMode.Host, null, TestContext.Current.CancellationToken);
            string Resolve(string value) => value.Replace(WebGpuLinkRecipe.WorkspaceToken, staged.Directory.Replace('\\', '/'), StringComparison.Ordinal);
            foreach (var pair in contribution.Environment)
            {
                cargo.Environment[pair.Key] = Resolve(pair.Value);
            }

            if (contribution.RustFlags.Count > 0)
            {
                cargo.Environment["RUSTFLAGS"] = string.Join(" ", contribution.RustFlags.Select(Resolve));
            }
        }

        var result = await workbench.BuildAsync(project, cargo, null, TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue("cargo failed: " + string.Join(Environment.NewLine, result.Errors.Select(e => e.Rendered ?? e.Message)) + Environment.NewLine + result.StandardError);
        result.Warnings.Should().BeEmpty("the generated facade builds without warnings: " + string.Join(Environment.NewLine, result.Warnings.Select(w => w.Rendered ?? w.Message)));
        var artifact = result.Artifacts
            .Where(a => a.Kinds.Contains("cdylib"))
            .SelectMany(a => a.Filenames)
            .Single(f => string.Equals(Path.GetFileName(f), HostFileName(plan.LibName), StringComparison.Ordinal));
        artifact.Should().StartWith(render.Directory);
        return new LiveBuild { Render = render, ArtifactPath = artifact, Info = NativeArtifactReader.Read(artifact) };
    }

    // ------------------------------------------------------------------ datacrate (sample 15), hand-built plan

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Vec2DotFn(Vec2 self, Vec2 other, out double result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Vec2DotRawFn(Vec2 self, Vec2 other, IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Vec2NewFn(double x, double y, out Vec2 result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TickSecondsFn(Tick self, out double result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncoderNewFn(EncoderConfig cfg, out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EncoderEncodeFn(IntPtr handle, byte[] input, nuint length, out FacadeBuffer result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleU32Fn(IntPtr handle, out uint result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DestroyFn(IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TextToBufferFn(byte[] text, nuint length, out FacadeBuffer result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ParsePairFn(byte[] text, nuint length, out int a, out int b);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InvertFn(Matrix3 m, out Matrix3 result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FindFn(int[] xs, nuint length, int x, out byte present, out nuint index);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ModeFromNameFn(byte[] name, nuint length, out byte present, out int mode);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ChecksumFn(byte[] data, nuint length, out uint result);

    [StructLayout(LayoutKind.Sequential)]
    private struct Vec2
    {
        public double X;
        public double Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Tick
    {
        public long Item0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EncoderConfig
    {
        public int Mode;
        public uint FrameSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Matrix3
    {
        public double M00, M01, M02, M10, M11, M12, M20, M21, M22;
    }

    [Fact]
    public async Task The_hand_built_datacrate_facade_builds_exports_every_planned_symbol_and_works_when_called()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(15);
        var plan = DatacratePlan.Create();
        var build = await StageRenderBuildAsync(sandbox, Path.Combine(sandbox.Directory, "Cargo.toml"), plan);

        //Act
        using var lib = new NativeFacade(build.ArtifactPath, plan.SymbolPrefix);
        var dotStatus = lib.Fn<Vec2DotFn>("vec2_dot")(new Vec2 { X = 3, Y = 4 }, new Vec2 { X = 2, Y = -1 }, out var dot);
        var nullOutStatus = lib.Fn<Vec2DotRawFn>("vec2_dot")(default, default, IntPtr.Zero);
        var nullOutMessage = lib.LastError();
        lib.Fn<Vec2NewFn>("vec2_new")(1.5, -2.5, out var made);
        lib.Fn<TickSecondsFn>("tick_seconds")(new Tick { Item0 = 381_024_000_000 }, out var seconds);

        var newStatus = lib.Fn<EncoderNewFn>("encoder_new")(new EncoderConfig { Mode = 2, FrameSize = 2 }, out var encoder);
        var encodeStatus = lib.Fn<EncoderEncodeFn>("encoder_encode")(encoder, new byte[] { 0, 255, 1 }, 3, out var encoded);
        var encodedBytes = lib.TakeBytes(encoded);
        lib.Fn<HandleU32Fn>("encoder_frames")(encoder, out var frames);
        lib.Fn<DestroyFn>("encoder_destroy")(encoder);
        lib.Fn<DestroyFn>("encoder_destroy")(IntPtr.Zero);
        var badModeStatus = lib.Fn<EncoderNewFn>("encoder_new")(new EncoderConfig { Mode = 5, FrameSize = 1 }, out var notCreated);
        var badModeMessage = lib.LastError();

        var name = NativeFacade.Utf8("Ada");
        var greetStatus = lib.Fn<TextToBufferFn>("greet")(name, (nuint)name.Length, out var greeting);
        var greetingText = lib.TakeString(greeting);
        var badUtf8Status = lib.Fn<TextToBufferFn>("greet")(new byte[] { 0xff, 0xfe }, 2, out _);
        var badUtf8Message = lib.LastError();

        var pairOk = lib.Fn<ParsePairFn>("parse_pair")(NativeFacade.Utf8("3,-4"), 4, out var a, out var b);
        var pairStatus = lib.Fn<ParsePairFn>("parse_pair")(NativeFacade.Utf8("x"), 1, out _, out _);
        var pairMessage = lib.LastError();
        var afterSuccess = lib.Fn<ParsePairFn>("parse_pair")(NativeFacade.Utf8("1,2"), 3, out _, out _);
        var clearedMessage = lib.LastError();

        lib.Fn<InvertFn>("invert")(new Matrix3 { M00 = 2, M11 = 2, M22 = 2 }, out var inverse);
        lib.Fn<FindFn>("find")(new[] { 5, 6, 7 }, 3, 7, out var found, out var index);
        lib.Fn<FindFn>("find")(new[] { 5 }, 1, 9, out var notFound, out _);
        var nullSliceStatus = lib.Fn<FindFn>("find")(null, 2, 1, out _, out _);
        var emptySliceStatus = lib.Fn<FindFn>("find")(null, 0, 1, out var emptyFound, out _);
        lib.Fn<ModeFromNameFn>("mode_from_name")(NativeFacade.Utf8("B"), 1, out var modePresent, out var mode);
        lib.Fn<ChecksumFn>("checksum")(new byte[] { 1, 2, 250 }, 3, out var sum);

        //Assert
        build.Info.ExportedSymbols.Should().Contain(DatacratePlan.PlannedSymbols(plan));
        build.Render.Exports.Should().Equal(DatacratePlan.PlannedSymbols(plan));
        dotStatus.Should().Be(0);
        dot.Should().Be(2.0);
        nullOutStatus.Should().Be(FacadeConventions.StatusNullArgument);
        nullOutMessage.Should().Be("argument 'out' is null");
        made.X.Should().Be(1.5);
        made.Y.Should().Be(-2.5);
        seconds.Should().Be(1.5);
        newStatus.Should().Be(0);
        encodeStatus.Should().Be(0);
        encodedBytes.Should().Equal(255, 0, 254);
        frames.Should().Be(2u);
        badModeStatus.Should().Be(FacadeConventions.StatusInvalidArgument);
        badModeMessage.Should().Contain("argument 'cfg'");
        notCreated.Should().Be(IntPtr.Zero);
        greetStatus.Should().Be(0);
        greetingText.Should().Be("Hello, Ada!");
        badUtf8Status.Should().Be(FacadeConventions.StatusInvalidArgument);
        badUtf8Message.Should().Contain("is not valid UTF-8");
        pairOk.Should().Be(0);
        a.Should().Be(3);
        b.Should().Be(-4);
        pairStatus.Should().Be(FacadeConventions.StatusUpstreamError);
        pairMessage.Should().Be("missing comma");
        afterSuccess.Should().Be(0);
        clearedMessage.Should().BeEmpty();
        inverse.M00.Should().Be(0.5);
        inverse.M11.Should().Be(0.5);
        inverse.M01.Should().Be(0.0);
        found.Should().Be((byte)1);
        index.Should().Be((nuint)2);
        notFound.Should().Be((byte)0);
        nullSliceStatus.Should().Be(FacadeConventions.StatusNullArgument);
        emptySliceStatus.Should().Be(0);
        emptyFound.Should().Be((byte)0);
        modePresent.Should().Be((byte)1);
        mode.Should().Be(2);
        sum.Should().Be(253u);
    }

    [Fact]
    public async Task The_planner_plan_of_the_datacrate_sample_builds_and_exports_every_planned_symbol()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.FromSample(15);
        var api = ModelSamples.Load(ModelSamples.DataCrate);
        var plan = new MappingPlanner().Plan(api, null, null);

        //Act
        var build = await StageRenderBuildAsync(sandbox, Path.Combine(sandbox.Directory, "Cargo.toml"), plan, FacadeRenderOptions.FromRequest(new CrateBuildRequest(), api));
        using var lib = new NativeFacade(build.ArtifactPath, plan.SymbolPrefix);
        var status = lib.Fn<ParsePairFn>("parse_pair")(NativeFacade.Utf8("x"), 1, out _, out _);
        var message = lib.LastError();

        //Assert
        build.Info.ExportedSymbols.Should().Contain(build.Render.Exports);
        build.Render.Exports.Should().Equal(new[] { plan.SymbolPrefix + "last_error_message" }.Concat(plan.Functions.Select(f => f.CName)));
        status.Should().Be(FacadeConventions.StatusUpstreamError);
        message.Should().Be("missing comma");
    }

    [Fact]
    public async Task A_webgpu_linked_facade_builds_with_the_link_stager_and_bundles_no_engine()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        WebGpuTestPackage.LocateOrSkip();
        var target = WebGpuTestPackage.HostTarget();
        if (target == null)
        {
            Assert.Skip("This host is not one of the runtime identifiers CrateBuilder builds for.");
        }

        using var sandbox = SampleSandbox.FromSample(15);
        var plan = DatacratePlan.Create();
        plan.LinkToCodeBrixWebGpu = true;

        //Act
        var build = await StageRenderBuildAsync(sandbox, Path.Combine(sandbox.Directory, "Cargo.toml"), plan, null, target);

        //Assert
        build.Render.GeneratedFiles.Should().Contain("build.rs");
        build.Info.ExportedSymbols.Should().Contain(DatacratePlan.PlannedSymbols(plan));
        build.Info.ExportedSymbols.Should().NotContain(e => e.StartsWith("wgpu", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ the coverage crate (every shape)

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int I64ToHandleFn(long start, out IntPtr handle);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleI64OutFn(IntPtr handle, out long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleI64InFn(IntPtr handle, long by);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleSliceFn(IntPtr handle, long[] values, nuint length);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleBufferFn(IntPtr handle, out FacadeBuffer result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleHandleOutFn(IntPtr handle, out IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleHandleFn(IntPtr handle, IntPtr other);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int HandleMaybeHandleFn(IntPtr handle, byte hasOther, IntPtr other);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ByteToByteFn(byte value, out byte result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int IntToIntFn(int value, out int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FlagsOfFn(byte on, int color, out Flags result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DescribeFn(Flags flags, out FacadeBuffer result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ScaleFn(F32x4 v, byte hasK, float k, out F32x4 result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SumVecFn(int[] values, nuint length, out long result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int TextToIntFn(byte[] text, nuint length, out int result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int ByteStatusFn(byte value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SizeToBufferFn(nuint n, out FacadeBuffer result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int MinMaxFn(double[] xs, nuint length, out double min, out double max);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int DivModFn(int a, int b, out int quotient, out int remainder);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FirstEvenFn(uint[] xs, nuint length, out byte present, out uint value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int NoArgumentsFn();

    [StructLayout(LayoutKind.Sequential)]
    private struct F32x4
    {
        public float A, B, C, D;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Flags
    {
        public byte On;
        public int Color;
        public F32x4 Weights;
    }

    [Fact]
    public async Task The_coverage_facade_builds_and_every_shape_behaves_by_the_c_conventions()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        using var sandbox = SampleSandbox.CreateEmpty();
        var manifest = sandbox.WriteFile("shapecheck/Cargo.toml", CoverageCrate.CargoToml + "\n");
        sandbox.WriteFile("shapecheck/src/lib.rs", CoverageCrate.LibRs + "\n");
        var helpers = sandbox.WriteFile("mine/helpers.rs", CoverageCrate.HelpersRs + "\n");
        var plan = CoverageCrate.Create();
        plan.AdditionalFacadeFiles.Add(helpers);
        var build = await StageRenderBuildAsync(sandbox, manifest, plan);
        using var lib = new NativeFacade(build.ArtifactPath, plan.SymbolPrefix);
        var results = new Dictionary<string, object>();

        //Act
        lib.Fn<I64ToHandleFn>("counter_new")(4, out var counter);
        results["add"] = lib.Fn<HandleI64InFn>("counter_add")(counter, 3);
        results["add_all"] = lib.Fn<HandleSliceFn>("counter_add_all")(counter, new long[] { 1, 2 }, 2);
        lib.Fn<HandleI64OutFn>("counter_get")(counter, out var count);
        var filled = new long[3];
        results["fill"] = lib.Fn<HandleSliceFn>("counter_fill")(counter, filled, 3);
        lib.Fn<HandleBufferFn>("counter_history")(counter, out var history);
        var historyBytes = lib.TakeBytes(history);
        lib.Fn<HandleHandleOutFn>("counter_split")(counter, out var split);
        results["half_odd"] = lib.Fn<HandleI64OutFn>("counter_half")(split, out _);
        results["half_odd_message"] = lib.LastError();
        lib.Fn<HandleI64OutFn>("counter_get")(split, out var splitCount);
        results["merge_self"] = lib.Fn<HandleHandleFn>("counter_merge")(counter, counter);
        results["merge_self_message"] = lib.LastError();
        results["merge"] = lib.Fn<HandleHandleFn>("counter_merge")(counter, split);
        results["maybe_merge_none"] = lib.Fn<HandleMaybeHandleFn>("counter_maybe_merge")(counter, 0, IntPtr.Zero);
        results["maybe_merge_some"] = lib.Fn<HandleMaybeHandleFn>("counter_maybe_merge")(counter, 1, split);
        results["maybe_merge_bad_flag"] = lib.Fn<HandleMaybeHandleFn>("counter_maybe_merge")(counter, 2, split);
        lib.Fn<HandleI64OutFn>("counter_get")(counter, out var merged);
        results["into_total"] = lib.Fn<HandleI64OutFn>("counter_into_total")(counter, out var total);
        results["after_consume"] = lib.Fn<HandleI64OutFn>("counter_get")(counter, out _);
        results["after_consume_message"] = lib.LastError();
        lib.Fn<DestroyFn>("counter_destroy")(counter);

        lib.Fn<I64ToHandleFn>("counter_new")(0, out var zero);
        lib.Fn<HandleHandleOutFn>("counter_split")(zero, out var noSplit);
        results["explode"] = lib.Fn<HandleI64OutFn>("counter_explode")(zero, out _);
        results["explode_message"] = lib.LastError();
        results["after_panic"] = lib.Fn<HandleI64OutFn>("counter_get")(zero, out _);
        results["after_panic_message"] = lib.LastError();
        lib.Fn<DestroyFn>("counter_destroy")(zero);
        lib.Fn<DestroyFn>("counter_destroy")(split);
        results["try_new_bad"] = lib.Fn<I64ToHandleFn>("counter_try_new")(-1, out var notMade);
        results["try_new_bad_message"] = lib.LastError();
        results["try_new"] = lib.Fn<I64ToHandleFn>("counter_try_new")(8, out var made);
        lib.Fn<HandleI64OutFn>("counter_half")(made, out var half);
        lib.Fn<DestroyFn>("counter_destroy")(made);

        lib.Fn<ByteToByteFn>("negate")(1, out var negated);
        results["negate_bad"] = lib.Fn<ByteToByteFn>("negate")(2, out _);
        lib.Fn<IntToIntFn>("next_color")(7, out var nextColor);
        results["next_color_bad"] = lib.Fn<IntToIntFn>("next_color")(3, out _);
        results["next_color_bad_message"] = lib.LastError();
        lib.Fn<FlagsOfFn>("flags_of")(1, 7, out var flags);
        lib.Fn<DescribeFn>("describe")(flags, out var description);
        var describeText = lib.TakeString(description);
        results["describe_bad_bool"] = lib.Fn<DescribeFn>("describe")(new Flags { On = 3, Color = 0 }, out _);
        lib.Fn<ScaleFn>("scale")(new F32x4 { A = 1, B = 2, C = 3, D = 4 }, 1, 2, out var scaled);
        lib.Fn<ScaleFn>("scale")(new F32x4 { A = 1, B = 2, C = 3, D = 4 }, 0, 99, out var unscaled);
        lib.Fn<TextToBufferFn>("bytes_of")(NativeFacade.Utf8("hé"), 3, out var bytes);
        var bytesOf = lib.TakeBytes(bytes);
        lib.Fn<SumVecFn>("sum_vec")(new[] { 1, 2, 3 }, 3, out var vecSum);
        lib.Fn<TextToIntFn>("parse_color")(NativeFacade.Utf8("blue"), 4, out var parsed);
        results["parse_color_bad"] = lib.Fn<TextToIntFn>("parse_color")(NativeFacade.Utf8("x"), 1, out _);
        results["parse_color_bad_message"] = lib.LastError();
        results["check_ok"] = lib.Fn<ByteStatusFn>("check")(1);
        results["check_bad"] = lib.Fn<ByteStatusFn>("check")(0);
        results["check_bad_message"] = lib.LastError();
        lib.Fn<SizeToBufferFn>("read_bytes")(3, out var readBuffer);
        var read = lib.TakeBytes(readBuffer);
        lib.Fn<SizeToBufferFn>("read_bytes")(0, out var emptyBuffer);
        results["read_bytes_bad"] = lib.Fn<SizeToBufferFn>("read_bytes")(17, out _);
        lib.Fn<TextToBufferFn>("shout")(NativeFacade.Utf8("hey"), 3, out var shouted);
        var shout = lib.TakeString(shouted);
        results["shout_empty"] = lib.Fn<TextToBufferFn>("shout")(null, 0, out _);
        results["shout_empty_message"] = lib.LastError();
        results["shout_null"] = lib.Fn<TextToBufferFn>("shout")(null, 3, out _);
        lib.Fn<MinMaxFn>("min_max")(new[] { 3.0, -1.0, 8.0 }, 3, out var min, out var max);
        lib.Fn<DivModFn>("div_mod")(17, 5, out var quotient, out var remainder);
        results["div_zero"] = lib.Fn<DivModFn>("div_mod")(1, 0, out _, out _);
        lib.Fn<FirstEvenFn>("first_even")(new uint[] { 3, 5, 8 }, 3, out var evenPresent, out var even);
        results["noop"] = lib.Fn<NoArgumentsFn>("noop")();
        var answer = lib.Fn<NoArgumentsFn>("answer")();

        //Assert
        build.Info.ExportedSymbols.Should().Contain(build.Render.Exports);
        build.Info.ExportedSymbols.Should().Contain("shapecheck_answer");
        build.Render.Exports.Should().HaveCount(plan.Functions.Count + 1);
        ((int)results["add"]).Should().Be(0);
        ((int)results["add_all"]).Should().Be(0);
        count.Should().Be(10);
        ((int)results["fill"]).Should().Be(0);
        filled.Should().Equal(10L, 10L, 10L);
        historyBytes.Should().Equal(BitConverter.GetBytes((ushort)3).Concat(BitConverter.GetBytes((ushort)1)).Concat(BitConverter.GetBytes((ushort)2)));
        split.Should().NotBe(IntPtr.Zero);
        splitCount.Should().Be(5);
        ((int)results["half_odd"]).Should().Be(FacadeConventions.StatusUpstreamError);
        ((string)results["half_odd_message"]).Should().Be("DebugOnly(5)");
        ((int)results["merge_self"]).Should().Be(FacadeConventions.StatusInvalidArgument);
        ((string)results["merge_self_message"]).Should().Contain("is the same object as handle");
        ((int)results["merge"]).Should().Be(0);
        ((int)results["maybe_merge_none"]).Should().Be(0);
        ((int)results["maybe_merge_some"]).Should().Be(0);
        ((int)results["maybe_merge_bad_flag"]).Should().Be(FacadeConventions.StatusInvalidArgument);
        merged.Should().Be(20);
        ((int)results["into_total"]).Should().Be(0);
        total.Should().Be(20);
        ((int)results["after_consume"]).Should().Be(FacadeConventions.StatusHandleConsumed);
        ((string)results["after_consume_message"]).Should().Contain("already used up");
        noSplit.Should().Be(IntPtr.Zero);
        ((int)results["explode"]).Should().Be(FacadeConventions.StatusPanic);
        ((string)results["explode_message"]).Should().Be("panic: counter exploded at 0");
        ((int)results["after_panic"]).Should().Be(FacadeConventions.StatusPanic);
        ((string)results["after_panic_message"]).Should().Contain("earlier call panicked");
        ((int)results["try_new_bad"]).Should().Be(FacadeConventions.StatusUpstreamError);
        ((string)results["try_new_bad_message"]).Should().Be("start -1 is negative");
        notMade.Should().Be(IntPtr.Zero);
        ((int)results["try_new"]).Should().Be(0);
        half.Should().Be(4);
        negated.Should().Be((byte)0);
        ((int)results["negate_bad"]).Should().Be(FacadeConventions.StatusInvalidArgument);
        nextColor.Should().Be(0);
        ((int)results["next_color_bad"]).Should().Be(FacadeConventions.StatusInvalidArgument);
        ((string)results["next_color_bad_message"]).Should().Contain("is not a value of the enum Color");
        flags.On.Should().Be((byte)1);
        flags.Color.Should().Be(7);
        flags.Weights.D.Should().Be(4f);
        describeText.Should().Be("true Blue 10");
        ((int)results["describe_bad_bool"]).Should().Be(FacadeConventions.StatusInvalidArgument);
        scaled.D.Should().Be(8f);
        unscaled.D.Should().Be(4f);
        bytesOf.Should().Equal(NativeFacade.Utf8("hé"));
        vecSum.Should().Be(6);
        parsed.Should().Be(7);
        ((int)results["parse_color_bad"]).Should().Be(FacadeConventions.StatusUpstreamError);
        ((string)results["parse_color_bad_message"]).Should().Be("unknown colour 'x'");
        ((int)results["check_ok"]).Should().Be(0);
        ((int)results["check_bad"]).Should().Be(FacadeConventions.StatusUpstreamError);
        ((string)results["check_bad_message"]).Should().Contain("Opaque");
        read.Should().Equal(0, 1, 2);
        emptyBuffer.Data.Should().Be(IntPtr.Zero);
        emptyBuffer.Len.Should().Be((nuint)0);
        ((int)results["read_bytes_bad"]).Should().Be(FacadeConventions.StatusUpstreamError);
        shout.Should().Be("HEY");
        ((int)results["shout_empty"]).Should().Be(FacadeConventions.StatusUpstreamError);
        ((string)results["shout_empty_message"]).Should().Be("nothing to shout");
        ((int)results["shout_null"]).Should().Be(FacadeConventions.StatusNullArgument);
        min.Should().Be(-1.0);
        max.Should().Be(8.0);
        quotient.Should().Be(3);
        remainder.Should().Be(2);
        ((int)results["div_zero"]).Should().Be(FacadeConventions.StatusUpstreamError);
        evenPresent.Should().Be((byte)1);
        even.Should().Be(8u);
        ((int)results["noop"]).Should().Be(0);
        answer.Should().Be(42);
    }

    // ------------------------------------------------------------------ the C header

    public static TheoryData<string> HeaderPlans => new TheoryData<string> { "datacrate", "hardcrate", "edgecases", "coverage" };

    [Theory]
    [MemberData(nameof(HeaderPlans))]
    public async Task The_generated_header_compiles_as_strict_c11_and_can_be_included_twice(string planName)
    {
        //Arrange
        var compiler = CCompiler.FindOrSkip();
        using var sandbox = SampleSandbox.CreateEmpty();
        MappingPlan plan;
        if (planName == "coverage")
        {
            plan = CoverageCrate.Create();
        }
        else
        {
            var api = ModelSamples.Load(planName + "-api.json");
            api.Crate.CrateTypes = new List<string> { "rlib" };
            plan = new MappingPlanner().Plan(api, null, null);
        }

        var workspace = RenderingTestWorkspace.FromSample(sandbox, 15, plan.LibName);
        var render = new FacadeRenderer().Render(plan, workspace, null);

        //Act
        var (exitCode, output) = await CCompiler.CheckHeaderAsync(compiler, render.HeaderPath, sandbox.Directory);

        //Assert
        render.Generated.Should().BeTrue();
        File.Exists(render.HeaderPath).Should().BeTrue();
        exitCode.Should().Be(0, compiler + " rejected " + render.HeaderPath + ":\n" + output);
    }
}
