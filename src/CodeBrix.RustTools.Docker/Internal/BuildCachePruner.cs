using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Engine;

namespace CodeBrix.RustTools.Docker.Internal;

/// <summary>
/// The captured outcome of one <c>docker</c> command-line run.
/// </summary>
/// <param name="ExitCode">The process exit code.</param>
/// <param name="StandardOutput">Everything written to standard output.</param>
/// <param name="StandardError">Everything written to standard error.</param>
internal sealed record DockerCliOutput(int ExitCode, string StandardOutput, string StandardError);

/// <summary>
/// What a build-cache prune did.
/// </summary>
/// <param name="Pruned">Whether the prune ran and exited with code 0.</param>
/// <param name="ReclaimedSpace">The reclaimed-space text the CLI printed (for example
/// <c>1.234GB</c>), or <c>null</c>.</param>
/// <param name="ReclaimedBytes">That text as a byte count, or <c>null</c>.</param>
/// <param name="Output">The raw standard output and standard error, or <c>null</c> when the
/// CLI could not be started.</param>
/// <param name="Failure">Why the prune failed, or <c>null</c>.</param>
internal sealed record BuildCachePruneOutcome(bool Pruned, string ReclaimedSpace, long? ReclaimedBytes, string Output, string Failure);

/// <summary>
/// Runs <c>docker builder prune --all --force</c> - the engine-wide BuildKit cache prune - and reads
/// the reclaimed space from its output.
/// </summary>
/// <remarks>
/// The derived images are built by CodeBrix.Docker with <c>docker build</c> (BuildKit) through
/// the docker CLI; the prune goes through the same CLI, with the same executable
/// (<see cref="DockerClientOptions.DockerCliPath"/>) and the same <c>DOCKER_HOST</c> handling,
/// so it acts on the builder that holds that cache. CodeBrix.Docker's own CLI runner is
/// internal to that package, so this mirrors it.
/// </remarks>
internal static partial class BuildCachePruner
{
    /// <summary>The arguments of the prune command.</summary>
    public static readonly IReadOnlyList<string> PruneArguments = Array.AsReadOnly(new[] { "builder", "prune", "--all", "--force" });

    /// <summary>
    /// Runs the prune through <paramref name="runCli"/> and describes the outcome. A CLI that
    /// cannot be started or exits non-zero is reported in the outcome, not thrown; cancellation
    /// is thrown.
    /// </summary>
    public static async Task<BuildCachePruneOutcome> PruneAsync(
        Func<IReadOnlyList<string>, CancellationToken, Task<DockerCliOutput>> runCli,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runCli);
        DockerCliOutput output;
        try
        {
            output = await runCli(PruneArguments, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerCliException ex)
        {
            return new BuildCachePruneOutcome(false, null, null, null, "Build cache: " + ex.Message);
        }

        var raw = Combine(output.StandardOutput, output.StandardError);
        if (output.ExitCode != 0)
        {
            var detail = string.IsNullOrWhiteSpace(output.StandardError) ? output.StandardOutput : output.StandardError;
            return new BuildCachePruneOutcome(false, null, null, raw,
                $"Build cache: 'docker {string.Join(' ', PruneArguments)}' exited with code {output.ExitCode}.{Describe(detail)}");
        }

        var text = ParseReclaimedSpace(output.StandardOutput);
        if (text == null)
        {
            text = ParseReclaimedSpace(output.StandardError);
        }

        return new BuildCachePruneOutcome(true, text, ParseSize(text), raw, null);
    }

    /// <summary>
    /// Runs the docker CLI named by <paramref name="options"/> with <paramref name="args"/>,
    /// pointing it at the options' endpoint (as <c>DOCKER_HOST</c>) when one is set. Throws
    /// <see cref="DockerCliException"/> (exit code -1) when the CLI is missing or cannot be
    /// started; a non-zero exit code is returned, not thrown.
    /// </summary>
    public static async Task<DockerCliOutput> RunCliAsync(DockerClientOptions options, IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(args);
        var cli = string.IsNullOrWhiteSpace(options.DockerCliPath) ? "docker" : options.DockerCliPath.Trim();
        var command = cli + " " + string.Join(' ', args);
        if (!DockerAvailability.IsCliOnPath(cli))
        {
            throw new DockerCliException(command, -1, $"The docker command-line tool '{cli}' was not found.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = cli,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (!string.IsNullOrWhiteSpace(options.Endpoint))
        {
            startInfo.Environment["DOCKER_HOST"] = options.Endpoint.Trim();
        }

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
        {
            throw new DockerCliException(command, -1, $"Could not start '{cli}': {ex.Message}", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
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
                // Already gone.
            }
            catch (Win32Exception)
            {
                // Cannot be killed; nothing more to do.
            }

            throw;
        }

        return new DockerCliOutput(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    /// <summary>
    /// The reclaimed-space text of a prune's output: the value of its last <c>Total:</c> (or
    /// <c>Total reclaimed space:</c>) line, for example <c>1.234GB</c>; <c>null</c> when there is
    /// none.
    /// </summary>
    public static string ParseReclaimedSpace(string output)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        string result = null;
        foreach (var line in output.Split('\n'))
        {
            var match = TotalLinePattern().Match(line.TrimEnd('\r'));
            if (match.Success)
            {
                result = match.Groups[1].Value;
            }
        }

        return result;
    }

    /// <summary>
    /// The byte count of a size the docker CLI printed: decimal units (<c>B</c>, <c>kB</c>,
    /// <c>MB</c>, <c>GB</c>, <c>TB</c>, <c>PB</c>, powers of 1000) or binary ones (<c>KiB</c>,
    /// <c>MiB</c>, ... powers of 1024); <c>null</c> when it cannot be read.
    /// </summary>
    public static long? ParseSize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = SizePattern().Match(text.Trim());
        if (!match.Success || !decimal.TryParse(match.Groups[1].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        var unit = match.Groups[2].Value;
        var binary = unit.Length == 3;
        var power = char.ToUpperInvariant(unit[0]) switch
        {
            'B' => 0,
            'K' => 1,
            'M' => 2,
            'G' => 3,
            'T' => 4,
            'P' => 5,
            _ => -1,
        };
        if (power < 0)
        {
            return null;
        }

        decimal factor = 1;
        for (var i = 0; i < power; i++)
        {
            factor *= binary ? 1024 : 1000;
        }

        try
        {
            return (long)Math.Round(number * factor, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static string Combine(string stdout, string stderr)
    {
        if (string.IsNullOrEmpty(stderr))
        {
            return stdout ?? string.Empty;
        }

        if (string.IsNullOrEmpty(stdout))
        {
            return stderr;
        }

        return stdout.EndsWith('\n') ? stdout + stderr : stdout + Environment.NewLine + stderr;
    }

    private static string Describe(string detail)
        => string.IsNullOrWhiteSpace(detail) ? string.Empty : " " + detail.Trim();

    [GeneratedRegex(@"^\s*Total(?:\s+reclaimed\s+space)?\s*:\s*(\S+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex TotalLinePattern();

    [GeneratedRegex(@"^(\d+(?:\.\d+)?)\s*(B|[kKMGTP]B|[KMGTP]iB)$")]
    private static partial Regex SizePattern();
}
