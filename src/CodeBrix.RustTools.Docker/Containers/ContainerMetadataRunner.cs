using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.Docker.Containers;

/// <summary>
/// Runs <c>cargo metadata --format-version 1</c> inside a build container and parses it with
/// the same <see cref="CargoMetadata.Parse"/> the host-side library uses. Every path in the
/// result (manifest paths, the workspace root, the target folder) is a CONTAINER path, for
/// example <c>/work/Cargo.toml</c>.
/// </summary>
internal static class ContainerMetadataRunner
{
    /// <summary>
    /// The cargo arguments for a manifest inside the container (the same list
    /// <see cref="CargoProject.GetMetadataAsync"/> uses on the host).
    /// </summary>
    public static IReadOnlyList<string> BuildCommand(string containerManifestPath, CargoMetadataOptions options)
    {
        var command = new List<string> { "cargo" };
        command.AddRange(CargoProject.BuildMetadataArguments(containerManifestPath, options ?? new CargoMetadataOptions()));
        return command;
    }

    /// <summary>
    /// Runs <c>cargo metadata</c> for a manifest inside the container.
    /// </summary>
    /// <exception cref="ContainerBuildException">cargo metadata failed (the exception carries
    /// the exit code and standard error), or its output is not valid metadata.</exception>
    public static async Task<CargoMetadata> RunAsync(DockerClient client, string containerId, string containerManifestPath, CargoMetadataOptions options, TimeSpan defaultTimeout, CancellationToken cancellationToken)
    {
        var command = BuildCommand(containerManifestPath, options);
        var limit = options?.Timeout ?? defaultTimeout;
        using var timeoutSource = limit > TimeSpan.Zero && limit != Timeout.InfiniteTimeSpan
            ? new CancellationTokenSource(limit)
            : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);
        var directory = containerManifestPath.Substring(0, Math.Max(1, containerManifestPath.LastIndexOf('/')));
        var environment = new[] { "CARGO_TERM_COLOR=never", "RUSTUP_AUTO_INSTALL=0", "CARGO_TARGET_DIR=" + BuildContainer.TargetPath };
        ContainerCommandResult result;
        try
        {
            result = await ContainerCommandRunner.RunAsync(client, containerId, command, directory, environment, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (timeoutSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new ContainerBuildException($"cargo metadata did not finish within {limit}.", new TimeoutException(ex.Message, ex));
        }

        if (!result.Succeeded)
        {
            throw new ContainerBuildException(
                $"cargo metadata failed for '{containerManifestPath}' in the build container (exit code {result.ExitCode}).{Tail(result.StandardError)}",
                result.Command,
                result.ExitCode,
                result.StandardOutput,
                result.StandardError);
        }

        try
        {
            return CargoMetadata.Parse(result.StandardOutput);
        }
        catch (Exception ex) when (ex is JsonException || ex is RustToolsException || ex is FormatException)
        {
            throw new ContainerBuildException($"cargo metadata for '{containerManifestPath}' printed output that is not valid metadata: {ex.Message}", ex);
        }
    }

    private static string Tail(string standardError)
    {
        if (string.IsNullOrWhiteSpace(standardError))
        {
            return string.Empty;
        }

        var lines = standardError.TrimEnd().Split('\n');
        return Environment.NewLine + string.Join(Environment.NewLine, lines.Skip(Math.Max(0, lines.Length - 20)).Select(l => l.TrimEnd('\r')));
    }
}
