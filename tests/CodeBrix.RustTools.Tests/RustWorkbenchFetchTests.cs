using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Tests.Helpers;
using CodeBrix.RustTools.Toolchain;
using SilverAssertions;
using Xunit;

namespace CodeBrix.RustTools.Tests;

/// <summary>
/// <see cref="RustWorkbench.FetchAsync"/>: <c>cargo fetch</c> behind a readiness check that needs
/// only cargo and rustc. The download test gives cargo an empty CARGO_HOME of its own, so it
/// proves the fetch even on a machine whose cargo cache already holds every crate.
/// </summary>
public class RustWorkbenchFetchTests
{
    private const string MinimalManifest = "[package]\nname = \"tiny\"\nversion = \"0.1.0\"\nedition = \"2021\"\n";

    private static RustToolchainInfo WithoutCToolchain()
        => TestToolchains.With(
            OperatingSystemKind.DebianLinux,
            InstalledTool.Missing(RustTool.CCompiler, "'cc' was not found."),
            InstalledTool.Missing(RustTool.Linker, "'cc' was not found."));

    [Fact]
    public async Task FetchAsync_runs_cargo_fetch_when_only_the_C_toolchain_is_missing()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        sandbox.WriteFile("src/main.rs", "fn main() {}\n");
        var info = WithoutCToolchain();
        var workbench = new RustWorkbench(new RustWorkbenchOptions { CargoPath = CargoAvailability.CargoPath }, (_, _) => Task.FromResult(info));
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var result = await workbench.FetchAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        result.Command.Should().Be(CargoCommand.Fetch);
        result.CommandLine.Should().Contain("fetch");
        result.Readiness.Should().NotBeNull();
        result.Readiness.MissingTools.Should().BeEmpty();
        result.Artifacts.Should().BeEmpty();
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }

    [Fact]
    public async Task FetchAsync_throws_before_cargo_runs_when_cargo_is_missing()
    {
        //Arrange
        using var sandbox = SampleSandbox.CreateEmpty();
        sandbox.WriteFile("Cargo.toml", MinimalManifest);
        var info = TestToolchains.With(OperatingSystemKind.DebianLinux, InstalledTool.Missing(RustTool.Cargo, "'cargo' was not found."));
        var workbench = new RustWorkbench(new RustWorkbenchOptions(), (_, _) => Task.FromResult(info));
        var project = workbench.OpenProject(sandbox.Directory);

        //Act
        var act = () => workbench.FetchAsync(project, cancellationToken: TestContext.Current.CancellationToken);

        //Assert
        var exception = (await act.Should().ThrowAsync<ToolchainNotFoundException>()).Which;
        exception.Message.Should().Contain("cargo").And.Contain("cannot have its dependencies fetched");
        exception.Readiness.MissingTools.Should().Equal(RustTool.Cargo);
        exception.Guidance.Should().NotBeEmpty();
    }

    [Fact]
    public async Task FetchAsync_fills_an_empty_cargo_home_so_that_offline_metadata_succeeds()
    {
        //Arrange
        CargoAvailability.SkipIfMissing();
        using var sandbox = SampleSandbox.FromSample(2);
        using var cargoHome = new TemporaryFolder();
        var workbench = new RustWorkbench(new RustWorkbenchOptions { CargoHome = cargoHome.Path });
        var project = workbench.OpenProject(sandbox.Directory);
        var progress = new CollectingProgress();

        //Act
        var result = await workbench.FetchAsync(project, new CargoBuildOptions { Locked = true }, progress, TestContext.Current.CancellationToken);
        var metadata = await project.GetMetadataAsync(new CargoMetadataOptions { Offline = true, Locked = true }, TestContext.Current.CancellationToken);

        //Assert
        result.Success.Should().BeTrue(result.StandardError);
        progress.Reports.Should().Contain(p => p.Phase == CargoProgressPhase.Downloading && p.CrateName == "clap");
        metadata.Packages.Select(p => p.Name).Should().Contain(new[] { "clap", "serde", "anyhow", "csv" });
        Directory.EnumerateFiles(Path.Combine(cargoHome.Path, "registry", "cache"), "*.crate", SearchOption.AllDirectories).Should().NotBeEmpty();
        Directory.Exists(Path.Combine(sandbox.Directory, "target")).Should().BeFalse();
    }

    /// <summary>
    /// A short-named folder under the system temp folder (deleted on Dispose). A CARGO_HOME
    /// holds deeply nested crate sources, so it is kept out of the long test output path.
    /// </summary>
    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cbrt-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // Best effort; the folder lives under the system temp folder.
            }
            catch (UnauthorizedAccessException)
            {
                // Best effort.
            }
        }
    }
}
