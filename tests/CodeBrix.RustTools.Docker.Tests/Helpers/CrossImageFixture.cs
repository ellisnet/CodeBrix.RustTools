using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Docker;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using Xunit;

namespace CodeBrix.RustTools.Docker.Tests.Helpers;

/// <summary>
/// The base of the cross-compilation fixtures: a workbench of its own (with its own cargo
/// cache volumes, so it never competes with the other collections), the engine check (FAILING
/// the collection when the engine cannot be used), and the derived image with one
/// cross-compilation layer, built once per run and timed into the fixture log. When the library
/// refuses the fixture's targets on this engine (Android on anything but x86-64), no image is
/// built: the refusal is kept in <see cref="UnsupportedReason"/> and the collection's tests
/// call <see cref="SkipIfUnsupported"/> to be reported as skipped with that reason. At the end
/// every container, derived image and volume of the workbench is removed, unless
/// CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1.
/// </summary>
public abstract class CrossImageFixture : IAsyncLifetime
{
    public DockerRustWorkbench Workbench { get; private set; }

    public DockerEngineInfo Engine { get; private set; }

    public RustBuildImage Image { get; private set; }

    public TimeSpan ImageDuration { get; private set; }

    /// <summary>The library's reason for refusing this fixture's targets on the engine (the
    /// CrossTargetNotSupportedException message), or <c>null</c> when the image was built.</summary>
    public string UnsupportedReason { get; private set; }

    /// <summary>The concrete Linux target of the engine's own architecture.</summary>
    public CrossTarget NativeTarget()
        => CrossTargetInfo.NativeLinuxTargetFor(Engine) ?? throw new InvalidOperationException("The engine's architecture has no Linux target: " + Engine?.Architecture);

    /// <summary>Skips the calling test, with the library's reason, when the engine cannot build
    /// this fixture's targets; a no-op otherwise.</summary>
    public void SkipIfUnsupported()
    {
        if (UnsupportedReason != null)
        {
            Assert.Skip($"The {Name} cross-compilation tests cannot run on this Docker engine: {UnsupportedReason}");
        }
    }

    protected abstract string Name { get; }

    protected abstract IReadOnlyList<CrossTarget> Targets { get; }

    public string CacheVolumeName => DockerEngineFixture.TestCacheVolumeName + "-" + Name;

    public BuildImageSpec CreateImageSpec()
    {
        var spec = new BuildImageSpec();
        foreach (var target in Targets)
        {
            spec.CrossTargets.Add(target);
        }

        return spec;
    }

    public async ValueTask InitializeAsync()
    {
        var options = DockerEngineFixture.CreateOptions();
        options.CacheVolumeName = CacheVolumeName;
        options.ContainerNamePrefix = DockerEngineFixture.TestContainerNamePrefix + "-" + Name;
        Workbench = new DockerRustWorkbench(options);
        Engine = await Workbench.CheckEngineAsync(CancellationToken.None);
        if (!Engine.IsAvailable)
        {
            throw new InvalidOperationException(
                "The live Docker tests need a running Docker engine with Linux containers, and none could be used: "
                + Engine + Environment.NewLine + string.Join(Environment.NewLine + Environment.NewLine, Workbench.GetDockerGuidance(Engine)));
        }

        try
        {
            CrossTargetInfo.EnsureEngineCanBuild(Engine, Targets);
        }
        catch (CrossTargetNotSupportedException ex)
        {
            UnsupportedReason = ex.Message;
            DockerEngineFixture.Log($"{DateTime.Now:O} {Name} image not built, tests skip: {ex.Message}");
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        Image = await Workbench.EnsureImageAsync(CreateImageSpec(), null, CancellationToken.None);
        ImageDuration = stopwatch.Elapsed;
        DockerEngineFixture.Log($"{DateTime.Now:O} {Name} image {Image.Reference} built={Image.WasBuilt} reused={Image.WasReused} in {ImageDuration}");
    }

    public async ValueTask DisposeAsync()
    {
        if (Workbench == null)
        {
            return;
        }

        try
        {
            var keep = DockerEngineFixture.KeepCache;
            var result = await Workbench.CleanupAsync(
                new CleanupOptions { RemoveContainers = true, RemoveImages = !keep, RemoveVolumes = !keep, OnlyThisWorkbench = true },
                CancellationToken.None);
            if (!keep)
            {
                foreach (var name in new[] { CacheVolumeName, CacheVolumeName + "-git" })
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

            DockerEngineFixture.Log($"{DateTime.Now:O} {Name} cleanup keep={keep}: {result.RemovedContainers.Count} containers, "
                + $"{result.RemovedImages.Count} images, {result.RemovedVolumes.Count} volumes");
        }
        finally
        {
            Workbench.Dispose();
        }
    }
}
