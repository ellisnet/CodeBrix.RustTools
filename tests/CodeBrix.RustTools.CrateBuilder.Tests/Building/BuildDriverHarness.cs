using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Tests.Helpers;
using CodeBrix.RustTools.Docker;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Building;

/// <summary>
/// One live build-driver run: a sandbox copy of a sample as the staged workspace (never
/// rust-samples/ itself), an output folder and a scratch run folder inside the sandbox, and a
/// <see cref="CrateBuilderContext"/> over the given workbenches. Times itself and appends the
/// duration to build-driver-log.txt in the test output folder.
/// </summary>
internal sealed class BuildDriverHarness : IDisposable
{
    private readonly SampleSandbox _sandbox;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private readonly string _name;

    public BuildDriverHarness(string name, int sample, RustWorkbench hostWorkbench = null, DockerRustWorkbench dockerWorkbench = null, CrateBuilderOptions options = null)
    {
        _name = name;
        _sandbox = SampleSandbox.FromSample(sample);
        WorkspaceDirectory = _sandbox.Directory;
        OutputDirectory = Path.Combine(Path.GetDirectoryName(_sandbox.Directory), "out");
        RunDirectory = Path.Combine(Path.GetDirectoryName(_sandbox.Directory), "run");
        Directory.CreateDirectory(RunDirectory);
        Workspace = new StagedWorkspace
        {
            Directory = WorkspaceDirectory,
            ManifestPath = Path.Combine(WorkspaceDirectory, "Cargo.toml"),
            UpstreamManifestPath = Path.Combine(WorkspaceDirectory, "Cargo.toml"),
            CrateName = RepoPaths.SampleName(sample),
            Identity = new CrateSourceIdentity { Kind = CrateSourceIdentity.LocalKind, CrateName = RepoPaths.SampleName(sample), CrateTypes = new[] { "cdylib", "rlib" } },
        };
        Context = new CrateBuilderContext(options ?? new CrateBuilderOptions(), RunDirectory, () => hostWorkbench, () => dockerWorkbench, p => Reports.Add(p));
    }

    public string WorkspaceDirectory { get; }

    public string OutputDirectory { get; }

    public string RunDirectory { get; }

    public StagedWorkspace Workspace { get; }

    public CrateBuilderContext Context { get; }

    public ConcurrentBag<CrateBuildProgress> Reports { get; } = new ConcurrentBag<CrateBuildProgress>();

    public CrateBuildRequest Request(BuildMode mode, params CrateTarget[] targets)
    {
        var request = new CrateBuildRequest
        {
            Source = new LocalManifestSource(Workspace.ManifestPath),
            Mode = mode,
            OutputDirectory = OutputDirectory,
            Targets = new List<CrateTarget>(targets),
        };
        return request;
    }

    public void Dispose()
    {
        try
        {
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "build-driver-log.txt"), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {_name}: {_stopwatch.Elapsed.TotalSeconds:F1} s\n");
        }
        catch (IOException)
        {
            // The log is informational.
        }

        _sandbox.Dispose();
    }
}
