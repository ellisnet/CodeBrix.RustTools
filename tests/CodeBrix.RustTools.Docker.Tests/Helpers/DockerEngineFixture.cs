using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Containers;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The one shared fixture of the live Docker tests. It checks the engine (and FAILS the whole
/// collection when the engine cannot be used - these tests never skip for a missing engine; only
/// the cross-compilation collections skip, when the library refuses their targets on the engine,
/// see CrossImageFixture), makes sure the base
/// image is present (pulling it the first time), starts one shared build container, and at the
/// end removes every container, derived image and volume the tests created. Setting
/// CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1 keeps the cargo cache volumes and the derived images
/// between runs - a speed knob, never a gate.
/// </summary>
public sealed class DockerEngineFixture : IAsyncLifetime
{
    public const string KeepCacheVariable = "CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE";

    public const string TestCacheVolumeName = "codebrix-rusttools-tests-cargo-cache";

    public const string TestImageRepository = "codebrix-rusttools-tests";

    public const string TestContainerNamePrefix = "codebrix-rusttools-tests";

    public static string LogPath => Path.Combine(AppContext.BaseDirectory, "docker-fixture-log.txt");

    public static bool KeepCache => Environment.GetEnvironmentVariable(KeepCacheVariable) == "1";

    public DockerRustWorkbench Workbench { get; private set; }

    public DockerEngineInfo Engine { get; private set; }

    public RustBuildImage BaseImage { get; private set; }

    public BuildContainer SharedContainer { get; private set; }

    public TimeSpan BaseImageDuration { get; private set; }

    /// <summary>The concrete Linux target of the engine's own architecture (what
    /// <see cref="CrossTarget.LinuxNative"/> resolves to here).</summary>
    public CrossTarget NativeTarget { get; private set; }

    public static DockerRustWorkbenchOptions CreateOptions() => new DockerRustWorkbenchOptions
    {
        CacheVolumeName = TestCacheVolumeName,
        BuildImageRepository = TestImageRepository,
        ContainerNamePrefix = TestContainerNamePrefix,
    };

    public async ValueTask InitializeAsync()
    {
        Workbench = new DockerRustWorkbench(CreateOptions());
        Engine = await Workbench.CheckEngineAsync(CancellationToken.None);
        if (!Engine.IsAvailable)
        {
            var guidance = string.Join(Environment.NewLine + Environment.NewLine, Workbench.GetDockerGuidance(Engine));
            throw new InvalidOperationException(
                "The live Docker tests need a running Docker engine with Linux containers, and none could be used: "
                + Engine + Environment.NewLine + guidance);
        }

        NativeTarget = CrossTargetInfo.NativeLinuxTargetFor(Engine)
            ?? throw new InvalidOperationException("The live Docker tests need an x86-64, ARM64 or RISC-V engine; this one is " + Engine.Architecture + ".");
        var stopwatch = Stopwatch.StartNew();
        BaseImage = await Workbench.EnsureImageAsync(null, null, CancellationToken.None);
        BaseImageDuration = stopwatch.Elapsed;
        Log($"{DateTime.Now:O} engine {Engine}; base image {BaseImage.Reference} {BaseImage.ImageId} pulled={BaseImage.WasBasePulled} in {BaseImageDuration}");

        SharedContainer = await Workbench.StartContainerAsync(BaseImage, null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (Workbench == null)
        {
            return;
        }

        if (SharedContainer != null)
        {
            await SharedContainer.DisposeAsync();
        }

        try
        {
            var keep = KeepCache;
            var result = await Workbench.CleanupAsync(
                new CleanupOptions { RemoveContainers = true, RemoveImages = !keep, RemoveVolumes = !keep, OnlyThisWorkbench = true },
                CancellationToken.None);
            if (!keep)
            {
                foreach (var name in new[] { TestCacheVolumeName, TestCacheVolumeName + "-git" })
                {
                    try
                    {
                        await Workbench.Client.Volumes.RemoveAsync(name, force: false, CancellationToken.None);
                    }
                    catch (DockerApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                    {
                        // Already removed by the cleanup above.
                    }
                }
            }

            Log($"{DateTime.Now:O} cleanup keep={keep}: {result.RemovedContainers.Count} containers, {result.RemovedImages.Count} images, "
                + $"{result.RemovedVolumes.Count} volumes; failures: {string.Join("; ", result.Failures.DefaultIfEmpty("none"))}");
        }
        finally
        {
            Workbench.Dispose();
        }
    }

    internal static void Log(string line)
    {
        try
        {
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch (IOException)
        {
            // The log is a convenience.
        }
    }
}
