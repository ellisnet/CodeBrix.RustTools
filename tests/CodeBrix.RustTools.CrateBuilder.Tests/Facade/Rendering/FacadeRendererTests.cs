using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Rendering;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering.Fixtures;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.CrateBuilder.WebGpu;
using CodeBrix.Templating;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering;

public class FacadeRendererTests
{
    private static (FacadeRenderResult Result, string Directory) RenderDatacrate(SampleSandbox sandbox, MappingPlan plan = null, FacadeRenderOptions options = null)
    {
        var workspace = RenderingTestWorkspace.FromSample(sandbox, 15, DatacratePlan.Lib);
        var result = new FacadeRenderer().Render(plan ?? DatacratePlan.Create(), workspace, options ?? new FacadeRenderOptions());
        return (result, result.Directory);
    }

    private static string Read(string directory, string relative) => File.ReadAllText(Path.Combine(directory, relative));

    [Fact]
    public void Render_writes_every_facade_file_under_the_codebrix_facade_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var (result, directory) = RenderDatacrate(sandbox);

        //Assert
        result.Generated.Should().BeTrue();
        directory.Should().Be(Path.Combine(sandbox.Directory, "stage", ".codebrix-facade", "datacrate_facade"));
        result.ManifestPath.Should().Be(Path.Combine(directory, "Cargo.toml"));
        result.HeaderPath.Should().Be(Path.Combine(directory, "datacrate.h"));
        result.LibName.Should().Be("datacrate");
        result.GeneratedFiles.Should().Equal("Cargo.lock", "Cargo.toml", "FACADE-README.txt", "datacrate.h", "src/lib.rs");
        foreach (var file in result.GeneratedFiles.Append("generated-files.json"))
        {
            File.Exists(Path.Combine(directory, file)).Should().BeTrue(file);
        }

        File.ReadAllBytes(Path.Combine(directory, "Cargo.lock")).Should().Equal(File.ReadAllBytes(Path.Combine(sandbox.Directory, "stage", "src", "Cargo.lock")));
        Directory.Exists(Path.Combine(directory, "src")).Should().BeTrue();
    }

    [Fact]
    public void Cargo_toml_is_a_cdylib_named_after_the_crate_with_a_path_dependency_and_its_own_workspace()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var (_, directory) = RenderDatacrate(sandbox);
        var toml = Read(directory, "Cargo.toml");

        //Assert
        toml.Should().Contain("[package]\nname = \"datacrate_facade\"\nversion = \"0.1.0\"\nedition = \"2024\"\npublish = false\n");
        toml.Should().Contain("[lib]\nname = \"datacrate\"\npath = \"src/lib.rs\"\ncrate-type = [\"cdylib\"]\n");
        toml.Should().Contain("upstream = { package = \"datacrate\", path = \"../../src\", default-features = true, features = [] }");
        toml.Should().Contain("\n[workspace]\n");
        toml.Should().Contain("[profile.dev]\npanic = \"unwind\"");
        toml.Should().Contain("[profile.release]\npanic = \"unwind\"");
        toml.Should().NotContain("build =");
        toml.Should().NotContain("\r");
    }

    [Fact]
    public void Cargo_toml_has_no_patch_section_when_the_workspace_has_none()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var (_, directory) = RenderDatacrate(sandbox);

        //Assert
        Read(directory, "Cargo.toml").Should().NotContain("[patch");
    }

    [Fact]
    public void WorkspacePatches_copies_the_patch_and_replace_tables_with_paths_rebased_on_the_facade_folder()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = sandbox.WriteFile("stage/shape/src/Cargo.toml", """
            [workspace]
            members = ["crates/lib"]

            [patch.crates-io]
            shape_dep = { path = "vendor/shape_dep" }
            shape_git = { git = "https://example.invalid/shape.git", rev = "abc123", version = "=0.7.2" }

            [patch."https://example.invalid/other.git"]
            "odd.name" = { path = "/abs/odd", features = ["a", "b"], default-features = false }

            [replace]
            "shape_old:1.0.0" = { path = "vendor/old" }
            """);
        var facade = Path.Combine(sandbox.Directory, "stage", "shape", ".codebrix-facade", "shape_facade");

        //Act
        var text = FacadeRenderer.WorkspacePatches(facade, root);

        //Assert
        text.Should().Contain("[patch.crates-io]\nshape_dep = { path = \"../../src/vendor/shape_dep\" }\n");
        text.Should().Contain("shape_git = { git = \"https://example.invalid/shape.git\", rev = \"abc123\", version = \"=0.7.2\" }\n");
        text.Should().Contain("[patch.\"https://example.invalid/other.git\"]\n\"odd.name\" = { path = \"/abs/odd\", features = [\"a\", \"b\"], default-features = false }\n");
        text.Should().Contain("[replace]\n\"shape_old:1.0.0\" = { path = \"../../src/vendor/old\" }\n");
        FacadeRenderer.WorkspacePatches(facade, Path.Combine(sandbox.Directory, "missing", "Cargo.toml")).Should().BeEmpty();
    }

    [Fact]
    public void WorkspaceProfiles_copies_the_workspaces_profiles_and_keeps_unwinding_in_dev_and_release()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var root = sandbox.WriteFile("stage/shape/src/Cargo.toml", """
            [workspace]
            members = ["crates/lib"]

            [profile.dev]
            opt-level = 1

            [profile.dev.package."*"]
            opt-level = 2

            [profile.dev.package.shape-hot]
            opt-level = 3

            [profile.release]
            lto = "thin"
            codegen-units = 1
            panic = "abort"
            strip = true

            [profile.release.build-override]
            opt-level = 0

            [profile.ship]
            inherits = "release"
            panic = "abort"
            debug = 1
            """);

        //Act
        var text = FacadeRenderer.WorkspaceProfiles(root);
        var none = FacadeRenderer.WorkspaceProfiles(Path.Combine(sandbox.Directory, "missing", "Cargo.toml"));

        //Assert
        text.Should().Contain("[profile.dev]\npanic = \"unwind\"\nopt-level = 1\n\n[profile.dev.package.\"*\"]\nopt-level = 2\n\n[profile.dev.package.shape-hot]\nopt-level = 3\n");
        text.Should().Contain("[profile.release]\n# The upstream workspace sets panic = \"abort\" here; the facade needs \"unwind\".\npanic = \"unwind\"\nlto = \"thin\"\ncodegen-units = 1\nstrip = true\n\n[profile.release.build-override]\nopt-level = 0");
        text.Should().Contain("[profile.ship]\n# The upstream workspace sets panic = \"abort\" here; the facade needs \"unwind\".\ninherits = \"release\"\ndebug = 1");
        text.Should().NotContain("[profile.dev.package]\n");
        text.Should().NotContain("panic = \"abort\"\n");
        CountOf(text, "panic = \"unwind\"").Should().Be(2);
        none.Should().Be("# The export boundary catches panics (catch_unwind), which needs unwinding.\n\n[profile.dev]\npanic = \"unwind\"\n\n[profile.release]\npanic = \"unwind\"");
    }

    [Fact]
    public void Cargo_toml_carries_the_workspaces_release_profile()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workspace = RenderingTestWorkspace.FromSample(sandbox, 15, DatacratePlan.Lib);
        File.AppendAllText(workspace.WorkspaceManifestPath ?? workspace.UpstreamManifestPath, "\n[profile.release]\nlto = true\n");

        //Act
        var result = new FacadeRenderer().Render(DatacratePlan.Create(), workspace, new FacadeRenderOptions());
        var toml = Read(result.Directory, "Cargo.toml");

        //Assert
        toml.Should().Contain("[profile.release]\npanic = \"unwind\"\nlto = true\n");
        toml.Should().Contain("[profile.dev]\npanic = \"unwind\"");
    }

    [Fact]
    public void Features_of_the_request_go_to_the_upstream_dependency_sorted_and_distinct()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var options = new FacadeRenderOptions
        {
            Features = { "zeta", "alpha", "zeta", " " },
            NoDefaultFeatures = true,
            AllFeatures = true,
            DeclaredFeatures = { "default", "beta" },
        };

        //Act
        var (_, directory) = RenderDatacrate(sandbox, options: options);

        //Assert
        Read(directory, "Cargo.toml").Should().Contain("default-features = false, features = [\"alpha\", \"beta\", \"zeta\"] }");
    }

    [Fact]
    public void FromRequest_takes_the_features_and_the_crate_identity_of_the_model()
    {
        //Arrange
        var request = new CrateBuildRequest { Features = { "x" }, NoDefaultFeatures = true, AllFeatures = true };
        var api = ModelSamples.Load(ModelSamples.DataCrate);

        //Act
        var options = FacadeRenderOptions.FromRequest(request, api);

        //Assert
        options.Features.Should().Equal("x");
        options.NoDefaultFeatures.Should().BeTrue();
        options.AllFeatures.Should().BeTrue();
        options.UpstreamPackageName.Should().Be("datacrate");
        options.UpstreamVersion.Should().Be("0.1.0");
    }

    [Fact]
    public void Lib_rs_holds_the_support_code_and_one_guarded_export_per_planned_function()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();

        //Act
        var (result, directory) = RenderDatacrate(sandbox, plan);
        var lib = Read(directory, "src/lib.rs");

        //Assert
        lib.Should().Contain("#![allow(clippy::all, unsafe_op_in_unsafe_fn)]");
        lib.Should().Contain("\nuse upstream as up;\n");
        lib.Should().Contain("pub const STATUS_OK: i32 = 0;");
        lib.Should().Contain("pub const STATUS_NULL_ARGUMENT: i32 = -1;");
        lib.Should().Contain("pub const STATUS_INVALID_ARGUMENT: i32 = -2;");
        lib.Should().Contain("pub const STATUS_UPSTREAM_ERROR: i32 = -3;");
        lib.Should().Contain("pub const STATUS_PANIC: i32 = -4;");
        lib.Should().Contain("pub const STATUS_HANDLE_CONSUMED: i32 = -5;");
        lib.Should().Contain("pub unsafe extern \"C\" fn datacrate_last_error_message(buf: *mut u8, cap: usize, written: *mut usize) -> i32 {");
        lib.Should().Contain("pub struct Buffer {\n        pub data: *mut u8,\n        pub len: usize,\n        pub cap: usize,\n    }");
        lib.Should().Contain("drop(Vec::from_raw_parts(buffer.data, buffer.len, buffer.cap));");
        foreach (var f in plan.Functions)
        {
            lib.Should().Contain("#[unsafe(no_mangle)]\npub unsafe extern \"C\" fn " + f.CName + "(");
        }

        var statusFunctions = plan.Functions.Count(f => f.ReturnsStatus);
        CountOf(lib, "::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| upstream::").Should().Be(statusFunctions);
        lib.Should().Contain("pub type datacrate_encoder = facade_support::HandleBox<up::Encoder>;");
        lib.Should().Contain("#[repr(C)]\n#[derive(Clone, Copy)]\npub struct datacrate_vec2 {\n    /// The horizontal component.\n    pub x: f64,");
        lib.Should().Contain("impl TryFrom<datacrate_encoder_config> for up::EncoderConfig {");
        lib.Should().Contain("impl From<datacrate_vec2> for up::Vec2 {");
        lib.Should().Contain("#[repr(i32)]\n#[derive(Clone, Copy, Debug, PartialEq, Eq)]\npub enum datacrate_mode {");
        lib.Should().Contain("impl TryFrom<i32> for datacrate_mode {");
        lib.Should().Contain("const _: () = assert!(::core::mem::size_of::<datacrate_vec2>() == 16);");
        lib.Should().Contain("const _: () = assert!(::core::mem::offset_of!(datacrate_encoder_config, frame_size) == 4);");
        lib.Should().Contain("/// A two-dimensional vector of `f64` components.");
        lib.Should().Contain("Err(e) => return crate::facade_support::fail(crate::facade_support::STATUS_UPSTREAM_ERROR, ::std::string::ToString::to_string(&e)),");
        result.Exports.Should().Equal(DatacratePlan.PlannedSymbols(plan));
    }

    [Fact]
    public void The_header_declares_every_export_with_the_status_conventions()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();

        //Act
        var (_, directory) = RenderDatacrate(sandbox, plan);
        var header = Read(directory, "datacrate.h");

        //Assert
        CountOf(header, "#pragma once").Should().Be(1);
        header.Should().Contain("#define DATACRATE_STATUS_OK 0\n#define DATACRATE_STATUS_NULL_ARGUMENT (-1)\n#define DATACRATE_STATUS_INVALID_ARGUMENT (-2)\n#define DATACRATE_STATUS_UPSTREAM_ERROR (-3)\n#define DATACRATE_STATUS_PANIC (-4)\n#define DATACRATE_STATUS_HANDLE_CONSUMED (-5)\n");
        header.Should().Contain("typedef struct datacrate_buffer {\n    uint8_t* data;\n    size_t len;\n    size_t cap;\n} datacrate_buffer;");
        header.Should().Contain("typedef struct datacrate_encoder datacrate_encoder;");
        header.Should().Contain("typedef int32_t datacrate_mode;\nenum {\n    // Pass the bytes through unchanged.\n    DATACRATE_MODE_A = 1,");
        header.Should().Contain("typedef struct datacrate_array_f64_3x3 {\n    double values[3][3];\n} datacrate_array_f64_3x3;");
        header.Should().Contain("typedef struct datacrate_encoder_config {\n    // The processing mode.\n    int32_t mode;\n    // Bytes per frame.\n    uint32_t frame_size;\n} datacrate_encoder_config;");
        header.Should().Contain("int32_t datacrate_last_error_message(uint8_t* buf, size_t cap, size_t* written);");
        header.Should().Contain("int32_t datacrate_greet(const uint8_t* name_ptr, size_t name_len, datacrate_buffer* out);");
        header.Should().Contain("int32_t datacrate_find(const int32_t* xs_ptr, size_t xs_len, int32_t x, bool* out_present, size_t* out);");
        header.Should().Contain("int32_t datacrate_encoder_new(datacrate_encoder_config cfg, datacrate_encoder** out);");
        header.Should().Contain("void datacrate_encoder_destroy(datacrate_encoder* handle);");
        header.Should().Contain("void datacrate_free_buffer(datacrate_buffer buffer);");
        header.Should().Contain("int32_t datacrate_parse_pair(const uint8_t* s_ptr, size_t s_len, int32_t* out0, int32_t* out1);");
        foreach (var f in plan.Functions)
        {
            CountOf(header, " " + f.CName + "(").Should().Be(1, f.CName);
        }

        header.Should().Contain("#ifdef __cplusplus\nextern \"C\" {\n#endif");
    }

    [Fact]
    public void The_read_me_and_the_manifest_describe_the_generated_files()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();

        //Act
        var (result, directory) = RenderDatacrate(sandbox);
        var readme = Read(directory, "FACADE-README.txt");
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(directory, "generated-files.json")));

        //Assert
        readme.Should().Contain("Generated by CodeBrix.RustTools.CrateBuilder (FacadeRenderer)");
        readme.Should().Contain("Do not edit the files");
        readme.Should().Contain("  datacrate_encoder_new\n");
        manifest.RootElement.GetProperty("generator").GetString().Should().Be("CodeBrix.RustTools.CrateBuilder FacadeRenderer");
        manifest.RootElement.GetProperty("files").EnumerateArray().Select(e => e.GetProperty("path").GetString()).Should().Equal(result.GeneratedFiles);
        manifest.RootElement.GetProperty("files").EnumerateArray().All(e => e.GetProperty("sha256").GetString().Length == 64).Should().BeTrue();
    }

    [Fact]
    public void Rendering_twice_gives_identical_bytes_and_rewrites_nothing()
    {
        //Arrange
        using var first = SampleSandbox.CreateEmpty();
        using var second = SampleSandbox.CreateEmpty();
        var (one, oneDirectory) = RenderDatacrate(first);
        var stamps = one.GeneratedFiles.ToDictionary(f => f, f => File.GetLastWriteTimeUtc(Path.Combine(oneDirectory, f)));

        //Act
        var again = new FacadeRenderer().Render(DatacratePlan.Create(), ExistingWorkspace(first), new FacadeRenderOptions());
        var (other, otherDirectory) = RenderDatacrate(second);

        //Assert
        again.ChangedFiles.Should().BeEmpty();
        again.RemovedFiles.Should().BeEmpty();
        foreach (var f in one.GeneratedFiles)
        {
            File.GetLastWriteTimeUtc(Path.Combine(oneDirectory, f)).Should().Be(stamps[f], f);
            File.ReadAllBytes(Path.Combine(otherDirectory, f)).Should().Equal(File.ReadAllBytes(Path.Combine(oneDirectory, f)), f);
        }

        File.ReadAllBytes(Path.Combine(otherDirectory, "generated-files.json")).Should().Equal(File.ReadAllBytes(Path.Combine(oneDirectory, "generated-files.json")));
        other.Exports.Should().Equal(one.Exports);
    }

    private static StagedWorkspace ExistingWorkspace(SampleSandbox sandbox)
    {
        var manifest = Path.Combine(sandbox.Directory, "stage", "src", "Cargo.toml");
        return new StagedWorkspace
        {
            Directory = Path.Combine(sandbox.Directory, "stage"),
            ManifestPath = manifest,
            UpstreamManifestPath = manifest,
            WorkspaceManifestPath = manifest,
            CrateName = DatacratePlan.Lib,
            Identity = new CrateSourceIdentity { CrateName = DatacratePlan.Lib, CrateVersion = "0.1.0" },
        };
    }

    [Fact]
    public void A_re_render_removes_the_stale_generated_files_and_nothing_else()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workspace = RenderingTestWorkspace.FromSample(sandbox, 15, DatacratePlan.Lib);
        var helpers = sandbox.WriteFile("mine/helpers.rs", CoverageCrate.HelpersRs);
        var plan = DatacratePlan.Create();
        plan.AdditionalFacadeFiles.Add(helpers);
        var renderer = new FacadeRenderer();
        var first = renderer.Render(plan, workspace, null);
        var firstLib = Read(first.Directory, "src/lib.rs");
        File.WriteAllText(Path.Combine(first.Directory, "notes.txt"), "mine");
        Directory.CreateDirectory(Path.Combine(first.Directory, "target", "release"));
        File.WriteAllText(Path.Combine(first.Directory, "target", "release", "keep.bin"), "x");
        File.WriteAllText(Path.Combine(first.Directory, "src", "mine.rs"), "// mine");

        //Act
        var second = renderer.Render(DatacratePlan.Create(), workspace, null);

        //Assert
        first.GeneratedFiles.Should().Contain("src/helpers.rs");
        firstLib.Should().Contain("\npub mod helpers;\n");
        second.RemovedFiles.Should().Equal("src/helpers.rs");
        second.ChangedFiles.Should().Equal("FACADE-README.txt", "src/lib.rs");
        File.Exists(Path.Combine(second.Directory, "src", "helpers.rs")).Should().BeFalse();
        File.Exists(Path.Combine(second.Directory, "notes.txt")).Should().BeTrue();
        File.Exists(Path.Combine(second.Directory, "target", "release", "keep.bin")).Should().BeTrue();
        File.Exists(Path.Combine(second.Directory, "src", "mine.rs")).Should().BeTrue();
        Directory.GetDirectories(Path.Combine(sandbox.Directory, "stage", ".codebrix-facade")).Should().HaveCount(1, "the temporary render folder is gone");
    }

    [Fact]
    public void A_pass_through_plan_renders_nothing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = new MappingPlanner().Plan(ModelSamples.Load(ModelSamples.EdgeCases), null, null);
        var workspace = RenderingTestWorkspace.FromSample(sandbox, 15, "edgecases");

        //Act
        var result = new FacadeRenderer().Render(plan, workspace, null);

        //Assert
        plan.UpstreamIsCdylib.Should().BeTrue();
        result.Generated.Should().BeFalse();
        result.Directory.Should().BeNull();
        result.GeneratedFiles.Should().BeEmpty();
        result.Exports.Should().Equal(plan.Functions.Select(f => f.CName));
        Directory.Exists(Path.Combine(workspace.Directory, ".codebrix-facade")).Should().BeFalse();
        result.ToGenerationResult().Generated.Should().BeFalse();
    }

    [Fact]
    public void The_planner_plan_of_the_datacrate_sample_renders_every_planned_export()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var api = ModelSamples.Load(ModelSamples.DataCrate);
        var plan = new MappingPlanner().Plan(api, null, null);

        //Act
        var (result, directory) = RenderDatacrate(sandbox, plan, FacadeRenderOptions.FromRequest(new CrateBuildRequest(), api));
        var lib = Read(directory, "src/lib.rs");
        var generation = result.ToGenerationResult();

        //Assert
        result.Exports.Should().Equal(new[] { "datacrate_last_error_message" }.Concat(plan.Functions.Select(f => f.CName)));
        foreach (var f in plan.Functions)
        {
            lib.Should().Contain("pub unsafe extern \"C\" fn " + f.CName + "(");
        }

        generation.Generated.Should().BeTrue();
        generation.CrateName.Should().Be("datacrate_facade");
        generation.ManifestPath.Should().Be(result.ManifestPath);
        generation.ExportedFunctions.Select(e => e.Name).Should().Equal(result.Exports);
    }

    [Fact]
    public void The_coverage_plan_renders_every_shape()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var workspace = CoverageWorkspace(sandbox, out var helpers);
        var plan = CoverageCrate.Create();
        plan.AdditionalFacadeFiles.Add(helpers);

        //Act
        var result = new FacadeRenderer().Render(plan, workspace, null);
        var lib = Read(result.Directory, "src/lib.rs");

        //Assert
        lib.Should().Contain("crate::facade_support::handle_take(__cb_p0, \"handle\")");
        lib.Should().Contain("crate::facade_support::slice_mut(out_ptr, out_len, \"out\")");
        lib.Should().Contain("Buffer::from_elements::<u16>(::std::vec::Vec::<u16>::from(__cb_value))");
        lib.Should().Contain("HandleBox::boxed(__cb_some)");
        lib.Should().Contain(".write(::std::ptr::null_mut())");
        lib.Should().Contain("::std::format!(\"{:?}\", e)");
        lib.Should().Contain("::std::any::type_name_of_val(&e)");
        lib.Should().Contain("crate::facade_support::flag(has_k, \"has_k\")");
        lib.Should().Contain("if !__cb_p0.is_null() && __cb_p0 == __cb_p1 {");
        lib.Should().Contain("let this = __cb_a0;\n    let __cb_call = ::std::panic::catch_unwind(::std::panic::AssertUnwindSafe(|| this.get()));");
        lib.Should().Contain("|| up::noop()");
        lib.Should().Contain("pub mod overrides {\n    use super::*;\n    use crate::facade_support::*;\n\n    // Override for answer\n    /// A hand-written export.");
        lib.Should().Contain("impl TryFrom<shapecheck_flags> for up::Flags {");
        lib.Should().Contain("on: crate::facade_support::bool_from(c.on)?,");
        result.GeneratedFiles.Should().Contain("src/helpers.rs");
    }

    [Fact]
    public void A_webgpu_linked_plan_gets_the_recipe_module_lines_and_a_build_script()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.LinkToCodeBrixWebGpu = true;

        //Act
        var (result, directory) = RenderDatacrate(sandbox, plan);
        var lib = Read(directory, "src/lib.rs");
        var build = Read(directory, "build.rs");

        //Assert
        result.GeneratedFiles.Should().Contain("build.rs");
        Read(directory, "Cargo.toml").Should().Contain("publish = false\nbuild = \"build.rs\"\n");
        lib.Should().Contain(string.Join("\n", WebGpuLinkRecipe.ModuleLines));
        lib.IndexOf("mod wgpu_ffi {", StringComparison.Ordinal).Should().BeLessThan(lib.IndexOf("use upstream as up;", StringComparison.Ordinal));
        build.Should().Contain(string.Join("\n", WebGpuLinkRecipe.BuildScriptLines));
    }

    [Fact]
    public void A_duplicate_c_name_is_refused()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.Functions[1].CName = plan.Functions[0].CName;

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("is used twice");
    }

    [Fact]
    public void The_reserved_last_error_name_is_refused_for_a_planned_function()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.Functions[0].CName = "datacrate_last_error_message";

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Stage.Should().Be(CrateBuildStage.RenderingFacade);
    }

    [Fact]
    public void An_out_length_parameter_is_refused_because_a_buffer_carries_its_length()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        var greet = plan.Functions.Single(f => f.CName == "datacrate_greet");
        greet.Parameters.Add(PlanKit.Out("out_len", PlanKit.Prim("usize"), ParameterRole.OutLength));

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("OutLength");
    }

    [Fact]
    public void A_pass_through_function_in_a_facade_plan_is_refused()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.Functions[0].Kind = FunctionKind.PassThrough;

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("pass-through");
    }

    [Fact]
    public void A_missing_additional_facade_file_is_refused()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.AdditionalFacadeFiles.Add(Path.Combine(sandbox.Directory, "nope.rs"));

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("does not exist");
    }

    [Fact]
    public void An_additional_facade_file_with_a_taken_module_name_is_refused()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var plan = DatacratePlan.Create();
        plan.AdditionalFacadeFiles.Add(sandbox.WriteFile("overrides.rs", "// x"));

        //Act
        var act = () => RenderDatacrate(sandbox, plan);

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("taken");
    }

    [Fact]
    public void Every_template_is_embedded_and_parses()
    {
        //Act
        var names = FacadeTemplates.Names();

        //Assert
        names.Should().Equal("Cargo.toml.sbn", "FACADE-README.txt.sbn", "build.rs.sbn", "header.h.sbn", "lib.rs.sbn");
        foreach (var name in names)
        {
            FacadeTemplates.Parse(name, FacadeTemplates.Text(name)).HasErrors.Should().BeFalse(name);
        }
    }

    [Fact]
    public void A_template_with_a_missing_variable_fails_instead_of_rendering_empty_text()
    {
        //Arrange
        var template = Template.Parse("x = {{ not_in_the_model }}");

        //Act
        var act = () => FacadeTemplates.Render("probe", template, new FacadeSourceModel());

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("probe");
    }

    [Fact]
    public void A_template_with_a_parse_error_is_refused_with_its_messages()
    {
        //Act
        var act = () => FacadeTemplates.Parse("broken", "{{ for x in }}");

        //Assert
        act.Should().Throw<FacadeGenerationException>().Which.Message.Should().Contain("broken");
    }

    [Theory]
    [InlineData("CName", "c_name")]
    [InlineData("CDocLines", "c_doc_lines")]
    [InlineData("WebGpuModuleLines", "web_gpu_module_lines")]
    [InlineData("LinkToWebGpu", "link_to_web_gpu")]
    [InlineData("ByteSize", "byte_size")]
    [InlineData("Prefix", "prefix")]
    public void Model_members_are_snake_case_in_the_templates(string member, string expected)
    {
        //Act
        var name = FacadeTemplates.SnakeCase(member);

        //Assert
        name.Should().Be(expected);
    }

    [Theory]
    [InlineData("type", "r#type")]
    [InlineData("self", "self_")]
    [InlineData("value", "value")]
    [InlineData("r#match", "r#match")]
    public void Rust_identifiers_escape_keywords(string name, string expected)
    {
        //Act
        var ident = RustSyntax.Ident(name);

        //Assert
        ident.Should().Be(expected);
    }

    internal static StagedWorkspace CoverageWorkspace(SampleSandbox sandbox, out string helpers)
    {
        var manifest = sandbox.WriteFile("stage/src/Cargo.toml", CoverageCrate.CargoToml + "\n");
        sandbox.WriteFile("stage/src/src/lib.rs", CoverageCrate.LibRs + "\n");
        helpers = sandbox.WriteFile("mine/helpers.rs", CoverageCrate.HelpersRs + "\n");
        return new StagedWorkspace
        {
            Directory = Path.Combine(sandbox.Directory, "stage"),
            ManifestPath = manifest,
            UpstreamManifestPath = manifest,
            WorkspaceManifestPath = manifest,
            CrateName = CoverageCrate.Lib,
            Identity = new CrateSourceIdentity { CrateName = CoverageCrate.Lib, CrateVersion = CoverageCrate.Version },
        };
    }

    private static int CountOf(string text, string part)
    {
        var count = 0;
        for (var i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + part.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
