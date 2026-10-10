using System;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// One progress report from a running cargo command, derived from the status lines cargo writes
/// to standard error (<c>Compiling foo v1.2.3</c>, <c>Finished</c>, ...) and from the artifacts it
/// reports as each crate completes.
/// </summary>
public sealed class CargoProgress
{
    /// <summary>
    /// What happened.
    /// </summary>
    public CargoProgressPhase Phase { get; init; }

    /// <summary>
    /// The crate the event is about (for example <c>serde</c>), or <c>null</c> when the event is
    /// not about one crate.
    /// </summary>
    public string CrateName { get; init; }

    /// <summary>
    /// The crate version (for example <c>1.0.228</c>), or <c>null</c> when unknown.
    /// </summary>
    public string CrateVersion { get; init; }

    /// <summary>
    /// The number of distinct crates whose compilation has completed so far in this command
    /// (crates that were already up to date count as they are reported).
    /// </summary>
    public int Completed { get; init; }

    /// <summary>
    /// The expected number of crates - an ESTIMATE, not a promise. It is
    /// <see cref="CargoBuildOptions.ExpectedCrateCount"/> when the caller set it; otherwise
    /// <see cref="RustWorkbench"/> (and the Docker workbench) estimate it before a compiling
    /// command as the number of distinct packages reachable from the selected packages over
    /// normal and build dependency edges in <c>cargo metadata --filter-platform</c> for the
    /// build's target (dev-dependencies of the selected packages only for <c>cargo test</c>,
    /// all-targets and examples). Feature unification and target-specific details can still
    /// make <see cref="Completed"/> end a little below or above it. <c>null</c> when unknown
    /// (<see cref="CargoRunner"/> used directly without an expected count, or
    /// <c>cargo metadata</c> failed).
    /// </summary>
    public int? Total { get; init; }

    /// <summary>
    /// The cargo line (trimmed) that produced the event, or a short description for
    /// <see cref="CargoProgressPhase.CrateCompleted"/>.
    /// </summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// The fraction completed (0..1) when <see cref="Total"/> is known and positive; otherwise
    /// <c>null</c>.
    /// </summary>
    public double? Fraction => Total.HasValue && Total.Value > 0 ? Math.Min(1.0, (double)Completed / Total.Value) : null;

    /// <summary>
    /// Returns <see cref="Message"/> prefixed with the counts, for logs.
    /// </summary>
    public override string ToString()
        => Total.HasValue ? $"[{Completed}/{Total.Value}] {Message}" : $"[{Completed}] {Message}";
}
