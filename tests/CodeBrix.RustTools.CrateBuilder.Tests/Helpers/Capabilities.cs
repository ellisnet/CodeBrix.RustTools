using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Toolchain;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

/// <summary>
/// Dynamic skips for live tests: cargo (through CodeBrix.RustTools' own detection) and a
/// usable Docker engine (through CodeBrix.RustTools.Docker's engine check), and a network route
/// to GitHub. Each is detected once per test run. Tests that go too deep for Windows' path
/// limit skip there.
/// </summary>
internal static class Capabilities
{
    private static readonly Lazy<Task<RustToolchainInfo>> Toolchain = new Lazy<Task<RustToolchainInfo>>(
        () => new RustToolchainDetector().DetectAsync(null, CancellationToken.None));

    /// <summary>How long one attempt of the GitHub probe may take.</summary>
    public static readonly TimeSpan GitHubProbeTimeout = TimeSpan.FromSeconds(20);

    /// <summary>How many attempts the GitHub probe makes before it reports no route.</summary>
    public const int GitHubProbeAttempts = 3;

    private static readonly Lazy<Task<bool>> GitHub = new Lazy<Task<bool>>(() => ProbeGitHubAsync(GitHubProbeAttempts, GitHubProbeTimeout));

    private static readonly Lazy<Task<DockerEngineInfo>> Engine = new Lazy<Task<DockerEngineInfo>>(
        () => DockerAvailability.CheckAsync(null, CancellationToken.None));

    /// <summary>Skips the test unless cargo and rustc were detected.</summary>
    public static async Task SkipIfCargoMissingAsync()
    {
        var toolchain = await Toolchain.Value;
        if (!toolchain.HasCargoAndRustc)
        {
            Assert.Skip("cargo / rustc are not installed on this machine (CodeBrix.RustTools detection found none).");
        }
    }

    /// <summary>Whether github.com:443 can be reached: ONE probe shared by every test of the
    /// run (so tests running in parallel under load do not each race their own short probe),
    /// up to <see cref="GitHubProbeAttempts"/> TCP connects of <see cref="GitHubProbeTimeout"/>
    /// each.</summary>
    public static Task<bool> CanReachGitHubAsync() => GitHub.Value;

    /// <summary>Tries a TCP connect to github.com:443 up to <paramref name="attempts"/> times,
    /// each limited to <paramref name="timeout"/>; true on the first success.</summary>
    internal static async Task<bool> ProbeGitHubAsync(int attempts, TimeSpan timeout)
    {
        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var limit = new CancellationTokenSource(timeout);
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("github.com", 443, limit.Token);
                return true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // Try again: a busy machine can miss one connect window.
            }
        }

        return false;
    }

    /// <summary>Skips the test on Windows, where it goes past the 260-character path limit
    /// (MAX_PATH) below the test output folder.</summary>
    /// <param name="what">What fails that deep, for the skip message.</param>
    public static void SkipOnWindowsPathLimit(string what)
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Skip("Windows' 260-character path limit (MAX_PATH) is reached below the test output folder: " + what + ".");
        }
    }

    /// <summary>Skips the test unless the Docker engine is usable.</summary>
    public static async Task SkipIfDockerUnavailableAsync()
    {
        var engine = await Engine.Value;
        if (!engine.IsAvailable)
        {
            Assert.Skip("No usable Docker engine (CodeBrix.RustTools.Docker engine check: " + (engine.ErrorMessage ?? string.Join(", ", engine.Issues)) + ").");
        }
    }
}
