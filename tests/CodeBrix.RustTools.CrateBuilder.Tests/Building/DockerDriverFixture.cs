using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Engine;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

/// <summary>
/// The Docker workbench of the Docker build-driver tests, isolated from every other workbench
/// on the engine (its own label prefix, cargo cache volume and container prefix). At the end it
/// removes every container, volume and derived image it created; with
/// CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1 the cache volumes and images are kept (a speed knob
/// only). Base images (rust, manylinux) are never removed.
/// </summary>
public sealed class DockerDriverFixture : IAsyncLifetime
{
    public const string KeepCacheVariable = "CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE";

    public DockerDriverFixture()
    {
        Workbench = new DockerRustWorkbench(new DockerRustWorkbenchOptions
        {
            LabelPrefix = "codebrix.rusttools.cratebuilder.tests",
            CacheVolumeName = "codebrix-rusttools-cratebuilder-tests-cargo-cache",
            ContainerNamePrefix = "codebrix-rusttools-cratebuilder-tests",
        });
    }

    public DockerRustWorkbench Workbench { get; }

    public DockerEngineInfo Engine { get; private set; }

    public async ValueTask InitializeAsync()
    {
        Engine = await Workbench.CheckEngineAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Engine?.IsAvailable == true)
            {
                var keep = Environment.GetEnvironmentVariable(KeepCacheVariable) == "1";
                await Workbench.CleanupAsync(new CleanupOptions { RemoveContainers = true, RemoveImages = !keep, RemoveVolumes = !keep }, CancellationToken.None);
            }
        }
        finally
        {
            Workbench.Dispose();
        }
    }

    /// <summary>Skips the test unless the engine is usable.</summary>
    public void SkipIfUnavailable()
    {
        if (Engine == null || !Engine.IsAvailable)
        {
            Assert.Skip("No usable Docker engine (CodeBrix.RustTools.Docker engine check: " + (Engine?.ErrorMessage ?? string.Join(", ", Engine?.Issues ?? Array.Empty<DockerEngineIssue>())) + ").");
        }
    }
}
