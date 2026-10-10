using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Building;

public class CargoRunnerTests
{
    private const string ClippyMissing = "error: 'cargo-clippy' is not installed for the toolchain 'stable-x86_64-unknown-linux-gnu'.\nTo install, run `rustup component add clippy`";

    private static string[] Args(CargoCommand command, CargoBuildOptions options = null, string manifestPath = null)
        => CargoRunner.BuildArguments(command, options, manifestPath).ToArray();

    private static string WriteFakeCargo(SampleSandbox sandbox, string standardError, int exitCode)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("The fake cargo is a /bin/sh script.");
        }

        var path = sandbox.WriteFile(
            "fake-bin/cargo",
            "#!/bin/sh\n"
            + "printf '%s\\n' \"$@\" > \"$(dirname \"$0\")/args.txt\"\n"
            + "pwd > \"$(dirname \"$0\")/pwd.txt\"\n"
            + "env > \"$(dirname \"$0\")/env.txt\"\n"
            + "printf '%s\\n' \"" + standardError.Replace("\"", "\\\"").Replace("`", "\\`").Replace("\n", "\\n") + "\" >&2\n"
            + "exit " + exitCode + "\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        return path;
    }

    private static string FakeOutput(string fakeCargo, string fileName)
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(fakeCargo), fileName));

    private static int CountProcessesUsing(string directory)
    {
        var count = 0;
        foreach (var processDirectory in Directory.GetDirectories("/proc"))
        {
            var name = Path.GetFileName(processDirectory);
            if (!name.All(char.IsDigit) || name == Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                continue;
            }

            try
            {
                var commandLine = File.ReadAllText(Path.Combine(processDirectory, "cmdline")).Replace('\0', ' ');
                var cwd = new DirectoryInfo(Path.Combine(processDirectory, "cwd")).LinkTarget ?? string.Empty;
                if (commandLine.Contains(directory, StringComparison.Ordinal) || cwd.StartsWith(directory, StringComparison.Ordinal))
                {
                    count++;
                }
            }
            catch (IOException)
            {
                // The process exited while we looked.
            }
            catch (UnauthorizedAccessException)
            {
                // Not our process.
            }
        }

        return count;
    }

    private static async Task<int> WaitForNoProcessesUsing(string directory)
    {
        var stopwatch = Stopwatch.StartNew();
        int count;
        while ((count = CountProcessesUsing(directory)) > 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }

        return count;
    }

    [Fact]
    public void Options_default_when_null()
        => new CargoRunner().Options.Should().NotBeNull();

    [Theory]
    [InlineData(CargoCommand.Build, "build")]
    [InlineData(CargoCommand.Check, "check")]
    [InlineData(CargoCommand.Test, "test")]
    [InlineData(CargoCommand.Clean, "clean")]
    [InlineData(CargoCommand.Doc, "doc")]
    [InlineData(CargoCommand.Clippy, "clippy")]
    [InlineData(CargoCommand.Fmt, "fmt")]
    [InlineData(CargoCommand.Fetch, "fetch")]
    public void SubcommandName_maps_every_command(CargoCommand command, string expected)
        => CargoRunner.SubcommandName(command).Should().Be(expected);

    [Fact]
    public void BuildArguments_build_defaults_to_json_messages_only()
        => Args(CargoCommand.Build).Should().Equal("build", "--message-format=json");

    [Fact]
    public void BuildArguments_maps_the_release_profile()
        => Args(CargoCommand.Build, new CargoBuildOptions { Profile = CargoProfile.Release }).Should().Equal("build", "--message-format=json", "--release");

    [Fact]
    public void BuildArguments_custom_profile_wins_over_profile()
        => Args(CargoCommand.Check, new CargoBuildOptions { Profile = CargoProfile.Release, CustomProfileName = " dist " })
            .Should().Equal("check", "--message-format=json", "--profile", "dist");

    [Fact]
    public void BuildArguments_maps_features()
    {
        //Arrange
        var options = new CargoBuildOptions { AllFeatures = true, NoDefaultFeatures = true };
        options.Features.Add("json");
        options.Features.Add("case");

        //Act
        var arguments = Args(CargoCommand.Build, options);

        //Assert
        arguments.Should().Equal("build", "--message-format=json", "--features", "json,case", "--all-features", "--no-default-features");
    }

    [Fact]
    public void BuildArguments_maps_package_and_target_selection()
    {
        //Arrange
        var options = new CargoBuildOptions { AllTargets = true };
        options.Packages.Add("core");
        options.Packages.Add("cli");
        options.Bins.Add("unitconv");
        options.Examples.Add("basic_stats");

        //Act
        var arguments = Args(CargoCommand.Clippy, options);

        //Assert
        arguments.Should().Equal("clippy", "--message-format=json", "-p", "core", "-p", "cli", "--bin", "unitconv", "--example", "basic_stats", "--all-targets");
    }

    [Fact]
    public void BuildArguments_maps_target_lock_network_and_jobs_flags()
        => Args(CargoCommand.Build, new CargoBuildOptions { TargetTriple = "wasm32-unknown-unknown", Locked = true, Offline = true, Frozen = true, Jobs = 4 })
            .Should().Equal("build", "--message-format=json", "--target", "wasm32-unknown-unknown", "--locked", "--offline", "--frozen", "-j", "4");

    [Fact]
    public void BuildArguments_adds_manifest_path_and_verbose()
        => Args(CargoCommand.Build, new CargoBuildOptions { Verbose = true }, "/src/app/Cargo.toml")
            .Should().Equal("build", "--message-format=json", "--manifest-path", "/src/app/Cargo.toml", "-v");

    [Fact]
    public void BuildArguments_test_adds_no_fail_fast_and_the_filter_before_extra_arguments()
    {
        //Arrange
        var options = new CargoBuildOptions { NoFailFast = true, TestNameFilter = "palindrome" };
        options.ExtraArguments.Add("--");
        options.ExtraArguments.Add("--test-threads=1");

        //Act
        var arguments = Args(CargoCommand.Test, options);

        //Assert
        arguments.Should().Equal("test", "--message-format=json", "--no-fail-fast", "palindrome", "--", "--test-threads=1");
    }

    [Fact]
    public void BuildArguments_doc_adds_no_deps_by_default_and_never_all_targets()
        => Args(CargoCommand.Doc, new CargoBuildOptions { AllTargets = true }).Should().Equal("doc", "--message-format=json", "--no-deps");

    [Fact]
    public void BuildArguments_doc_can_include_dependencies()
        => Args(CargoCommand.Doc, new CargoBuildOptions { NoDeps = false }).Should().Equal("doc", "--message-format=json");

    [Fact]
    public void BuildArguments_clean_keeps_only_the_flags_clean_understands()
    {
        //Arrange
        var options = new CargoBuildOptions { Profile = CargoProfile.Release, AllFeatures = true, AllTargets = true, Jobs = 2, TargetTriple = "x86_64-unknown-linux-gnu", Offline = true };
        options.Packages.Add("core");
        options.Bins.Add("x");

        //Act
        var arguments = Args(CargoCommand.Clean, options);

        //Assert
        arguments.Should().Equal("clean", "--release", "-p", "core", "--target", "x86_64-unknown-linux-gnu", "--offline");
    }

    [Fact]
    public void BuildArguments_fetch_keeps_only_the_flags_fetch_understands()
    {
        //Arrange
        var options = new CargoBuildOptions { Profile = CargoProfile.Release, AllFeatures = true, NoDefaultFeatures = true, AllTargets = true, Jobs = 2, TargetTriple = "aarch64-unknown-linux-gnu", Locked = true, Offline = true, Frozen = true, Verbose = true };
        options.Features.Add("json");
        options.Packages.Add("core");
        options.Bins.Add("x");
        options.ExtraArguments.Add("--quiet");

        //Act
        var arguments = Args(CargoCommand.Fetch, options, "/p/Cargo.toml");

        //Assert
        arguments.Should().Equal("fetch", "--manifest-path", "/p/Cargo.toml", "-v", "--target", "aarch64-unknown-linux-gnu", "--locked", "--offline", "--frozen", "--quiet");
    }

    [Fact]
    public void BuildArguments_fetch_defaults_to_every_platform()
        => Args(CargoCommand.Fetch).Should().Equal("fetch");

    [Fact]
    public void BuildArguments_fmt_checks_by_default_and_ignores_build_flags()
    {
        //Arrange
        var options = new CargoBuildOptions { Profile = CargoProfile.Release, AllFeatures = true, Offline = true, Jobs = 2, TargetTriple = "x" };
        options.Packages.Add("cli");

        //Act
        var arguments = Args(CargoCommand.Fmt, options);

        //Assert
        arguments.Should().Equal("fmt", "-p", "cli", "--check");
    }

    [Fact]
    public void BuildArguments_fmt_can_rewrite_files()
        => Args(CargoCommand.Fmt, new CargoBuildOptions { CheckOnly = false }).Should().Equal("fmt");

    [Fact]
    public void BuildArguments_skips_blank_entries()
    {
        //Arrange
        var options = new CargoBuildOptions { TargetTriple = " ", CustomProfileName = string.Empty };
        options.Features.Add(" ");
        options.Packages.Add(string.Empty);

        //Act
        var arguments = Args(CargoCommand.Build, options);

        //Assert
        arguments.Should().Equal("build", "--message-format=json");
    }

    [Fact]
    public void BuildArguments_rejects_zero_jobs()
    {
        //Act
        var act = () => CargoRunner.BuildArguments(CargoCommand.Build, new CargoBuildOptions { Jobs = 0 });

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildEnvironment_disables_auto_install_and_colour()
    {
        //Act
        var environment = CargoRunner.BuildEnvironment(new RustWorkbenchOptions { CargoHome = "/opt/cargo" }, null);

        //Assert
        environment["RUSTUP_AUTO_INSTALL"].Should().Be("0");
        environment["CARGO_TERM_COLOR"].Should().Be("never");
        environment["CARGO_HOME"].Should().Be("/opt/cargo");
    }

    [Fact]
    public void BuildEnvironment_command_variables_override_workbench_variables()
    {
        //Arrange
        var workbench = new RustWorkbenchOptions();
        workbench.Environment["CARGO_TERM_COLOR"] = "always";
        workbench.Environment["SHARED"] = "workbench";
        var options = new CargoBuildOptions();
        options.Environment["SHARED"] = "command";
        options.Environment["REMOVED"] = null;

        //Act
        var environment = CargoRunner.BuildEnvironment(workbench, options);

        //Assert
        environment["CARGO_TERM_COLOR"].Should().Be("always");
        environment["SHARED"].Should().Be("command");
        environment.Should().ContainKey("REMOVED").WhoseValue.Should().BeNull();
    }

    [Theory]
    [InlineData(CargoCommand.Clippy, "error: no such command: `clippy`\n\nhelp: view all installed commands with `cargo --list`")]
    [InlineData(CargoCommand.Clippy, ClippyMissing)]
    [InlineData(CargoCommand.Clippy, "error: 'clippy-driver' is not installed for the toolchain 'nightly'")]
    [InlineData(CargoCommand.Fmt, "error: no such command: `fmt`")]
    [InlineData(CargoCommand.Fmt, "error: 'cargo-fmt' is not installed for the toolchain 'stable-x86_64-unknown-linux-gnu'.")]
    [InlineData(CargoCommand.Fmt, "error: 'rustfmt' is not installed for the toolchain '1.94.1-x86_64-unknown-linux-gnu'.")]
    public void IsComponentMissing_recognises_missing_component_messages(CargoCommand command, string standardError)
        => CargoRunner.IsComponentMissing(command, standardError).Should().BeTrue();

    [Theory]
    [InlineData(CargoCommand.Build, ClippyMissing)]
    [InlineData(CargoCommand.Clippy, "error: could not compile `x` (bin \"x\") due to 2 previous errors")]
    [InlineData(CargoCommand.Fmt, "")]
    [InlineData(CargoCommand.Fmt, ClippyMissing)]
    public void IsComponentMissing_is_false_for_other_failures(CargoCommand command, string standardError)
        => CargoRunner.IsComponentMissing(command, standardError).Should().BeFalse();

    [Fact]
    public async Task RunAsync_rejects_a_blank_project()
    {
        //Act
        Func<Task> act = () => new CargoRunner().BuildAsync(" ", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_throws_for_a_missing_folder()
    {
        //Act
        Func<Task> act = () => new CargoRunner().BuildAsync(Path.Combine(AppContext.BaseDirectory, "no-such-project"), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }

    [Fact]
    public async Task RunAsync_throws_ToolchainNotFoundException_when_cargo_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var runner = new CargoRunner(new RustWorkbenchOptions { CargoPath = Path.Combine(sandbox.Directory, "no-cargo-here") });

        //Act
        Func<Task> act = () => runner.BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which.Message.Should().Contain("CargoPath");
    }

    [Fact]
    public async Task ClippyAsync_throws_with_install_guidance_when_clippy_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakeCargo = WriteFakeCargo(sandbox, ClippyMissing, 1);
        var runner = new CargoRunner(new RustWorkbenchOptions { CargoPath = fakeCargo });

        //Act
        Func<Task> act = () => runner.ClippyAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<CargoCommandException>()).Which;
        exception.ExitCode.Should().Be(1);
        exception.StandardError.Should().Contain("cargo-clippy");
        exception.Guidance.Should().ContainSingle().Which.Topic.Should().Be(InstallGuidanceTopic.InstallComponents);
        exception.Guidance[0].Commands.Should().Contain(c => c.Contains("clippy"));
    }

    [Fact]
    public async Task FmtAsync_throws_with_install_guidance_when_rustfmt_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakeCargo = WriteFakeCargo(sandbox, "error: 'cargo-fmt' is not installed for the toolchain 'stable-x86_64-unknown-linux-gnu'.", 1);
        var runner = new CargoRunner(new RustWorkbenchOptions { CargoPath = fakeCargo });

        //Act
        Func<Task> act = () => runner.FmtAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<CargoCommandException>()).Which.Guidance[0].Commands.Should().Contain(c => c.Contains("rustfmt"));
    }

    [Fact]
    public async Task RunAsync_passes_arguments_folder_and_environment_to_cargo()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var fakeCargo = WriteFakeCargo(sandbox, "error: deliberate failure", 101);
        var project = sandbox.CreateDirectory("project");
        sandbox.WriteFile("project/Cargo.toml", "[package]\nname = \"p\"\n");
        var workbench = new RustWorkbenchOptions { CargoPath = fakeCargo };
        workbench.Environment["FROM_WORKBENCH"] = "yes";
        var options = new CargoBuildOptions { Offline = true, WorkingDirectory = sandbox.Directory };
        options.Environment["FROM_COMMAND"] = "also";

        //Act
        var result = await new CargoRunner(workbench).BuildAsync(project, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(101);
        result.Errors.Should().ContainSingle().Which.Message.Should().Be("deliberate failure");
        FakeOutput(fakeCargo, "args.txt").Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Should().Equal("build", "--message-format=json", "--manifest-path", Path.Combine(project, "Cargo.toml"), "--offline");
        FakeOutput(fakeCargo, "pwd.txt").Trim().Should().Be(sandbox.Directory);
        var environment = FakeOutput(fakeCargo, "env.txt");
        environment.Should().Contain("RUSTUP_AUTO_INSTALL=0");
        environment.Should().Contain("CARGO_TERM_COLOR=never");
        environment.Should().Contain("FROM_WORKBENCH=yes");
        environment.Should().Contain("FROM_COMMAND=also");
        result.WorkingDirectory.Should().Be(sandbox.Directory);
        result.CommandLine.Should().StartWith(fakeCargo + " build");
    }

    [Fact]
    public async Task BuildAsync_builds_the_lib_and_bin_of_sample_05()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.ExitCode.Should().Be(0);
        result.ErrorCount.Should().Be(0);
        var bin = result.RunnableArtifacts.Should().ContainSingle().Which;
        bin.TargetName.Should().Be("textkit");
        bin.Kinds.Should().Contain("bin");
        File.Exists(bin.Executable).Should().BeTrue();
        var lib = result.Artifacts.Single(a => a.PackageName == "test_project_05" && a.Kinds.Contains("lib"));
        lib.Executable.Should().BeNull();
        lib.Filenames.Should().NotBeEmpty();
        result.Artifacts.Select(a => a.PackageName).Should().Contain("licensed_by_file");
        result.Command.Should().Be(CargoCommand.Build);
        result.Duration.Should().BeGreaterThan(TimeSpan.Zero);
        result.ToString().Should().Contain("cargo build succeeded");
    }

    [Fact]
    public async Task BuildAsync_accepts_a_manifest_path_and_raises_the_progress_event()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(6);
        var runner = new CargoRunner();
        var events = new ConcurrentQueue<CargoProgress>();
        runner.Progress += (_, p) => events.Enqueue(p);

        //Act
        var result = await runner.BuildAsync(Path.Combine(sandbox.Directory, "Cargo.toml"), cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.RunnableArtifacts.Select(a => a.TargetName).Should().BeEquivalentTo(new[] { "buildinfo", "crc32sum" });
        result.RunnableArtifacts.Should().OnlyContain(a => File.Exists(a.Executable));
        result.Artifacts.Should().Contain(a => a.IsBuildScript);
        result.CommandLine.Should().Contain("--manifest-path");
        events.Should().Contain(p => p.Phase == CargoProgressPhase.Compiling && p.CrateName == "test_project_06");
        events.Should().Contain(p => p.Phase == CargoProgressPhase.Finished);
    }

    [Fact]
    public async Task BuildAsync_reports_the_warnings_of_sample_10()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(10);

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.WarningCount.Should().BeGreaterThanOrEqualTo(20);
        result.Warnings.Should().OnlyContain(w => w.Code != null || !string.IsNullOrEmpty(w.Rendered));
        result.BuildScriptWarnings.Should().NotBeEmpty();
        result.BuildScriptWarnings[0].Message.Should().Contain("intentional build-script warning");
        result.Warnings.Where(w => w.Source == DiagnosticOrigin.Rustc)
            .Should().OnlyContain(w => w.PrimarySpan != null && w.PrimarySpan.FileName.Length > 0 && w.PrimarySpan.LineStart > 0);
    }

    [Fact]
    public async Task BuildAsync_builds_every_binary_member_of_workspace_04()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(4);

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, new CargoBuildOptions { Locked = true }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.RunnableArtifacts.Select(a => a.PackageName).Should().BeEquivalentTo(new[] { "unitconv_cli", "unitconv_report" });
        result.Artifacts.Should().Contain(a => a.PackageName == "unitconv_core" && a.Kinds.Contains("lib"));
    }

    [Fact]
    public async Task BuildAsync_with_a_package_selection_builds_only_that_member()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(4);
        var options = new CargoBuildOptions { Locked = true };
        options.Packages.Add("unitconv_cli");

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.RunnableArtifacts.Should().ContainSingle().Which.TargetName.Should().Be("unitconv");
        result.Artifacts.Should().NotContain(a => a.PackageName == "unitconv_report");
    }

    [Fact]
    public async Task BuildAsync_builds_real_dependencies_offline_and_reports_progress()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(2);
        var runner = new CargoRunner();
        var fetch = await runner.FetchAsync(sandbox.Directory, new CargoBuildOptions { Locked = true }, cancellationToken: TestContext.Current.CancellationToken);
        var progress = new ConcurrentQueue<CargoProgress>();
        var reporter = new SynchronousProgress(progress.Enqueue);
        var options = new CargoBuildOptions { Offline = true, Locked = true, ExpectedCrateCount = 30 };

        //Act
        var result = await runner.BuildAsync(sandbox.Directory, options, reporter, TestContext.Current.CancellationToken);

        //Assert
        fetch.Success.Should().BeTrue(fetch.StandardError);
        result.Success.Should().BeTrue(result.StandardError);
        result.Artifacts.Select(a => a.PackageName).Should().Contain(new[] { "serde", "clap", "anyhow", "csv" });
        var completed = progress.Where(p => p.Phase == CargoProgressPhase.CrateCompleted).Select(p => p.Completed).ToList();
        completed.Should().BeInAscendingOrder();
        completed.Last().Should().BeGreaterThan(10);
        progress.Where(p => p.Phase == CargoProgressPhase.Compiling).Should().OnlyContain(p => !string.IsNullOrEmpty(p.CrateName));
        progress.Should().OnlyContain(p => p.Total == 30);
    }

    [Fact]
    public async Task CheckAsync_checks_sample_05_without_executables()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);

        //Act
        var result = await new CargoRunner().CheckAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.Command.Should().Be(CargoCommand.Check);
        result.Artifacts.Should().NotBeEmpty();
        result.RunnableArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task TestAsync_runs_the_tests_of_sample_05()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);

        //Act
        var result = await new CargoRunner().TestAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.TestsPassed.Should().BeGreaterThan(0);
        result.TestsFailed.Should().Be(0);
        result.AllTestsPassed.Should().BeTrue();
        result.TestBinaryCount.Should().BeGreaterThanOrEqualTo(4);
        result.Tests.Should().Contain(t => t.Name == "tests::palindromes" && t.Passed);
        result.Artifacts.Should().Contain(a => a.Profile.Test);
    }

    [Fact]
    public async Task TestAsync_with_all_features_runs_the_json_tests()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var options = new CargoBuildOptions { AllFeatures = true, Locked = true };

        //Act
        var result = await new CargoRunner().TestAsync(sandbox.Directory, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.TestsFailed.Should().Be(0);
        result.TestsPassed.Should().BeGreaterThan(0);
        result.Artifacts.Should().Contain(a => a.PackageName == "serde_json");
    }

    [Fact]
    public async Task TestAsync_reports_a_failing_test_with_its_output()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        File.AppendAllText(
            Path.Combine(sandbox.Directory, "tests", "library_api.rs"),
            "\n#[test]\nfn deliberately_fails() {\n    assert_eq!(1 + 1, 3, \"broken on purpose\");\n}\n");

        //Act
        var result = await new CargoRunner().TestAsync(sandbox.Directory, new CargoBuildOptions { NoFailFast = true }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.TestsFailed.Should().Be(1);
        result.FailedTestNames.Should().ContainSingle().Which.Should().Be("deliberately_fails");
        result.Tests.Single(t => t.Failed).Output.Should().Contain("broken on purpose");
        result.TestsPassed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task TestAsync_applies_the_name_filter()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);

        //Act
        var result = await new CargoRunner().TestAsync(sandbox.Directory, new CargoBuildOptions { TestNameFilter = "palindromes" }, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.TestsPassed.Should().Be(1);
        result.TestsFilteredOut.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task CleanAsync_removes_the_target_folder()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(6);
        var runner = new CargoRunner();
        (await runner.BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken)).Success.Should().BeTrue();
        var target = Path.Combine(sandbox.Directory, "target");
        var existedBefore = Directory.Exists(target);

        //Act
        var result = await runner.CleanAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        existedBefore.Should().BeTrue();
        result.Success.Should().BeTrue(result.StandardError);
        Directory.Exists(target).Should().BeFalse();
    }

    [Fact]
    public async Task DocAsync_documents_sample_05()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);

        //Act
        var result = await new CargoRunner().DocAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        File.Exists(Path.Combine(sandbox.Directory, "target", "doc", "textkit", "index.html")).Should().BeTrue();
    }

    [Fact]
    public async Task BuildAsync_reports_the_error_of_a_broken_source_file()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        File.AppendAllText(Path.Combine(sandbox.Directory, "src", "main.rs"), "\nfn broken( {\n");

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeFalse();
        result.ExitCode.Should().NotBe(0);
        result.ErrorCount.Should().BeGreaterThanOrEqualTo(1);
        result.Errors[0].Source.Should().Be(DiagnosticOrigin.Rustc);
        result.Errors[0].PrimarySpan.FileName.Should().EndWith("main.rs");
        result.Errors[0].PrimarySpan.LineStart.Should().BeGreaterThan(0);
        result.RunnableArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task ClippyAsync_reports_clippy_lints_for_sample_10()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(10);
        CargoCommandResult result;

        //Act
        try
        {
            result = await new CargoRunner().ClippyAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (CargoCommandException ex) when (ex.Guidance.Count > 0)
        {
            Assert.Skip("clippy is not installed on this machine: " + ex.Message);
            return;
        }

        //Assert
        result.Diagnostics.Should().Contain(d => d.Code != null && d.Code.StartsWith("clippy::"));
        result.WarningCount.Should().BeGreaterThanOrEqualTo(20);
    }

    [Fact]
    public async Task FmtAsync_reports_unformatted_files_without_changing_them()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(10);
        var mainRs = Path.Combine(sandbox.Directory, "src", "main.rs");
        var before = File.ReadAllText(mainRs);
        CargoCommandResult result;

        //Act
        try
        {
            result = await new CargoRunner().FmtAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);
        }
        catch (CargoCommandException ex) when (ex.Guidance.Count > 0)
        {
            Assert.Skip("rustfmt is not installed on this machine: " + ex.Message);
            return;
        }

        //Assert
        result.Success.Should().BeFalse();
        result.Warnings.Should().NotBeEmpty();
        result.Warnings.Should().OnlyContain(w => w.Code == "rustfmt" && w.PrimarySpan.FileName.EndsWith(".rs"));
        File.ReadAllText(mainRs).Should().Be(before);
    }

    [Fact]
    public async Task RunAsync_cancellation_kills_the_cargo_process_tree()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Uses /proc to verify that no cargo or rustc process is left behind.");
        }

        using var sandbox = SampleSandbox.FromSample(2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        //Act
        Func<Task> act = () => new CargoRunner().BuildAsync(sandbox.Directory, new CargoBuildOptions { Locked = true }, cancellationToken: cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        (await WaitForNoProcessesUsing(sandbox.Directory)).Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_can_return_a_cancelled_result_instead_of_throwing()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(2);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        var options = new CargoBuildOptions { Locked = true, ThrowOnCancellation = false };

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, options, cancellationToken: cancellation.Token);

        //Assert
        result.WasCancelled.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
    }

    [Fact]
    public async Task RunAsync_timeout_kills_cargo_and_sets_TimedOut()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(2);
        var options = new CargoBuildOptions { Locked = true, Timeout = TimeSpan.FromMilliseconds(300) };

        //Act
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, options, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.TimedOut.Should().BeTrue();
        result.Success.Should().BeFalse();
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(15));
        if (OperatingSystem.IsLinux())
        {
            (await WaitForNoProcessesUsing(sandbox.Directory)).Should().Be(0);
        }
    }

    private sealed class SynchronousProgress : IProgress<CargoProgress>
    {
        private readonly Action<CargoProgress> _handler;

        public SynchronousProgress(Action<CargoProgress> handler)
        {
            _handler = handler;
        }

        public void Report(CargoProgress value) => _handler(value);
    }
}
