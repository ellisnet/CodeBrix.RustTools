using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// Runs an external process asynchronously with line callbacks, a timeout, and cancellation that
/// kills the entire process tree (cargo starts rustc, build scripts and linkers as children).
/// </summary>
internal static class ProcessRunner
{
    private static readonly TimeSpan DrainWaitAfterKill = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Starts the process described by <paramref name="request"/> and waits for it to exit.
    /// </summary>
    /// <exception cref="RustToolsException">The process could not be started (for example the
    /// executable does not exist); the inner exception has the operating-system detail.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> fired and
    /// <see cref="ProcessRunRequest.ThrowOnCancellation"/> is true. The process tree has already
    /// been killed when this is thrown.</exception>
    public static async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument ?? string.Empty);
        }

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var pair in request.Environment)
        {
            if (pair.Value == null)
            {
                startInfo.Environment.Remove(pair.Key);
            }
            else
            {
                startInfo.Environment[pair.Key] = pair.Value;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var outputLock = new object();
        var commandLine = request.ToCommandLine();

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => HandleLine(e.Data, stdout, request.OnStandardOutputLine, request.CaptureOutput, outputLock);
        process.ErrorDataReceived += (_, e) => HandleLine(e.Data, stderr, request.OnStandardErrorLine, request.CaptureOutput, outputLock);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (!process.Start())
            {
                throw new RustToolsException($"The process '{commandLine}' could not be started.");
            }
        }
        catch (Win32Exception ex)
        {
            throw new RustToolsException($"The process '{commandLine}' could not be started: {ex.Message}", ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new RustToolsException($"The process '{commandLine}' could not be started: {ex.Message}", ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await WriteStandardInputAsync(process, request.StandardInput).ConfigureAwait(false);

        using var timeoutSource = request.Timeout.HasValue && request.Timeout.Value > TimeSpan.Zero
            ? new CancellationTokenSource(request.Timeout.Value)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var timedOut = false;
        var cancelled = false;
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            cancelled = cancellationToken.IsCancellationRequested;
            timedOut = !cancelled;
            KillTree(process);
            await WaitAfterKillAsync(process).ConfigureAwait(false);
        }

        stopwatch.Stop();

        if (cancelled && request.ThrowOnCancellation)
        {
            throw new OperationCanceledException($"The process '{commandLine}' was cancelled and its process tree was killed.", cancellationToken);
        }

        string capturedOut;
        string capturedErr;
        lock (outputLock)
        {
            capturedOut = stdout.ToString();
            capturedErr = stderr.ToString();
        }

        return new ProcessRunResult
        {
            ExitCode = timedOut || cancelled ? -1 : SafeExitCode(process),
            StandardOutput = capturedOut,
            StandardError = capturedErr,
            Duration = stopwatch.Elapsed,
            TimedOut = timedOut,
            WasCancelled = cancelled,
            CommandLine = commandLine,
        };
    }

    private static void HandleLine(string line, StringBuilder buffer, Action<string> callback, bool capture, object outputLock)
    {
        if (line == null)
        {
            return;
        }

        if (capture)
        {
            lock (outputLock)
            {
                if (buffer.Length > 0)
                {
                    buffer.Append('\n');
                }

                buffer.Append(line);
            }
        }

        callback?.Invoke(line);
    }

    private static async Task WriteStandardInputAsync(Process process, string input)
    {
        try
        {
            if (!string.IsNullOrEmpty(input))
            {
                await process.StandardInput.WriteAsync(input).ConfigureAwait(false);
                await process.StandardInput.FlushAsync().ConfigureAwait(false);
            }

            process.StandardInput.Close();
        }
        catch (System.IO.IOException)
        {
            // The child exited (or closed its input) before reading; nothing more to write.
        }
        catch (InvalidOperationException)
        {
            // The process has already exited.
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Exited between the check and the kill.
        }
        catch (Win32Exception)
        {
            // Some descendant could not be killed (already gone or not ours); best effort.
        }
        catch (NotSupportedException)
        {
            // Not supported for this process (remote machine); nothing more we can do.
        }
    }

    private static async Task WaitAfterKillAsync(Process process)
    {
        using var drain = new CancellationTokenSource(DrainWaitAfterKill);
        try
        {
            await process.WaitForExitAsync(drain.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A descendant still holds the output pipes; return what was captured so far.
        }
    }

    private static int SafeExitCode(Process process)
    {
        try
        {
            return process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            return -1;
        }
    }
}
