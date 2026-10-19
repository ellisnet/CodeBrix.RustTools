using System;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Docker;
using CodeBrix.RustTools.Docker.Engine;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.EndToEnd;

/// <summary>
/// The Docker workbench settings of the end-to-end Docker tests, isolated from every other
/// workbench on the engine: the label prefix <see cref="LabelPrefix"/>, its own cargo cache
/// volume and container-name prefix. At the end it removes every container, volume and derived
/// image carrying its labels (CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1 keeps the cache volumes and
/// images). Base images are never removed.
/// </summary>
public sealed class DockerEndToEndFixture : IAsyncLifetime
{
    /// <summary>The label prefix of every container, volume and image these tests create.</summary>
    public const string LabelPrefix = "codebrix.rusttools.cratebuilder.tests.e2e";

    /// <summary>The cargo cache volume of these tests.</summary>
    public const string CacheVolumeName = "codebrix-rusttools-cratebuilder-tests-e2e-cargo-cache";

    /// <summary>The container-name prefix of these tests.</summary>
    public const string ContainerNamePrefix = "codebrix-rusttools-cratebuilder-tests-e2e";

    /// <summary>The workbench options the tests' builders use.</summary>
    public static DockerRustWorkbenchOptions Options() => new DockerRustWorkbenchOptions
    {
        LabelPrefix = LabelPrefix,
        CacheVolumeName = CacheVolumeName,
        ContainerNamePrefix = ContainerNamePrefix,
    };

    public DockerEngineInfo Engine { get; private set; }

    public async ValueTask InitializeAsync()
    {
        using var workbench = new DockerRustWorkbench(Options());
        Engine = await workbench.CheckEngineAsync(CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (Engine?.IsAvailable != true)
        {
            return;
        }

        using var workbench = new DockerRustWorkbench(Options());
        var keep = Environment.GetEnvironmentVariable(Building.DockerDriverFixture.KeepCacheVariable) == "1";
        await workbench.CleanupAsync(new CleanupOptions { RemoveContainers = true, RemoveImages = !keep, RemoveVolumes = !keep }, CancellationToken.None);
    }

    /// <summary>Skips the test unless the engine is usable and x86-64 (the tests build linux-x64
    /// natively and win-x64 with the llvm-mingw layer).</summary>
    public void SkipUnlessX64Engine()
    {
        if (Engine == null || !Engine.IsAvailable)
        {
            Assert.Skip("No usable Docker engine (CodeBrix.RustTools.Docker engine check: " + (Engine?.ErrorMessage ?? string.Join(", ", Engine?.Issues ?? Array.Empty<DockerEngineIssue>())) + ").");
        }

        if (!string.Equals(Engine.Architecture, "x86_64", StringComparison.OrdinalIgnoreCase))
        {
            Assert.Skip("The end-to-end Docker tests build linux-x64 natively and need an x86-64 engine; this engine is " + Engine.Architecture + ".");
        }
    }
}
