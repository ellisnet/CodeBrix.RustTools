using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.Docker.Artifacts;
using CodeBrix.RustTools.Docker.Engine;
using CodeBrix.RustTools.Docker.Images;
using CodeBrix.RustTools.Licensing;

namespace CodeBrix.RustTools.Docker.Builds;

/// <summary>
/// The outcome of <see cref="DockerRustWorkbench.BuildAsync"/>: the engine, image and
/// container used, the cargo result per target (the sibling package's
/// <see cref="CargoCommandResult"/>, with container paths), the license audit, the retrieved
/// files and how long each stage took.
/// </summary>
public sealed class ContainerBuildResult
{
    /// <summary>The engine check made at the start.</summary>
    public DockerEngineInfo Engine { get; init; }

    /// <summary>The build image used.</summary>
    public RustBuildImage Image { get; init; }

    /// <summary>The build container's id.</summary>
    public string ContainerId { get; init; } = string.Empty;

    /// <summary>The build container's name.</summary>
    public string ContainerName { get; init; } = string.Empty;

    /// <summary>Whether the container was removed at the end of the build (otherwise it is
    /// still running and can be reused).</summary>
    public bool ContainerRemoved { get; init; }

    /// <summary>The source that was built.</summary>
    public ContainerBuildSource Source { get; init; }

    /// <summary>
    /// The cargo result of every target that was attempted, in the order of
    /// <see cref="ContainerBuildRequest.Targets"/>. When an enforcing license gate failed every
    /// target holds a result with <see cref="CargoCommandResult.LicenseGateFailed"/> set (cargo
    /// was not run); with <see cref="LicenseGateMode.ReportOnly"/> every target was built and
    /// carries the audit in <see cref="CargoCommandResult.LicenseAudit"/>. A target not attempted (after an earlier failure) is absent.
    /// </summary>
    public IReadOnlyDictionary<CrossTarget, CargoCommandResult> Results { get; init; } = new Dictionary<CrossTarget, CargoCommandResult>();

    /// <summary>The license audit made inside the container, or <c>null</c> when the request
    /// had no <see cref="CargoBuildOptions.LicensePolicy"/>. With
    /// <see cref="LicenseGateMode.ReportOnly"/> it is set whether it passed or not, and a failed
    /// report does not make <see cref="Success"/> false.</summary>
    public LicenseAuditReport LicenseAudit { get; init; }

    /// <summary>The files copied to the host, or <c>null</c> when the request had no
    /// <see cref="ContainerBuildRequest.Retrieval"/>.</summary>
    public ArtifactRetrievalResult Retrieval { get; init; }

    /// <summary>How long each stage took, in the order the stages ran.</summary>
    public IReadOnlyDictionary<BuildStage, TimeSpan> StageDurations { get; init; } = new Dictionary<BuildStage, TimeSpan>();

    /// <summary>The whole build's wall-clock time.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>The stage in which the build failed (<see cref="BuildStage.AuditingLicenses"/>
    /// for a failed gate, <see cref="BuildStage.Building"/> for a failed cargo run), or
    /// <c>null</c> when it succeeded.</summary>
    public BuildStage? FailedStage { get; init; }

    /// <summary>True when the license gate (if any) passed and every requested target built
    /// successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Returns the outcome per target, for logs.</summary>
    public override string ToString()
        => (Success ? "Container build succeeded" : $"Container build failed in {FailedStage}")
           + ": " + string.Join("; ", Results.Select(pair => $"{pair.Key}: {pair.Value}"));
}
