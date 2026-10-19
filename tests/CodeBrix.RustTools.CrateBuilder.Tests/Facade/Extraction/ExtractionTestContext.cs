using System;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.Docker;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Extraction;

/// <summary>
/// Contexts and staged workspaces for the extractor tests.
/// </summary>
internal static class ExtractionTestContext
{
    /// <summary>The scratch root shared by the host-built tool across test runs (below the test
    /// output folder, so the tool is compiled once per checkout and configuration).</summary>
    public static string SharedScratchRoot => Path.Combine(AppContext.BaseDirectory, "cratebuilder-scratch");

    public static CrateBuilderContext Create(string scratchRoot, string runDirectory, RustWorkbench host = null, DockerRustWorkbench docker = null)
    {
        var hostWorkbench = host ?? new RustWorkbench();
        return new CrateBuilderContext(
            new CrateBuilderOptions { ScratchRootDirectory = scratchRoot },
            runDirectory,
            () => hostWorkbench,
            () => docker,
            null);
    }

    public static StagedWorkspace Workspace(string directory, string crateName = null) => new StagedWorkspace
    {
        Directory = directory,
        ManifestPath = Path.Combine(directory, "Cargo.toml"),
        UpstreamManifestPath = Path.Combine(directory, "Cargo.toml"),
        CrateName = crateName,
    };
}
