using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Internal;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Runs commands inside a build container and collects standard output, standard error and the
/// exit code - either buffered (<see cref="RunAsync"/>) or line by line as the command writes
/// (<see cref="RunStreamingAsync"/>, the form long-running cargo commands use).
/// </summary>
internal static class ContainerCommandRunner
{
    private const int BufferSize = 16 * 1024;

    /// <summary>
    /// Runs a command to completion and returns its buffered output.
    /// </summary>
    public static async Task<ContainerCommandResult> RunAsync(
        DockerClient client,
        string containerId,
        IReadOnlyList<string> command,
        string workingDirectory,
        IReadOnlyList<string> environment,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        var stopwatch = Stopwatch.StartNew();
        var result = await client.Containers.ExecAsync(containerId, command, user: null, workingDirectory, environment, cancellationToken).ConfigureAwait(false);
        return new ContainerCommandResult
        {
            Command = command.ToArray(),
            ExitCode = result.ExitCode,
            StandardOutput = result.Stdout ?? string.Empty,
            StandardError = result.Stderr ?? string.Empty,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>
    /// Runs a command and reports each line of standard output and standard error as it
    /// arrives (no terminal, so the two streams stay apart), then returns the collected output
    /// and the exit code.
    /// </summary>
    public static async Task<ContainerCommandResult> RunStreamingAsync(
        DockerClient client,
        string containerId,
        IReadOnlyList<string> command,
        string workingDirectory,
        IReadOnlyList<string> environment,
        Action<string> onStandardOutputLine,
        Action<string> onStandardErrorLine,
        CancellationToken cancellationToken)
    {
        ValidateCommand(command);
        var stopwatch = Stopwatch.StartNew();
        var spec = new ExecSpec
        {
            Command = command.ToArray(),
            AttachStdin = false,
            AttachStdout = true,
            AttachStderr = true,
            Tty = false,
            WorkingDir = workingDirectory,
        };
        if (environment != null)
        {
            foreach (var variable in environment)
            {
                spec.Env.Add(variable);
            }
        }

        var stdout = new LineSplitter(onStandardOutputLine);
        var stderr = new LineSplitter(onStandardErrorLine);
        await using var stream = await client.Containers.ExecStreamAsync(containerId, spec, cancellationToken).ConfigureAwait(false);
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false);
                if (read.EndOfStream)
                {
                    break;
                }

                var chunk = buffer.AsSpan(0, read.Count);
                if (read.Target == ExecStreamTarget.StandardError)
                {
                    stderr.Append(chunk);
                }
                else
                {
                    stdout.Append(chunk);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        stdout.Complete();
        stderr.Complete();
        var exitCode = await stream.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        return new ContainerCommandResult
        {
            Command = command.ToArray(),
            ExitCode = exitCode,
            StandardOutput = stdout.Text,
            StandardError = stderr.Text,
            Duration = stopwatch.Elapsed,
        };
    }

    /// <summary>
    /// Throws when a command is missing or has an empty first element.
    /// </summary>
    public static void ValidateCommand(IReadOnlyList<string> command)
    {
        if (command == null)
        {
            throw new ArgumentNullException(nameof(command));
        }

        if (command.Count == 0 || string.IsNullOrWhiteSpace(command[0]))
        {
            throw new ArgumentException("The command must name a program to run.", nameof(command));
        }

        if (command.Any(argument => argument == null))
        {
            throw new ArgumentException("Command arguments cannot be null.", nameof(command));
        }
    }
}
