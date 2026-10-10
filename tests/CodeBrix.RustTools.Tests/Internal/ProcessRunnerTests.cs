using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Internal;
using CodeBrix.RustTools.Tests.Helpers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests.Internal;

public class ProcessRunnerTests
{
    private static ProcessRunRequest Shell(string script)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("These process tests use /bin/sh.");
        }

        var request = new ProcessRunRequest("/bin/sh");
        request.Arguments.Add("-c");
        request.Arguments.Add(script);
        return request;
    }

    [Fact]
    public async Task RunAsync_captures_standard_output_of_dotnet()
    {
        //Arrange
        var request = new ProcessRunRequest("dotnet");
        request.Arguments.Add("--version");

        //Act
        var result = await ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        result.ExitCode.Should().Be(0);
        result.Succeeded.Should().BeTrue();
        result.StandardOutput.Should().StartWith("10.");
        result.CommandLine.Should().Be("dotnet --version");
    }

    [Fact]
    public async Task RunAsync_reports_exit_code_and_line_callbacks()
    {
        //Arrange
        var request = Shell("echo out1; echo err1 >&2; echo out2; exit 3");
        var outLines = new ConcurrentQueue<string>();
        var errLines = new ConcurrentQueue<string>();
        request.OnStandardOutputLine = outLines.Enqueue;
        request.OnStandardErrorLine = errLines.Enqueue;

        //Act
        var result = await ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        result.ExitCode.Should().Be(3);
        result.Succeeded.Should().BeFalse();
        outLines.Should().ContainInOrder("out1", "out2");
        errLines.Should().ContainSingle().Which.Should().Be("err1");
        result.StandardOutput.Should().Be("out1\nout2");
        result.StandardError.Should().Be("err1");
    }

    [Fact]
    public async Task RunAsync_uses_working_directory_and_environment()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        var request = Shell("pwd; echo \"$RUSTTOOLS_TEST_VALUE\"; echo \"[${HOME_REMOVED_TEST-unset}]\"");
        request.WorkingDirectory = sandbox.Directory;
        request.Environment["RUSTTOOLS_TEST_VALUE"] = "hello from env";
        request.Environment["HOME_REMOVED_TEST"] = null;

        //Act
        var result = await ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        var lines = result.StandardOutput.Split('\n');
        Path.GetFullPath(lines[0]).Should().Be(Path.GetFullPath(sandbox.Directory));
        lines[1].Should().Be("hello from env");
        lines[2].Should().Be("[unset]");
    }

    [Fact]
    public async Task RunAsync_writes_standard_input()
    {
        //Arrange
        var request = Shell("read line; echo \"got:$line\"");
        request.StandardInput = "piped text\n";

        //Act
        var result = await ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        result.StandardOutput.Should().Be("got:piped text");
    }

    [Fact]
    public async Task RunAsync_kills_the_process_when_the_timeout_elapses()
    {
        //Arrange
        var request = Shell("sleep 30");
        request.Timeout = TimeSpan.FromMilliseconds(500);

        //Act
        var result = await ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        result.TimedOut.Should().BeTrue();
        result.WasCancelled.Should().BeFalse();
        result.ExitCode.Should().Be(-1);
        result.Duration.Should().BeLessThan(TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RunAsync_cancellation_kills_the_entire_process_tree()
    {
        //Arrange
        if (!OperatingSystem.IsLinux())
        {
            Assert.Skip("Uses /proc to verify the grandchild process is gone.");
        }

        var request = Shell("sleep 60 & echo $!; wait");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var childPid = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        request.OnStandardOutputLine = line =>
        {
            if (int.TryParse(line, NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
            {
                childPid.TrySetResult(pid);
                cancellation.Cancel();
            }
        };

        //Act
        Func<Task> act = () => ProcessRunner.RunAsync(request, cancellation.Token);

        //Assert
        await act.Should().ThrowAsync<OperationCanceledException>();
        var pidOfSleep = await childPid.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        var gone = false;
        for (var attempt = 0; attempt < 50 && !gone; attempt++)
        {
            gone = IsDeadOrZombie(pidOfSleep);
            if (!gone)
            {
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
        }

        gone.Should().BeTrue();
    }

    [Fact]
    public async Task RunAsync_returns_WasCancelled_when_not_throwing_on_cancellation()
    {
        //Arrange
        var request = Shell("sleep 30");
        request.ThrowOnCancellation = false;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));

        //Act
        var result = await ProcessRunner.RunAsync(request, cancellation.Token);

        //Assert
        result.WasCancelled.Should().BeTrue();
        result.TimedOut.Should().BeFalse();
        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_throws_RustToolsException_for_a_missing_executable()
    {
        //Arrange
        var request = new ProcessRunRequest(Path.Combine(Path.GetTempPath(), "no-such-tool-" + Guid.NewGuid().ToString("N")));

        //Act
        Func<Task> act = () => ProcessRunner.RunAsync(request, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<RustToolsException>();
    }

    [Fact]
    public void ProcessRunRequest_rejects_a_blank_file_name()
    {
        //Arrange
        Action act = () => _ = new ProcessRunRequest(" ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    private static bool IsDeadOrZombie(int pid)
    {
        var statPath = $"/proc/{pid}/stat";
        try
        {
            if (!File.Exists(statPath))
            {
                return true;
            }

            var stat = File.ReadAllText(statPath);
            var afterName = stat.Substring(stat.LastIndexOf(')') + 2);
            return afterName.StartsWith('Z') || afterName.StartsWith('X');
        }
        catch (IOException)
        {
            return true;
        }
    }
}
