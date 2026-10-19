using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Wrapper;

/// <summary>
/// Runs the dotnet CLI for the live wrapper tests (build / pack / run of generated projects):
/// no build servers, no node reuse, no telemetry, the test host's MSBuild variables removed.
/// </summary>
internal static class DotnetCli
{
    private static readonly Lazy<string> Executable = new Lazy<string>(Find);

    /// <summary>The dotnet executable, or <c>null</c>.</summary>
    public static string Path => Executable.Value;

    /// <summary>Skips the test when no dotnet executable is found.</summary>
    public static void SkipIfMissing()
    {
        if (Path == null)
        {
            Assert.Skip("The dotnet CLI was not found (DOTNET_HOST_PATH, DOTNET_ROOT or PATH); the live wrapper tests build generated projects with it.");
        }
    }

    /// <summary>Runs dotnet with <paramref name="arguments"/> in <paramref name="workingDirectory"/>.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string workingDirectory, TimeSpan timeout, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase) || k.StartsWith("VSTEST", StringComparison.OrdinalIgnoreCase) || k.StartsWith("TESTINGPLATFORM", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            start.Environment.Remove(key);
        }

        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";

        using var process = new Process { StartInfo = start };
        var output = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) { lock (output) { output.AppendLine(e.Data); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) { lock (output) { output.AppendLine(e.Data); } } };
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancel.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cancel.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("dotnet " + string.Join(" ", arguments) + " did not finish within " + timeout + ":\n" + output);
        }

        process.WaitForExit();
        lock (output)
        {
            return (process.ExitCode, output.ToString());
        }
    }

    /// <summary>The arguments that keep a build self-contained: no build servers, no node reuse.</summary>
    public static IEnumerable<string> BuildFlags() => new[] { "--disable-build-servers", "-nodeReuse:false", "-p:UseSharedCompilation=false" };

    private static string Find()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
        {
            return hostPath;
        }

        var name = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        var root = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(root) && File.Exists(System.IO.Path.Combine(root, name)))
        {
            return System.IO.Path.Combine(root, name);
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(System.IO.Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = System.IO.Path.Combine(folder, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
