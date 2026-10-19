using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// End-to-end host-mode builds of synthetic crates that reproduce crate SHAPES which broke the
/// generated facade or the staging of real-world crates: types that borrow data, items behind
/// a cargo feature that is off, objects passed by <c>&amp;mut</c> to free functions,
/// <c>#[non_exhaustive]</c> structs, two types of the same name in different modules, a crate
/// type named like an imported std type, a crate <c>Result</c> alias beside std's <c>Result</c>, test-only macros, workspace <c>[patch]</c> tables,
/// <c>.cargo/config.toml</c>, hidden data folders and a crate nested inside another
/// workspace. Every crate is built offline (no registry access); each test skips by name
/// without cargo.
/// </summary>
public class CrateShapesEndToEndTests
{
    private static CrateBuildRequest Request(string manifest, string output, CrateTarget target) => new CrateBuildRequest
    {
        Source = new LocalManifestSource(manifest),
        Mode = BuildMode.Host,
        Targets = { target },
        OutputDirectory = output,
        Offline = true,
    };

    private static void ShouldSucceed(CrateBuildResult result)
        => result.Success.Should().BeTrue(result.ToString() + "\nWarnings: " + string.Join(" | ", result.Warnings));

    [Fact]
    public async Task a_crate_with_borrowing_types_feature_gated_items_and_shadowed_names_builds_and_reports_what_it_skipped()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("shapekit/Cargo.toml", "[package]\nname = \"shapekit\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[features]\ndefault = []\nextra = []\n\n[dependencies]\n");
        sandbox.WriteFile("shapekit/src/lib.rs", ShapekitLib);
        sandbox.WriteFile("shapekit/src/extra.rs", "pub fn extra_value() -> i32 {\n    1\n}\n");
        sandbox.WriteFile("shapekit/src/movie.rs", "pub struct Frame {\n    pub index: u64,\n    pub label: String,\n}\n\nimpl Frame {\n    pub fn new(index: u64) -> Frame {\n        Frame { index, label: String::new() }\n    }\n}\n");
        sandbox.WriteFile("shapekit/src/codec/mod.rs", "mod encode;\n\npub use encode::Encoder;\n\npub struct Frame {\n    pub width: u32,\n    pub data: Vec<u8>,\n}\n\nimpl Frame {\n    pub fn new(width: u32) -> Frame {\n        Frame { width, data: vec![0; width as usize] }\n    }\n}\n");
        sandbox.WriteFile("shapekit/src/codec/encode.rs", "use crate::codec::{Frame};\n\npub struct Encoder {\n    count: u32,\n}\n\nimpl Encoder {\n    pub fn new() -> Encoder {\n        Encoder { count: 0 }\n    }\n\n    pub fn encode(&mut self, frame: &Frame) -> u32 {\n        self.count += 1;\n        frame.width + self.count\n    }\n}\n\npub fn encode_once(frame: &Frame) -> u32 {\n    frame.width\n}\n");
        var request = Request(Path.Combine(sandbox.Directory, "shapekit", "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), target);
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        var exports = result.Record.Facade.Exports;
        exports.Should().Contain("shapekit_bump", "a free function that takes an object by &mut borrows it mutably");
        exports.Should().Contain("shapekit_limits_span", "a #[non_exhaustive] struct is wrapped as an object, never built from its fields");
        exports.Should().Contain(e => e.Contains("encoder_encode", StringComparison.Ordinal), "codec::Frame, imported by a use in a private module, is the codec's Frame");
        exports.Should().Contain("shapekit_arc_length", "the crate's own Arc is wrapped");
        exports.Should().Contain("shapekit_rules_parse_count", "Result<u32, Issue> in a module that imports nothing named Result is std's Result, not the crate's alias");
        exports.Should().NotContain(e => e.Contains("extra_value", StringComparison.Ordinal), "the feature 'extra' is off");
        NativeArtifactReader.Read(result.Targets.Single().ArtifactPaths.Single()).ExportedSymbols.Should().Contain("shapekit_bump");
        var report = File.ReadAllText(result.WrapReportPath);
        report.Should().Contain("Reader");
        report.Should().Contain("borrows data for a lifetime");
        report.Should().Contain("extra_value");
        report.Should().Contain("cargo feature this build does not enable");
        report.Should().Contain("registry::find", "Option<Arc<Entity>> is std's Arc (imported by use), not the crate's Arc");
        result.Warnings.Should().NotContain(w => w.Contains("generated by macros", StringComparison.Ordinal), "a macro inside #[cfg(test)] code never reaches the library");
        EndToEndKit.Log(nameof(a_crate_with_borrowing_types_feature_gated_items_and_shadowed_names_builds_and_reports_what_it_skipped), stopwatch);
    }

    [Fact]
    public async Task a_workspace_member_built_through_a_facade_keeps_the_workspaces_patch_cargo_config_and_hidden_data()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("ws/Cargo.toml", "[workspace]\nmembers = [\"shapelib\"]\nresolver = \"2\"\n\n[patch.crates-io]\nshape_patched_dep = { path = \"patched\" }\n");
        sandbox.WriteFile("ws/.cargo/config.toml", "[env]\nSHAPE_GREETING = \"hello from the workspace config\"\n");
        sandbox.WriteFile("ws/.shapedata/greeting.txt", "hidden data");
        sandbox.WriteFile("ws/.vscode/settings.json", "{}\n");
        sandbox.WriteFile("ws/patched/Cargo.toml", "[package]\nname = \"shape_patched_dep\"\nversion = \"1.0.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n");
        sandbox.WriteFile("ws/patched/src/lib.rs", "pub fn answer() -> i32 {\n    42\n}\n");
        sandbox.WriteFile("ws/shapelib/Cargo.toml", "[package]\nname = \"shapelib\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[dependencies]\nshape_patched_dep = \"1.0\"\n");
        sandbox.WriteFile("ws/shapelib/src/lib.rs", "pub fn answer() -> i32 {\n    shape_patched_dep::answer()\n}\n\npub fn greeting() -> String {\n    env!(\"SHAPE_GREETING\").to_string()\n}\n\npub fn data_len() -> usize {\n    include_str!(\"../../.shapedata/greeting.txt\").len()\n}\n");
        var request = Request(Path.Combine(sandbox.Directory, "ws", "shapelib", "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), target);
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        result.Record.Facade.Generated.Should().BeTrue();
        result.Record.Facade.Exports.Should().Contain(new[] { "shapelib_answer", "shapelib_greeting", "shapelib_data_len" });
        NativeArtifactReader.Read(result.Targets.Single().ArtifactPaths.Single()).ExportedSymbols.Should().Contain("shapelib_greeting");
        EndToEndKit.Log(nameof(a_workspace_member_built_through_a_facade_keeps_the_workspaces_patch_cargo_config_and_hidden_data), stopwatch);
    }

    [Fact]
    public async Task a_crate_with_its_own_workspace_inside_another_workspace_builds_against_an_inheriting_member()
    {
        //Arrange
        await Capabilities.SkipIfCargoMissingAsync();
        var target = EndToEndKit.HostTargetOrSkip();
        var stopwatch = Stopwatch.StartNew();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("outer/Cargo.toml", "[workspace]\nmembers = [\"crates/core\"]\nexclude = [\"crates/core/contract\"]\nresolver = \"2\"\n\n[workspace.package]\nedition = \"2021\"\nversion = \"0.3.0\"\nlicense = \"MIT\"\n");
        sandbox.WriteFile("outer/crates/core/Cargo.toml", "[package]\nname = \"shape_core\"\nversion.workspace = true\nedition.workspace = true\nlicense.workspace = true\n");
        sandbox.WriteFile("outer/crates/core/src/lib.rs", "pub fn base() -> i32 {\n    7\n}\n");
        sandbox.WriteFile("outer/crates/core/contract/Cargo.toml", "[package]\nname = \"shape_contract\"\nversion = \"0.1.0\"\nedition = \"2021\"\nlicense = \"MIT\"\n\n[workspace]\n\n[dependencies]\nshape_core = { path = \"..\" }\n");
        sandbox.WriteFile("outer/crates/core/contract/src/lib.rs", "pub fn doubled() -> i32 {\n    shape_core::base() * 2\n}\n");
        var request = Request(Path.Combine(sandbox.Directory, "outer", "crates", "core", "contract", "Cargo.toml"), Path.Combine(sandbox.Directory, "out"), target);
        using var builder = EndToEndKit.NewBuilder();

        //Act
        var result = await builder.BuildAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        ShouldSucceed(result);
        result.Record.Facade.Exports.Should().Contain("shape_contract_doubled");
        EndToEndKit.Log(nameof(a_crate_with_its_own_workspace_inside_another_workspace_builds_against_an_inheriting_member), stopwatch);
    }

    /// <summary>The root of the shapes crate (see the first test).</summary>
    private const string ShapekitLib = """
        pub mod codec;
        mod movie;

        #[cfg(feature = "extra")]
        pub mod extra;

        pub use movie::Frame;

        /// A view that borrows its bytes.
        pub struct Reader<'a> {
            data: &'a [u8],
            pos: usize,
        }

        impl<'a> Reader<'a> {
            pub fn new(data: &'a [u8]) -> Reader<'a> {
                Reader { data, pos: 0 }
            }

            pub fn remaining(&self) -> usize {
                self.data.len() - self.pos
            }
        }

        pub struct Counter {
            pub value: i64,
        }

        impl Counter {
            pub fn new() -> Counter {
                Counter { value: 0 }
            }
        }

        pub fn bump(counter: &mut Counter, by: i64) {
            counter.value += by;
        }

        pub fn read(counter: &Counter) -> i64 {
            counter.value
        }

        #[derive(Clone, Copy, Debug)]
        #[non_exhaustive]
        pub struct Limits {
            pub max: u32,
            pub min: u32,
        }

        impl Limits {
            pub fn standard() -> Limits {
                Limits { max: 10, min: 1 }
            }

            pub fn span(&self) -> u32 {
                self.max - self.min
            }
        }

        /// A geometric arc (named like std's Arc).
        #[derive(Clone, Copy, Debug)]
        pub struct Arc {
            pub radius: f64,
            pub sweep: f64,
        }

        pub fn arc_length(arc: Arc) -> f64 {
            arc.radius * arc.sweep
        }

        pub struct Entity {
            pub id: u32,
        }

        pub mod registry {
            use std::sync::Arc;

            use crate::Entity;

            pub fn find(id: u32) -> Option<Arc<Entity>> {
                Some(Arc::new(Entity { id }))
            }
        }

        #[derive(Debug)]
        pub struct ShapeError;

        impl std::fmt::Display for ShapeError {
            fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
                f.write_str("shape error")
            }
        }

        pub type Result<T> = std::result::Result<T, ShapeError>;

        pub mod rules {
            #[derive(Clone, Copy, Debug)]
            pub enum Issue {
                Empty,
            }

            pub fn parse_count(s: &str) -> Result<u32, Issue> {
                s.parse().map_err(|_| Issue::Empty)
            }
        }

        #[cfg(test)]
        mod tests {
            macro_rules! noop {
                () => {};
            }

            noop!();
        }
        """;
}
