using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoOutputCollectorTests
{
    private static string[] FixtureLines(string fileName)
        => File.ReadAllLines(RepoPaths.Fixture(Path.Combine("Building", fileName)));

    private static CargoCommandResult Collect(
        CargoCommand command,
        string stdoutFixture,
        string stderrFixture = null,
        int exitCode = 0,
        CargoBuildOptions options = null,
        List<CargoProgress> progress = null)
    {
        var collector = new CargoOutputCollector(command, options, p => progress?.Add(p));
        var stderr = stderrFixture == null ? Array.Empty<string>() : FixtureLines(stderrFixture);
        foreach (var line in stderr)
        {
            collector.OnStandardErrorLine(line);
        }

        if (stdoutFixture != null)
        {
            foreach (var line in FixtureLines(stdoutFixture))
            {
                collector.OnStandardOutputLine(line);
            }
        }

        return collector.CreateResult(exitCode, false, false, TimeSpan.FromSeconds(1), "cargo " + CargoRunner.SubcommandName(command), string.Join("\n", stderr), "/work");
    }

    [Fact]
    public void CreateResult_parses_the_rustc_warnings_of_sample_10()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-10-warnings.jsonl", "build-10-warnings.stderr.txt");

        //Assert
        result.Success.Should().BeTrue();
        result.Warnings.Count(w => w.Source == DiagnosticOrigin.Rustc).Should().Be(20);
        result.WarningCount.Should().Be(21);
        result.ErrorCount.Should().Be(0);
        result.Warnings.Where(w => w.Source == DiagnosticOrigin.Rustc).Should().OnlyContain(w => w.Code != null && w.Rendered.StartsWith("warning: "));
        result.Warnings.Select(w => w.Code).Should().Contain(new[] { "unused_imports", "dead_code", "non_snake_case" });
    }

    [Fact]
    public void CreateResult_reads_spans_and_children_of_a_warning()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-10-warnings.jsonl");

        //Assert
        var unused = result.Warnings.First(w => w.Code == "unused_imports");
        unused.Level.Should().Be(DiagnosticLevel.Warning);
        unused.Message.Should().Be("unused import: `std::collections::HashMap`");
        unused.PrimarySpan.FileName.Should().Be("src/main.rs");
        unused.PrimarySpan.LineStart.Should().Be(7);
        unused.PrimarySpan.ColumnStart.Should().Be(5);
        unused.PrimarySpan.ColumnEnd.Should().Be(30);
        unused.Children.Should().HaveCount(2);
        unused.Children[1].Level.Should().Be(DiagnosticLevel.Help);
        unused.Children[1].Spans[0].SuggestedReplacement.Should().Be(string.Empty);
        unused.PackageName.Should().Be("test_project_10");
        unused.TargetName.Should().Be("test_project_10");
        unused.ToString().Should().Be(unused.Rendered);
    }

    [Fact]
    public void CreateResult_captures_build_script_warnings_from_standard_error()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-10-warnings.jsonl", "build-10-warnings.stderr.txt");

        //Assert
        var warning = result.BuildScriptWarnings.Should().ContainSingle().Which;
        warning.Source.Should().Be(DiagnosticOrigin.BuildScript);
        warning.PackageName.Should().Be("test_project_10");
        warning.Message.Should().Be("this is an intentional build-script warning from test_project_10");
        warning.Rendered.Should().StartWith("warning: test_project_10@0.1.0: ");
    }

    [Fact]
    public void CreateResult_removes_duplicate_warnings_but_keeps_them_in_Diagnostics()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Clippy, null, null);
        foreach (var line in FixtureLines("clippy-10.jsonl"))
        {
            collector.OnStandardOutputLine(line);
        }

        collector.OnStandardErrorLine("warning: test_project_10@0.1.0: this is an intentional build-script warning from test_project_10");
        collector.OnStandardErrorLine("warning: test_project_10@0.1.0: this is an intentional build-script warning from test_project_10");

        //Act
        var result = collector.CreateResult(101, false, false, TimeSpan.Zero, "cargo clippy", string.Empty, "/work");

        //Assert
        result.BuildScriptWarnings.Should().ContainSingle();
        result.Diagnostics.Count(d => d.Source == DiagnosticOrigin.BuildScript).Should().Be(2);
    }

    [Fact]
    public void CreateResult_parses_clippy_lints_and_denied_errors()
    {
        //Act
        var result = Collect(CargoCommand.Clippy, "clippy-10.jsonl", exitCode: 101);

        //Assert
        result.Success.Should().BeFalse();
        result.Errors.Select(e => e.Code).Should().Contain(new[] { "clippy::never_loop", "clippy::absurd_extreme_comparisons" });
        result.Errors.Should().OnlyContain(e => e.Source == DiagnosticOrigin.Rustc);
        result.WarningCount.Should().BeGreaterThan(10);
    }

    [Fact]
    public void CreateResult_parses_the_artifacts_of_sample_06()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-06-two-bins.jsonl");

        //Assert
        result.Artifacts.Should().HaveCount(3);
        var buildScript = result.Artifacts[0];
        buildScript.IsBuildScript.Should().BeTrue();
        buildScript.Executable.Should().BeNull();
        buildScript.IsRunnable.Should().BeFalse();
        result.RunnableArtifacts.Select(a => a.TargetName).Should().BeEquivalentTo(new[] { "buildinfo", "crc32sum" });
        var buildinfo = result.RunnableArtifacts[0];
        buildinfo.Executable.Should().Be("/work/test_project_06/target/debug/buildinfo");
        buildinfo.PackageName.Should().Be("test_project_06");
        buildinfo.PackageVersion.Should().Be("0.3.1");
        buildinfo.ManifestPath.Should().Be("/work/test_project_06/Cargo.toml");
        buildinfo.Kinds.Should().ContainSingle().Which.Should().Be("bin");
        buildinfo.Profile.OptLevel.Should().Be("0");
        buildinfo.Profile.DebugInfo.Should().Be("2");
        buildinfo.Profile.DebugAssertions.Should().BeTrue();
        buildinfo.Profile.Test.Should().BeFalse();
        buildinfo.Fresh.Should().BeFalse();
        buildinfo.Filenames.Should().ContainSingle();
        buildinfo.ToString().Should().Be("test_project_06/buildinfo (bin)");
    }

    [Fact]
    public void CreateResult_reports_the_error_of_a_broken_build()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-05-broken.jsonl", "build-05-broken.stderr.txt", exitCode: 101);

        //Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(101);
        var error = result.Errors.Should().ContainSingle().Which;
        error.Message.Should().Be("this file contains an unclosed delimiter");
        error.Source.Should().Be(DiagnosticOrigin.Rustc);
        error.PrimarySpan.FileName.Should().Be("src/main.rs");
        error.PrimarySpan.LineStart.Should().Be(30);
        error.PrimarySpan.IsPrimary.Should().BeTrue();
        error.PackageName.Should().Be("test_project_05");
        error.Spans.Should().HaveCount(3);
        result.StandardError.Should().Contain("could not compile");
    }

    [Fact]
    public void CreateResult_parses_libtest_output_across_test_binaries()
    {
        //Act
        var result = (CargoTestResult)Collect(CargoCommand.Test, "test-05-failing.stdout.txt", "test-05-failing.stderr.txt", exitCode: 101);

        //Assert
        result.Success.Should().BeFalse();
        result.TestsPassed.Should().Be(9);
        result.TestsFailed.Should().Be(1);
        result.TestsIgnored.Should().Be(1);
        result.TestsMeasured.Should().Be(0);
        result.TestsFilteredOut.Should().Be(0);
        result.TestBinaryCount.Should().Be(5);
        result.Tests.Should().HaveCount(11);
        result.FailedTestNames.Should().ContainSingle().Which.Should().Be("deliberately_fails");
        result.AllTestsPassed.Should().BeFalse();
    }

    [Fact]
    public void CreateResult_attaches_failure_output_and_ignore_reasons()
    {
        //Act
        var result = (CargoTestResult)Collect(CargoCommand.Test, "test-05-failing.stdout.txt", exitCode: 101);

        //Assert
        var failed = result.Tests.Single(t => t.Failed);
        failed.Output.Should().Contain("diagnostic output before the failure");
        failed.Output.Should().Contain("arithmetic is broken on purpose");
        failed.Output.Should().NotContain("failures:");
        var ignored = result.Tests.Single(t => t.Ignored);
        ignored.Name.Should().Be("slow_ignored_test");
        ignored.IgnoreReason.Should().Be("slow");
        result.Tests.Should().Contain(t => t.Name == "src/lib.rs - is_palindrome (line 45)" && t.Passed);
        result.Tests.Where(t => t.Passed).Should().OnlyContain(t => t.Output == null);
    }

    [Fact]
    public void CreateResult_keeps_libtest_text_in_StandardOutput_but_not_json()
    {
        //Act
        var result = Collect(CargoCommand.Test, "test-05-failing.stdout.txt", exitCode: 101);

        //Assert
        result.StandardOutput.Should().Contain("running 4 tests");
        result.StandardOutput.Should().NotContain("\"reason\"");
        result.RawMessages.Should().BeEmpty();
    }

    [Fact]
    public void CreateResult_keeps_raw_messages_when_asked()
    {
        //Act
        var result = Collect(CargoCommand.Build, "build-06-two-bins.jsonl", options: new CargoBuildOptions { KeepRawOutput = true });

        //Assert
        result.RawMessages.Should().HaveCount(5);
        result.RawMessages.Should().OnlyContain(line => line.StartsWith("{\"reason\":"));
    }

    [Fact]
    public void CreateResult_records_cargo_test_failures_as_cargo_errors_without_summaries()
    {
        //Act
        var result = Collect(CargoCommand.Test, "test-05-failing.stdout.txt", "test-05-failing.stderr.txt", exitCode: 101);

        //Assert
        result.ErrorCount.Should().Be(0);
        result.StandardError.Should().Contain("test failed, to rerun pass");
    }

    [Fact]
    public void OnStandardErrorLine_reports_crate_progress_from_status_lines()
    {
        //Arrange
        var progress = new List<CargoProgress>();

        //Act
        Collect(CargoCommand.Build, null, "build-02-deps.stderr.txt", progress: progress);

        //Assert
        var compiling = progress.Where(p => p.Phase == CargoProgressPhase.Compiling).ToList();
        compiling.Should().HaveCount(29);
        compiling.Should().OnlyContain(p => !string.IsNullOrEmpty(p.CrateName) && !string.IsNullOrEmpty(p.CrateVersion));
        compiling.Should().Contain(p => p.CrateName == "serde_derive" && p.CrateVersion == "1.0.229");
        compiling.Last().CrateName.Should().Be("test_project_02");
        progress.Last().Phase.Should().Be(CargoProgressPhase.Finished);
    }

    [Fact]
    public void OnStandardOutputLine_counts_completed_crates_with_the_expected_total()
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var options = new CargoBuildOptions { ExpectedCrateCount = 4 };

        //Act
        Collect(CargoCommand.Build, "build-05-broken.jsonl", options: options, progress: progress);

        //Assert
        var completed = progress.Where(p => p.Phase == CargoProgressPhase.CrateCompleted).ToList();
        completed.Select(p => p.Completed).Should().ContainInOrder(1, 2, 3, 4);
        completed.Should().OnlyContain(p => p.Total == 4 && p.CrateName.Length > 0);
        completed.Last().Fraction.Should().Be(1.0);
    }

    [Theory]
    [InlineData("    Updating crates.io index", CargoProgressPhase.Updating, null)]
    [InlineData("     Locking 30 packages to latest Rust 1.94.1 compatible versions", CargoProgressPhase.Locking, null)]
    [InlineData("  Downloaded serde v1.0.228", CargoProgressPhase.Downloading, "serde")]
    [InlineData("    Checking textkit v0.1.0 (/work/x)", CargoProgressPhase.Checking, "textkit")]
    [InlineData(" Documenting textkit v0.1.0 (/work/x)", CargoProgressPhase.Documenting, "textkit")]
    [InlineData("     Running unittests src/lib.rs (target/debug/deps/textkit-1)", CargoProgressPhase.Running, null)]
    [InlineData("   Doc-tests textkit", CargoProgressPhase.Running, "textkit")]
    [InlineData("    Finished `dev` profile [unoptimized + debuginfo] target(s) in 0.17s", CargoProgressPhase.Finished, null)]
    [InlineData("warning: unused manifest key: package.colour", CargoProgressPhase.Warning, null)]
    [InlineData("error: no matching package named `nope` found", CargoProgressPhase.Error, null)]
    public void OnStandardErrorLine_maps_status_lines_to_phases(string line, CargoProgressPhase phase, string crateName)
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var collector = new CargoOutputCollector(CargoCommand.Build, null, progress.Add);

        //Act
        collector.OnStandardErrorLine(line);
        collector.CreateResult(0, false, false, TimeSpan.Zero, "cargo build", line, "/work");

        //Assert
        progress.Should().ContainSingle();
        progress[0].Phase.Should().Be(phase);
        progress[0].CrateName.Should().Be(crateName);
        progress[0].Message.Should().Be(line.Trim());
    }

    [Fact]
    public void CreateResult_turns_cargo_errors_with_causes_into_one_diagnostic()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);
        collector.OnStandardErrorLine("error: failed to load manifest for workspace member `/work/crates/a`");
        collector.OnStandardErrorLine(string.Empty);
        collector.OnStandardErrorLine("Caused by:");
        collector.OnStandardErrorLine("  failed to read `/work/crates/a/Cargo.toml`");
        collector.OnStandardErrorLine("warning: unused manifest key: package.colour");

        //Act
        var result = collector.CreateResult(101, false, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        var error = result.Errors.Should().ContainSingle().Which;
        error.Source.Should().Be(DiagnosticOrigin.Cargo);
        error.Message.Should().Be("failed to load manifest for workspace member `/work/crates/a`");
        error.Rendered.Should().EndWith("failed to read `/work/crates/a/Cargo.toml`");
        error.Rendered.Should().NotContain("unused manifest key");
        result.Warnings.Should().ContainSingle().Which.Source.Should().Be(DiagnosticOrigin.Cargo);
    }

    [Fact]
    public void CreateResult_lists_compiler_errors_before_cargo_errors()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);
        collector.OnStandardErrorLine("error: some cargo-level problem");
        foreach (var line in FixtureLines("build-05-broken.jsonl"))
        {
            collector.OnStandardOutputLine(line);
        }

        //Act
        var result = collector.CreateResult(101, false, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        result.Errors.Should().HaveCount(2);
        result.Errors[0].Source.Should().Be(DiagnosticOrigin.Rustc);
        result.Errors[1].Source.Should().Be(DiagnosticOrigin.Cargo);
    }

    [Fact]
    public void CreateResult_ignores_rustc_summary_messages()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);
        collector.OnStandardOutputLine("{\"reason\":\"compiler-message\",\"package_id\":\"path+file:///work/a#0.1.0\",\"target\":{\"kind\":[\"bin\"],\"name\":\"a\"},\"message\":{\"rendered\":\"error: aborting due to 2 previous errors\\n\\n\",\"children\":[],\"level\":\"error\",\"message\":\"aborting due to 2 previous errors\",\"spans\":[],\"code\":null}}");
        collector.OnStandardOutputLine("{\"reason\":\"compiler-message\",\"package_id\":\"path+file:///work/a#0.1.0\",\"target\":{\"kind\":[\"bin\"],\"name\":\"a\"},\"message\":{\"rendered\":\"warning: 3 warnings emitted\\n\\n\",\"children\":[],\"level\":\"warning\",\"message\":\"3 warnings emitted\",\"spans\":[],\"code\":null}}");

        //Act
        var result = collector.CreateResult(101, false, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        result.Diagnostics.Should().HaveCount(2);
        result.ErrorCount.Should().Be(0);
        result.WarningCount.Should().Be(0);
    }

    [Fact]
    public void CreateResult_treats_build_script_compiler_messages_as_build_script_source()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);
        collector.OnStandardOutputLine("{\"reason\":\"compiler-message\",\"package_id\":\"path+file:///work/a#0.1.0\",\"target\":{\"kind\":[\"custom-build\"],\"name\":\"build-script-build\"},\"message\":{\"rendered\":\"warning: unused variable: `x`\\n\",\"children\":[],\"level\":\"warning\",\"message\":\"unused variable: `x`\",\"spans\":[{\"file_name\":\"build.rs\",\"line_start\":2,\"line_end\":2,\"column_start\":9,\"column_end\":10,\"is_primary\":true,\"label\":\"help: prefix it\"}],\"code\":{\"code\":\"unused_variables\",\"explanation\":null}}}");

        //Act
        var result = collector.CreateResult(0, false, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        var warning = result.Warnings.Should().ContainSingle().Which;
        warning.Source.Should().Be(DiagnosticOrigin.BuildScript);
        warning.TargetName.Should().Be("build-script-build");
        warning.PrimarySpan.Label.Should().Be("help: prefix it");
        result.BuildScriptWarnings.Should().BeEmpty();
    }

    [Fact]
    public void CreateResult_turns_rustfmt_diffs_into_warnings()
    {
        //Act
        var result = Collect(CargoCommand.Fmt, "fmt-10-check.stdout.txt", exitCode: 1);

        //Assert
        result.Success.Should().BeFalse();
        result.Warnings.Should().HaveCount(3);
        result.Warnings.Should().OnlyContain(w => w.Code == "rustfmt" && w.Source == DiagnosticOrigin.Cargo);
        result.Warnings[0].PrimarySpan.FileName.Should().Be("/work/test_project_10/src/main.rs");
        result.Warnings[0].PrimarySpan.LineStart.Should().Be(54);
        result.Warnings[0].Rendered.Should().StartWith("Diff in /work/test_project_10/src/main.rs:54:");
        result.Warnings[0].Rendered.Should().NotContain("\u001b");
    }

    [Fact]
    public void CreateResult_marks_timeouts_and_cancellation_as_failures()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);

        //Act
        var timedOut = collector.CreateResult(-1, true, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");
        var cancelled = collector.CreateResult(-1, false, true, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        timedOut.Success.Should().BeFalse();
        timedOut.TimedOut.Should().BeTrue();
        timedOut.ToString().Should().Contain("timed out");
        cancelled.Success.Should().BeFalse();
        cancelled.WasCancelled.Should().BeTrue();
    }

    [Fact]
    public void OnStandardOutputLine_treats_invalid_json_as_text()
    {
        //Arrange
        var collector = new CargoOutputCollector(CargoCommand.Build, null, null);
        collector.OnStandardOutputLine("{not json");
        collector.OnStandardOutputLine("{\"no_reason\":true}");

        //Act
        var result = collector.CreateResult(0, false, false, TimeSpan.Zero, "cargo build", string.Empty, "/work");

        //Assert
        result.StandardOutput.Should().Be("{not json\n{\"no_reason\":true}");
        result.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void StripAnsi_removes_colour_codes()
        => CargoOutputCollector.StripAnsi("\u001b[31m-    if x {\u001b(B\u001b[m").Should().Be("-    if x {");

    private static string Artifact(string name, string kind, bool fresh = false)
        => "{\"reason\":\"compiler-artifact\",\"package_id\":\"path+file:///work/" + name + "#" + name + "@0.1.0\",\"manifest_path\":\"/work/" + name + "/Cargo.toml\","
            + "\"target\":{\"kind\":[\"" + kind + "\"],\"crate_types\":[\"" + kind + "\"],\"name\":\"" + name + "\",\"src_path\":\"/work/" + name + "/src/lib.rs\"},"
            + "\"profile\":{\"opt_level\":\"0\",\"debuginfo\":2,\"debug_assertions\":true,\"overflow_checks\":true,\"test\":false},"
            + "\"features\":[],\"filenames\":[\"/work/target/debug/lib" + name + ".rlib\"],\"executable\":null,\"fresh\":" + (fresh ? "true" : "false") + "}";

    private const string BuildFinished = "{\"reason\":\"build-finished\",\"success\":true}";

    private const string FinishedLine = "    Finished `dev` profile [unoptimized + debuginfo] target(s) in 0.25s";

    [Fact]
    public void Finished_read_before_the_last_artifacts_is_reported_with_the_final_count()
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var collector = new CargoOutputCollector(CargoCommand.Build, new CargoBuildOptions { ExpectedCrateCount = 2 }, progress.Add);

        //Act
        collector.OnStandardErrorLine("   Compiling craft_core v0.1.0 (/work/craft_core)");
        collector.OnStandardErrorLine("   Compiling orbit_tools v0.1.0 (/work/orbit_tools)");
        collector.OnStandardOutputLine(Artifact("craft_core", "lib"));
        collector.OnStandardErrorLine(FinishedLine);
        collector.OnStandardOutputLine(Artifact("orbit_tools", "lib"));
        collector.OnStandardOutputLine(BuildFinished);
        var result = collector.CreateResult(0, false, false, TimeSpan.FromSeconds(1), "cargo build", string.Empty, "/work");

        //Assert
        result.Success.Should().BeTrue();
        progress.Last().Phase.Should().Be(CargoProgressPhase.Finished);
        progress.Last().Completed.Should().Be(2);
        progress.Last().Fraction.Should().Be(1.0);
        progress.Count(p => p.Phase == CargoProgressPhase.Finished).Should().Be(1);
        collector.Completed.Should().Be(2);
    }

    [Fact]
    public void Finished_read_after_build_finished_is_reported_at_once()
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var collector = new CargoOutputCollector(CargoCommand.Build, null, progress.Add);
        collector.OnStandardOutputLine(Artifact("craft_core", "lib"));
        collector.OnStandardOutputLine(BuildFinished);

        //Act
        collector.OnStandardErrorLine(FinishedLine);

        //Assert
        progress.Last().Phase.Should().Be(CargoProgressPhase.Finished);
        progress.Last().Completed.Should().Be(1);
    }

    [Fact]
    public void A_held_Finished_is_reported_before_the_next_standard_error_line()
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var collector = new CargoOutputCollector(CargoCommand.Test, null, progress.Add);

        //Act
        collector.OnStandardErrorLine(FinishedLine);
        collector.OnStandardErrorLine("     Running unittests src/lib.rs (target/debug/deps/textkit-1)");

        //Assert
        progress.Select(p => p.Phase).Should().Equal(CargoProgressPhase.Finished, CargoProgressPhase.Running);
    }

    [Fact]
    public void A_package_with_a_lib_and_a_bin_counts_once()
    {
        //Arrange
        var progress = new List<CargoProgress>();
        var collector = new CargoOutputCollector(CargoCommand.Build, null, progress.Add);

        //Act
        collector.OnStandardOutputLine(Artifact("textkit", "lib"));
        collector.OnStandardOutputLine(Artifact("textkit", "bin"));
        collector.OnStandardOutputLine(Artifact("helper", "lib", fresh: true));

        //Assert
        collector.Completed.Should().Be(2);
        progress.Where(p => p.Phase == CargoProgressPhase.CrateCompleted).Select(p => p.CrateName).Should().Equal("textkit", "helper");
    }
}
