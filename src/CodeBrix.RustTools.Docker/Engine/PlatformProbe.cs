using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Internal;

namespace CodeBrix.RustTools.Docker.Engine;

/// <summary>
/// Finds out whether an engine runs containers of a platform: pulls the probe image for it,
/// runs <c>uname -m</c> in a labelled throw-away container created with that platform, removes
/// the container, and classifies what happened.
/// </summary>
internal static class PlatformProbe
{
    /// <summary>How long the probe container may run before the probe gives up.</summary>
    public static readonly TimeSpan RunTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The words the kernel's (and runc's) error carries when a program's architecture
    /// has no binfmt handler.</summary>
    public const string ExecFormatErrorText = "exec format error";

    public static async Task<PlatformProbeResult> RunAsync(
        DockerClient client,
        DockerEngineInfo engine,
        ResourceLabels labels,
        string containerNamePrefix,
        string platform,
        string probeImage,
        CancellationToken cancellationToken)
    {
        var normalized = DockerPlatforms.Normalize(platform);
        if (engine.IsNativePlatform(normalized))
        {
            return new PlatformProbeResult
            {
                Platform = normalized,
                Outcome = PlatformProbeOutcome.Native,
                Message = $"{normalized ?? "The engine's own platform"} is the engine's own platform ({engine.Architecture}); it runs natively.",
            };
        }

        try
        {
            await client.Images.PullAsync(probeImage, new ImagePullOptions { Platform = normalized }, progress: null, cancellationToken).ConfigureAwait(false);
        }
        catch (DockerException ex)
        {
            return Result(normalized, PlatformProbeOutcome.Refused, null, probeImage, engine,
                $"The probe image {probeImage} could not be pulled for {normalized}: {ex.Message}");
        }

        var spec = new ContainerSpec
        {
            Image = probeImage,
            Name = containerNamePrefix + "-platform-probe-" + Guid.NewGuid().ToString("N").Substring(0, 12),
            Platform = normalized,
            Command = new[] { "uname", "-m" },
        };
        foreach (var pair in labels.For(ResourceLabels.KindContainer))
        {
            spec.Labels[pair.Key] = pair.Value;
        }

        string id = null;
        try
        {
            try
            {
                id = await client.Containers.CreateAsync(spec, cancellationToken).ConfigureAwait(false);
                await client.Containers.StartAsync(id, cancellationToken).ConfigureAwait(false);
            }
            catch (DockerException ex)
            {
                return Classify(normalized, probeImage, engine, -1, ex.Message, $"A {normalized} container could not be {(id == null ? "created" : "started")}: {ex.Message}");
            }

            long exitCode;
            ContainerLogs logs;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(RunTimeout);
                try
                {
                    exitCode = await client.Containers.WaitForExitAsync(id, timeout.Token).ConfigureAwait(false);
                    logs = await client.Containers.GetLogsAsync(id, null, false, timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return Result(normalized, PlatformProbeOutcome.Failed, null, probeImage, engine,
                        $"The {normalized} probe container did not finish within {RunTimeout.TotalSeconds:0} s.");
                }
                catch (DockerException ex)
                {
                    return Result(normalized, PlatformProbeOutcome.Failed, null, probeImage, engine,
                        $"The {normalized} probe container's result could not be read: {ex.Message}");
                }
            }

            var output = (logs?.Stdout ?? string.Empty).Trim();
            var errors = (logs?.Stderr ?? string.Empty).Trim();
            if (exitCode != 0)
            {
                var text = string.IsNullOrEmpty(errors) ? output : errors;
                return Classify(normalized, probeImage, engine, exitCode, text, $"`uname -m` in a {normalized} container exited with code {exitCode}: {text}");
            }

            var expected = DockerPlatforms.MachineOf(normalized);
            if (IsKnownMachine(expected) && !string.Equals(output, expected, StringComparison.Ordinal))
            {
                return Result(normalized, PlatformProbeOutcome.Failed, output, probeImage, engine,
                    $"A {normalized} container reported the machine '{output}', not {expected}: the engine did not run the requested platform.");
            }

            return Result(normalized, PlatformProbeOutcome.Runs, output, probeImage, engine,
                $"{normalized} containers run on this engine ({engine.Architecture}) under emulation: `uname -m` reported {output}.");
        }
        finally
        {
            if (id != null)
            {
                try
                {
                    await client.Containers.RemoveAsync(id, force: true, removeVolumes: true, CancellationToken.None).ConfigureAwait(false);
                }
                catch (DockerException)
                {
                    // The container is labelled; CleanupAsync removes it if this did not.
                }
            }
        }
    }

    /// <summary>
    /// The outcome of a failed create, start or run: <see cref="PlatformProbeOutcome.NoEmulator"/>
    /// when the error says "exec format error", <see cref="PlatformProbeOutcome.Refused"/> when
    /// nothing ran (<paramref name="exitCode"/> -1), otherwise <see cref="PlatformProbeOutcome.Failed"/>.
    /// </summary>
    internal static PlatformProbeResult Classify(string platform, string probeImage, DockerEngineInfo engine, long exitCode, string errorText, string message)
    {
        if (errorText != null && errorText.Contains(ExecFormatErrorText, StringComparison.OrdinalIgnoreCase))
        {
            var machine = DockerPlatforms.MachineOf(platform);
            return Result(platform, PlatformProbeOutcome.NoEmulator, null, probeImage, engine,
                $"{platform} programs cannot execute on this engine ({engine.Architecture}): {message.TrimEnd('.')}. The engine's host needs a qemu-user binfmt handler for {machine} (on Linux: /proc/sys/fs/binfmt_misc/qemu-{machine}).");
        }

        return Result(platform, exitCode < 0 ? PlatformProbeOutcome.Refused : PlatformProbeOutcome.Failed, null, probeImage, engine, message);
    }

    private static PlatformProbeResult Result(string platform, PlatformProbeOutcome outcome, string machine, string probeImage, DockerEngineInfo engine, string message) => new PlatformProbeResult
    {
        Platform = platform,
        Outcome = outcome,
        IsEmulated = true,
        ReportedMachine = string.IsNullOrEmpty(machine) ? null : machine,
        ProbeImage = probeImage,
        Message = message,
        Guidance = outcome == PlatformProbeOutcome.NoEmulator
            ? DockerGuidanceProvider.GetGuidance(DockerGuidanceTopic.EnableForeignPlatforms, engine.HostOperatingSystem)
            : null,
    };

    private static bool IsKnownMachine(string machine)
        => machine == "x86_64" || machine == "aarch64" || machine == "riscv64";
}
