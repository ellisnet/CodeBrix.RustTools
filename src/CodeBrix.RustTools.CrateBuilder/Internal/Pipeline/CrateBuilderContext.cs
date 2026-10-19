using System;
using System.Collections.Generic;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// What every stage of one build shares: the options, the run's scratch folder, the two
/// workbenches (created on first use and owned by the <see cref="CrateBuilder"/>), the
/// progress sink, and facts a stage leaves for the build record.
/// </summary>
internal sealed class CrateBuilderContext
{
    private readonly Func<RustWorkbench> _hostWorkbench;
    private readonly Func<DockerRustWorkbench> _dockerWorkbench;
    private readonly Action<CrateBuildProgress> _report;

    /// <summary>Creates a context.</summary>
    /// <param name="options">The builder's options.</param>
    /// <param name="runDirectory">This run's scratch folder (created by the pipeline before
    /// the source is resolved).</param>
    /// <param name="hostWorkbench">Returns the host workbench (created on first call).</param>
    /// <param name="dockerWorkbench">Returns the Docker workbench (created on first
    /// call).</param>
    /// <param name="report">Receives every progress report.</param>
    public CrateBuilderContext(CrateBuilderOptions options, string runDirectory, Func<RustWorkbench> hostWorkbench, Func<DockerRustWorkbench> dockerWorkbench, Action<CrateBuildProgress> report)
    {
        Options = options ?? new CrateBuilderOptions();
        RunDirectory = runDirectory;
        _hostWorkbench = hostWorkbench;
        _dockerWorkbench = dockerWorkbench;
        _report = report;
        Progress = new ProgressSink(this);
    }

    /// <summary>The builder's options.</summary>
    public CrateBuilderOptions Options { get; }

    /// <summary>The scratch root (<see cref="CrateBuilderOptions.EffectiveScratchRootDirectory"/>).</summary>
    public string ScratchRootDirectory => Options.EffectiveScratchRootDirectory;

    /// <summary>This run's scratch folder; deleted in the CleaningUp stage unless
    /// <see cref="CrateBuilderOptions.KeepScratch"/>.</summary>
    public string RunDirectory { get; }

    /// <summary>The host-mode workbench (CodeBrix.RustTools).</summary>
    public RustWorkbench RustWorkbench => _hostWorkbench?.Invoke() ?? throw new InvalidOperationException("No host workbench is available.");

    /// <summary>The Docker-mode workbench (CodeBrix.RustTools.Docker).</summary>
    public DockerRustWorkbench DockerWorkbench => _dockerWorkbench?.Invoke() ?? throw new InvalidOperationException("No Docker workbench is available.");

    /// <summary>The progress sink: everything reported here reaches the caller's progress and
    /// the builder's Progress event.</summary>
    public IProgress<CrateBuildProgress> Progress { get; }

    /// <summary>The readiness report of this run (set by the pipeline after the Readiness
    /// stage).</summary>
    public CrateReadinessReport Readiness { get; set; }

    /// <summary>A stage may set the toolchain description for the record (for example the
    /// Docker image reference and id); <c>null</c> = derived from the readiness report.</summary>
    public string ToolchainDescription { get; set; }

    /// <summary>The CodeBrix.WebGpu.Native version that was linked (set by the WebGPU link
    /// stage); <c>null</c> = the referenced package's version when the request links.</summary>
    public string WebGpuNativeVersion { get; set; }

    /// <summary>Warnings any stage adds; they go into the result and the record.</summary>
    public IList<string> Warnings { get; } = new List<string>();

    /// <summary>Reports progress.</summary>
    public void Report(CrateBuildStage stage, string message, CrateTarget? target = null, CargoProgress cargoProgress = null)
        => Report(new CrateBuildProgress { Stage = stage, Message = message, Target = target, CargoProgress = cargoProgress });

    /// <summary>Reports progress.</summary>
    public void Report(CrateBuildProgress progress)
    {
        if (progress != null)
        {
            _report?.Invoke(progress);
        }
    }

    private sealed class ProgressSink : IProgress<CrateBuildProgress>
    {
        private readonly CrateBuilderContext _context;

        public ProgressSink(CrateBuilderContext context)
        {
            _context = context;
        }

        public void Report(CrateBuildProgress value) => _context.Report(value);
    }
}
