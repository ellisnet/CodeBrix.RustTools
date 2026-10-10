using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Running;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Running;

public class BuiltBinaryRunnerTests
{
    private static async Task<BuildArtifact> BuildSingleBinary(SampleSandbox sandbox, string targetName)
    {
        var result = await new CargoRunner().BuildAsync(sandbox.Directory, cancellationToken: TestContext.Current.CancellationToken);
        result.Success.Should().BeTrue(result.StandardError);
        return result.RunnableArtifacts.Single(a => a.TargetName == targetName);
    }

    private static string Shell()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("These tests use /bin/sh.");
        }

        return "/bin/sh";
    }

    private static RunOptions ShellScript(string script)
    {
        var options = new RunOptions();
        options.Arguments.Add("-c");
        options.Arguments.Add(script);
        return options;
    }

    [Fact]
    public void Options_default_when_null()
        => new BuiltBinaryRunner().Options.Should().NotBeNull();

    [Fact]
    public async Task RunAsync_runs_the_buildinfo_binary_of_sample_06()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(6);
        var artifact = await BuildSingleBinary(sandbox, "buildinfo");

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(artifact, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Succeeded.Should().BeTrue(result.StandardError);
        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Contain("version 0.3.1");
        result.StandardOutput.Should().Contain("Earth");
        result.CommandLine.Should().Be(artifact.Executable);
        result.TimedOut.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_passes_arguments_to_the_sample_05_binary()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var artifact = await BuildSingleBinary(sandbox, "textkit");
        var options = new RunOptions();
        options.Arguments.Add("racecar");

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(artifact, options, TestContext.Current.CancellationToken);

        //Assert
        result.Succeeded.Should().BeTrue(result.StandardError);
        result.StandardOutput.Should().Contain("palindrome: true");
        result.CommandLine.Should().EndWith(" racecar");
    }

    [Fact]
    public async Task RunAsync_writes_standard_input_to_the_program()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        var artifact = await BuildSingleBinary(sandbox, "textkit");
        var options = new RunOptions { StandardInput = "Was it a car or a cat I saw" };

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(artifact, options, TestContext.Current.CancellationToken);

        //Assert
        result.Succeeded.Should().BeTrue(result.StandardError);
        result.StandardOutput.Should().Contain("palindrome: true");
        result.StandardOutput.Should().Contain("reversed: \"saw I cat a or car a it Was\"");
    }

    [Fact]
    public async Task RunAsync_times_out_a_built_program_that_sleeps()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(5);
        sandbox.WriteFile("src/bin/sleeper.rs", "fn main() {\n    println!(\"going to sleep\");\n    std::thread::sleep(std::time::Duration::from_secs(60));\n}\n");
        var artifact = await BuildSingleBinary(sandbox, "sleeper");
        var options = new RunOptions { Timeout = TimeSpan.FromMilliseconds(500) };

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(artifact, options, TestContext.Current.CancellationToken);

        //Assert
        result.TimedOut.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(30));
        result.ToString().Should().Contain("timed out");
    }

    [Fact]
    public async Task RunAsync_uses_the_executable_folder_as_the_default_working_directory()
    {
        //Arrange
        var shell = Shell();

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(shell, ShellScript("pwd -P"), TestContext.Current.CancellationToken);

        //Assert
        result.StandardOutput.Trim().Should().Be(new DirectoryInfo(Path.GetDirectoryName(shell)).ResolveLinkTarget(true)?.FullName ?? Path.GetDirectoryName(shell));
    }

    [Fact]
    public async Task RunAsync_applies_working_directory_and_environment()
    {
        //Arrange
        var shell = Shell();
        using var sandbox = SampleSandbox.CreateEmpty();
        var workbench = new RustWorkbenchOptions();
        workbench.Environment["FROM_WORKBENCH"] = "workbench";
        workbench.Environment["OVERRIDDEN"] = "workbench";
        var options = ShellScript("pwd; echo \"$FROM_WORKBENCH $OVERRIDDEN\"");
        options.WorkingDirectory = sandbox.Directory;
        options.Environment["OVERRIDDEN"] = "run";

        //Act
        var result = await new BuiltBinaryRunner(workbench).RunAsync(shell, options, TestContext.Current.CancellationToken);

        //Assert
        result.StandardOutput.Should().Be(sandbox.Directory + "\nworkbench run");
    }

    [Fact]
    public async Task RunAsync_reports_exit_code_and_standard_error()
    {
        //Arrange
        var shell = Shell();

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(shell, ShellScript("echo problem >&2; exit 7"), TestContext.Current.CancellationToken);

        //Assert
        result.ExitCode.Should().Be(7);
        result.Succeeded.Should().BeFalse();
        result.StandardError.Should().Be("problem");
        result.ToString().Should().Contain("exit code 7");
    }

    [Fact]
    public async Task RunAsync_streams_lines_to_callbacks()
    {
        //Arrange
        var shell = Shell();
        var lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var options = ShellScript("echo one; echo two");
        options.CaptureOutput = false;
        options.OnStandardOutputLine = lines.Enqueue;

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(shell, options, TestContext.Current.CancellationToken);

        //Assert
        lines.Should().ContainInOrder("one", "two");
        result.StandardOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task RunAsync_cancellation_kills_the_program_and_throws()
    {
        //Arrange
        var shell = Shell();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync(shell, ShellScript("sleep 60"), cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task RunAsync_can_return_a_cancelled_result()
    {
        //Arrange
        var shell = Shell();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        var options = ShellScript("sleep 60");
        options.ThrowOnCancellation = false;

        //Act
        var result = await new BuiltBinaryRunner().RunAsync(shell, options, cancellation.Token);

        //Assert
        result.WasCancelled.Should().BeTrue();
        result.Succeeded.Should().BeFalse();
        result.ToString().Should().Contain("cancelled");
    }

    [Fact]
    public async Task RunAsync_rejects_an_artifact_without_an_executable()
    {
        //Arrange
        var library = new BuildArtifact { PackageName = "textkit", TargetName = "textkit", Kinds = new[] { "lib" } };

        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync(library, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RustToolsException>()).Which.Message.Should().Contain("no executable");
    }

    [Fact]
    public async Task RunAsync_rejects_a_null_artifact()
    {
        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync((BuildArtifact)null, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_rejects_a_blank_path()
    {
        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync(" ", cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RunAsync_rejects_a_missing_executable()
    {
        //Arrange
        var artifact = new BuildArtifact { TargetName = "gone", Executable = Path.Combine(AppContext.BaseDirectory, "no-such-binary") };

        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync(artifact, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        (await act.Should().ThrowAsync<RustToolsException>()).Which.Message.Should().Contain("does not exist");
    }

    [Fact]
    public async Task RunAsync_rejects_a_missing_working_directory()
    {
        //Arrange
        var shell = Shell();
        var options = ShellScript("true");
        options.WorkingDirectory = Path.Combine(AppContext.BaseDirectory, "no-such-folder");

        //Act
        Func<Task> act = () => new BuiltBinaryRunner().RunAsync(shell, options, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }
}
