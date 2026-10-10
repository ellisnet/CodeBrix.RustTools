using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// Checks whether a Docker engine can run Linux build containers. The check never throws for a
/// Docker-side problem: everything it finds is reported in the returned
/// <see cref="DockerEngineInfo"/>, and <see cref="DockerGuidanceProvider"/> turns the issues into
/// instructions for the user.
/// </summary>
public static class DockerAvailability
{
    /// <summary>
    /// Connects to the engine described by <paramref name="options"/> (or the default engine),
    /// asks it for its version and system information, and reports the result.
    /// </summary>
    /// <param name="options">The Docker client configuration; <c>null</c> resolves the engine
    /// the usual way (<c>DOCKER_HOST</c>, then the platform default).</param>
    /// <param name="cancellationToken">Cancels the check; cancelling it is the one way this method
    /// throws (<see cref="OperationCanceledException"/>).</param>
    public static async Task<DockerEngineInfo> CheckAsync(DockerClientOptions options = null, CancellationToken cancellationToken = default)
    {
        DockerClient client;
        try
        {
            client = DockerClient.Create(options);
        }
        catch (Exception ex) when (ex is NotSupportedException || ex is DockerException || ex is ArgumentException || ex is UriFormatException)
        {
            return new DockerEngineInfo
            {
                IsAvailable = false,
                Endpoint = options?.Endpoint ?? Environment.GetEnvironmentVariable("DOCKER_HOST"),
                HostOperatingSystem = HostOperatingSystem.Detect(),
                DockerCliFound = IsCliOnPath(options?.DockerCliPath),
                Issues = new[] { DockerEngineIssue.EndpointNotSupported },
                ErrorMessage = ex.Message,
            };
        }

        using (client)
        {
            return await CheckAsync(client, options?.DockerCliPath, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Checks the engine an existing client talks to.
    /// </summary>
    internal static async Task<DockerEngineInfo> CheckAsync(DockerClient client, string dockerCliPath, CancellationToken cancellationToken)
    {
        var os = HostOperatingSystem.Detect();
        var cliFound = IsCliOnPath(dockerCliPath);
        var issues = new List<DockerEngineIssue>();
        DockerVersionInfo version = null;
        DockerSystemInfo system = null;
        string error = null;

        try
        {
            version = await client.System.GetVersionAsync(cancellationToken).ConfigureAwait(false);
            system = await client.System.GetInfoAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDockerFailure(ex, cancellationToken))
        {
            error = ex.Message;
            issues.Add(Classify(ex));
        }

        if (system == null && issues.Count == 0)
        {
            issues.Add(DockerEngineIssue.NotReachable);
        }

        var osType = system?.OsType ?? version?.Os;
        var isLinux = string.Equals(osType, "linux", StringComparison.OrdinalIgnoreCase);
        if (system != null && !isLinux)
        {
            issues.Add(DockerEngineIssue.NotLinuxDaemon);
            error ??= $"The Docker engine at {client.Endpoint} runs '{osType}' containers; Rust builds need Linux containers.";
        }

        var available = system != null && isLinux;
        if (!cliFound)
        {
            issues.Add(DockerEngineIssue.DockerCliNotFound);
        }

        var operatingSystem = system?.OperatingSystem;
        return new DockerEngineInfo
        {
            IsAvailable = available,
            Endpoint = client.Endpoint,
            ServerVersion = system?.ServerVersion ?? version?.Version,
            ApiVersion = version?.ApiVersion,
            OperatingSystem = operatingSystem,
            OsType = osType,
            Architecture = system?.Architecture ?? version?.Arch,
            IsLinuxDaemon = isLinux,
            IsDockerDesktop = operatingSystem != null && operatingSystem.Contains("Docker Desktop", StringComparison.OrdinalIgnoreCase),
            DockerCliFound = cliFound,
            HostOperatingSystem = os,
            Issues = issues,
            ErrorMessage = error,
        };
    }

    /// <summary>
    /// Decides whether a failure means "access refused" or "nothing there", from the exception
    /// chain (an access-denied socket error or <see cref="UnauthorizedAccessException"/>) and,
    /// failing that, from the message text.
    /// </summary>
    internal static DockerEngineIssue Classify(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
        {
            if (current is UnauthorizedAccessException
                || (current is SocketException socket && socket.SocketErrorCode == SocketError.AccessDenied)
                || ContainsPermissionText(current.Message))
            {
                return DockerEngineIssue.PermissionDenied;
            }
        }

        return DockerEngineIssue.NotReachable;
    }

    /// <summary>
    /// Whether the named Docker CLI executable exists: a rooted path is checked directly, a bare
    /// name is searched for on PATH (with <c>.exe</c> on Windows).
    /// </summary>
    internal static bool IsCliOnPath(string dockerCliPath)
    {
        var name = string.IsNullOrWhiteSpace(dockerCliPath) ? "docker" : dockerCliPath.Trim();
        var candidates = OperatingSystem.IsWindows() && !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? new[] { name + ".exe", name }
            : new[] { name };

        if (Path.IsPathRooted(name) || name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
        {
            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    if (File.Exists(Path.Combine(directory, candidate)))
                    {
                        return true;
                    }
                }
                catch (ArgumentException)
                {
                    // A malformed PATH entry; skip it.
                }
            }
        }

        return false;
    }

    private static bool ContainsPermissionText(string message)
        => message != null
           && (message.Contains("permission denied", StringComparison.OrdinalIgnoreCase)
               || message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)
               || message.Contains("access denied", StringComparison.OrdinalIgnoreCase));

    private static bool IsDockerFailure(Exception exception, CancellationToken cancellationToken)
        => !(exception is OperationCanceledException && cancellationToken.IsCancellationRequested);
}
