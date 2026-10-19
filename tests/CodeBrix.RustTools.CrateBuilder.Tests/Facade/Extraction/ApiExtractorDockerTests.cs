using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Extraction;

/// <summary>
/// A Docker workbench with names of its own (so cleanup touches nothing else); at the end it
/// removes its containers and, unless CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE=1, its volumes.
/// </summary>
public sealed class ExtractionDockerFixture : IAsyncLifetime
{
    public const string LabelPrefix = "codebrix.rusttools.cratebuilder.tests.extract";

    public DockerRustWorkbench Workbench { get; } = new DockerRustWorkbench(new DockerRustWorkbenchOptions
    {
        LabelPrefix = LabelPrefix,
        CacheVolumeName = "codebrix-rusttools-cratebuilder-tests-extract-cargo-cache",
        ContainerNamePrefix = "codebrix-rusttools-cratebuilder-tests-extract",
    });

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        try
        {
            var keep = Environment.GetEnvironmentVariable("CODEBRIX_RUSTTOOLS_KEEP_DOCKER_CACHE") == "1";
            await Workbench.CleanupAsync(new CleanupOptions { RemoveContainers = true, RemoveVolumes = !keep });
        }
        catch (Exception)
        {
            // No engine (the tests skipped) or a cleanup problem: nothing to remove here.
        }
        finally
        {
            Workbench.Dispose();
        }
    }
}

[Collection("Docker")]
public class ApiExtractorDockerTests : IClassFixture<ExtractionDockerFixture>
{
    private readonly ExtractionDockerFixture _fixture;

    public ApiExtractorDockerTests(ExtractionDockerFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(CrateApiModel Model, string ScratchRoot)> ExtractAsync(ApiExtractorDockerRoute route, SampleSandbox scratch)
    {
        using var sandbox = SampleSandbox.FromSample(15);
        var context = ExtractionTestContext.Create(scratch.Directory, sandbox.CreateDirectory("run"), docker: _fixture.Workbench);
        var extractor = new ApiExtractor(new ApiExtractorOptions { PreferHostToolchain = false, DockerRoute = route });
        var model = await extractor.ExtractAsync(ExtractionTestContext.Workspace(sandbox.Directory, "datacrate"), new CrateBuildRequest { Mode = BuildMode.Docker }, context, TestContext.Current.CancellationToken);
        return (model, scratch.Directory);
    }

    [Fact]
    public async Task live_docker_builds_and_runs_the_tool_inside_a_container()
    {
        //Arrange
        await Capabilities.SkipIfDockerUnavailableAsync();
        using var scratch = SampleSandbox.CreateEmpty();

        //Act
        var (model, _) = await ExtractAsync(ApiExtractorDockerRoute.RunInContainer, scratch);

        //Assert
        model.Crate.Name.Should().Be("datacrate");
        model.Crate.ManifestPath.Should().Be("/work/crate/Cargo.toml");
        model.Functions.Should().HaveCount(19);
        model.MethodsOf(model.FindStruct("Encoder")).Select(m => m.SelfKind.ToString()).Should().Equal("None", "RefMut", "Ref");
        model.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public async Task live_docker_builds_in_a_container_and_runs_the_retrieved_tool_on_a_linux_host()
    {
        //Arrange
        await Capabilities.SkipIfDockerUnavailableAsync();
        var engine = await _fixture.Workbench.CheckEngineAsync(TestContext.Current.CancellationToken);
        var hostRid = ApiExtractor.HostLinuxRuntimeIdentifier();
        if (hostRid == null || hostRid != engine.NativeLinuxRuntimeIdentifier)
        {
            Assert.Skip($"Needs a Linux host of the engine's architecture (host {hostRid ?? "not Linux"}, engine {engine.NativeLinuxRuntimeIdentifier}).");
        }

        using var scratch = SampleSandbox.CreateEmpty();

        //Act
        var (model, scratchRoot) = await ExtractAsync(ApiExtractorDockerRoute.RetrieveAndRunOnHost, scratch);

        //Assert
        model.Crate.Name.Should().Be("datacrate");
        model.Functions.Should().HaveCount(19);
        var toolDirectory = ApiExtractorToolSource.GetToolDirectory(scratchRoot);
        File.Exists(Path.Combine(toolDirectory, "docker-" + hostRid, ApiExtractorToolSource.ToolName)).Should().BeTrue();
        Directory.Exists(Path.Combine(toolDirectory, "target")).Should().BeFalse();
    }

    [Fact]
    public async Task live_docker_falls_back_to_the_container_when_the_retrieved_tool_does_not_run_on_the_host()
    {
        //Arrange
        await Capabilities.SkipIfDockerUnavailableAsync();
        var hostRid = ApiExtractor.HostLinuxRuntimeIdentifier() ?? "linux";
        using var scratch = SampleSandbox.CreateEmpty();
        var toolDirectory = ApiExtractorToolSource.Materialise(scratch.Directory);
        var unusable = scratch.WriteFile(Path.Combine(toolDirectory, "docker-" + hostRid, ApiExtractorToolSource.ToolName), "not a program\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(unusable), "docker-build.ok"), unusable + "\n");

        //Act
        var (model, _) = await ExtractAsync(ApiExtractorDockerRoute.RetrieveAndRunOnHost, scratch);

        //Assert
        model.Crate.Name.Should().Be("datacrate");
        model.Crate.ManifestPath.Should().Be("/work/crate/Cargo.toml");
        model.Functions.Should().HaveCount(19);
    }
}
