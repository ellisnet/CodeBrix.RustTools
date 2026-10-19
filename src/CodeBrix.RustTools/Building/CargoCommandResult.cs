using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.Licensing;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Building;

/// <summary>
/// The outcome of one cargo command run by <see cref="CargoRunner"/>: success, exit code, typed
/// diagnostics, the artifacts produced and the captured output.
/// </summary>
public class CargoCommandResult
{
    /// <summary>
    /// The <see cref="ExitCode"/> of a result whose license gate failed, so cargo was never
    /// started: -2. It cannot collide with cargo's own exit codes (never negative) or with -1
    /// (cargo was killed: cancelled or timed out).
    /// </summary>
    public const int LicenseGateFailedExitCode = -2;

    private bool _licenseGateBlocked;

    /// <summary>
    /// The subcommand that was run.
    /// </summary>
    public CargoCommand Command { get; init; }

    /// <summary>
    /// True when cargo exited with code 0 and was neither timed out nor cancelled.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// The exit code. Zero or positive: cargo's own exit code (0 for success, 101 for a failed
    /// build). -1: cargo was killed because the run was cancelled or timed out (see
    /// <see cref="WasCancelled"/> and <see cref="TimedOut"/>). -2
    /// (<see cref="LicenseGateFailedExitCode"/>): the license gate failed and cargo was never
    /// started (see <see cref="LicenseGateFailed"/>).
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// Every diagnostic in the order it was received, duplicates included (rustc reports the
    /// same warning once per target that compiles the file).
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> Diagnostics { get; init; } = Array.Empty<CompilerDiagnostic>();

    /// <summary>
    /// The distinct warnings: one entry per (package, rendered text). rustc's
    /// "N warnings emitted" summary lines are not included.
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> Warnings { get; init; } = Array.Empty<CompilerDiagnostic>();

    /// <summary>
    /// The distinct errors (including internal compiler errors): one entry per (package,
    /// rendered text), compiler errors first, then cargo-level errors. Summary lines such as
    /// "aborting due to ..." and "could not compile ..." are not included.
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> Errors { get; init; } = Array.Empty<CompilerDiagnostic>();

    /// <summary>
    /// The warnings printed by build scripts (<c>cargo::warning=</c>), distinct; they are also
    /// part of <see cref="Warnings"/>.
    /// </summary>
    public IReadOnlyList<CompilerDiagnostic> BuildScriptWarnings { get; init; } = Array.Empty<CompilerDiagnostic>();

    /// <summary>
    /// The number of distinct warnings (<see cref="Warnings"/>.Count).
    /// </summary>
    public int WarningCount => Warnings.Count;

    /// <summary>
    /// The number of distinct errors (<see cref="Errors"/>.Count).
    /// </summary>
    public int ErrorCount => Errors.Count;

    /// <summary>
    /// Every artifact cargo reported, in order, including fresh (already up to date) ones,
    /// libraries, build scripts and test harnesses.
    /// </summary>
    public IReadOnlyList<BuildArtifact> Artifacts { get; init; } = Array.Empty<BuildArtifact>();

    /// <summary>
    /// The artifacts a user would run: binaries and examples (not build scripts or test
    /// harnesses).
    /// </summary>
    public IReadOnlyList<BuildArtifact> RunnableArtifacts => Artifacts.Where(a => a.IsRunnable).ToList();

    /// <summary>
    /// Wall-clock time of the cargo process.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The display form of the command line that was run.
    /// </summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// The folder cargo ran in.
    /// </summary>
    public string WorkingDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Everything cargo wrote to standard error (status lines, cargo-level warnings and errors).
    /// </summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>
    /// The lines cargo (or the tools it ran) wrote to standard output that were NOT cargo JSON
    /// messages: libtest output for <see cref="CargoCommand.Test"/>, rustfmt's diff for
    /// <see cref="CargoCommand.Fmt"/>.
    /// </summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>
    /// The raw cargo JSON message lines, in order; empty unless
    /// <see cref="CargoBuildOptions.KeepRawOutput"/> was set.
    /// </summary>
    public IReadOnlyList<string> RawMessages { get; init; } = Array.Empty<string>();

    /// <summary>
    /// True when cargo was killed because <see cref="CargoBuildOptions.Timeout"/> elapsed.
    /// </summary>
    public bool TimedOut { get; init; }

    /// <summary>
    /// True when cargo was killed because the cancellation token was cancelled and
    /// <see cref="CargoBuildOptions.ThrowOnCancellation"/> was false.
    /// </summary>
    public bool WasCancelled { get; init; }

    /// <summary>
    /// The license audit that ran before the command (see
    /// <see cref="CargoBuildOptions.LicensePolicy"/>), or <c>null</c> when no gate ran. With
    /// <see cref="LicenseGateMode.ReportOnly"/> it is attached whether it passed or not, and a
    /// failed report here does not mean cargo was skipped (check
    /// <see cref="LicenseGateFailed"/> for that).
    /// </summary>
    public LicenseAuditReport LicenseAudit { get; internal set; }

    /// <summary>
    /// True when an enforcing license gate (<see cref="LicenseGateMode.Enforce"/>) ran and
    /// failed. cargo was then not run: the result has <see cref="Success"/> false,
    /// <see cref="ExitCode"/> <see cref="LicenseGateFailedExitCode"/> (-2), no diagnostics and no
    /// artifacts. Always false for <see cref="LicenseGateMode.ReportOnly"/>, even when
    /// <see cref="LicenseAudit"/> did not pass.
    /// </summary>
    public bool LicenseGateFailed => _licenseGateBlocked && LicenseAudit != null && !LicenseAudit.Passed;

    /// <summary>
    /// The toolchain readiness evaluation the workbench made before a compiling command (build,
    /// check, test, doc, clippy), or <c>null</c> for <c>clean</c>, <c>fmt</c> and results from
    /// <see cref="CargoRunner"/> used directly. It can hold non-blocking issues, such as a C
    /// toolchain whose status could not be determined
    /// (<see cref="ReadinessIssueKind.ToolStatusUnknown"/>).
    /// </summary>
    public ToolchainReadiness Readiness { get; internal set; }

    /// <summary>
    /// Creates the result returned when the license gate failed and cargo was not run (a
    /// <see cref="CargoTestResult"/> for <see cref="CargoCommand.Test"/>).
    /// </summary>
    internal static CargoCommandResult ForFailedLicenseGate(CargoCommand command, LicenseAuditReport report, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(report);
        CargoCommandResult result = command == CargoCommand.Test
            ? new CargoTestResult { Command = command, Success = false, ExitCode = LicenseGateFailedExitCode, WorkingDirectory = workingDirectory ?? string.Empty }
            : new CargoCommandResult { Command = command, Success = false, ExitCode = LicenseGateFailedExitCode, WorkingDirectory = workingDirectory ?? string.Empty };
        result.LicenseAudit = report;
        result._licenseGateBlocked = true;
        return result;
    }

    /// <summary>
    /// Returns a one-line summary (command, outcome, counts, duration).
    /// </summary>
    public override string ToString()
    {
        if (LicenseGateFailed)
        {
            return $"cargo {CargoRunner.SubcommandName(Command)} not run: the license audit failed ({LicenseAudit.RejectedCount} rejected, {LicenseAudit.UnresolvedCount} unresolved crate(s))";
        }

        var outcome = TimedOut ? "timed out" : WasCancelled ? "cancelled" : Success ? "succeeded" : $"failed (exit code {ExitCode})";
        return $"cargo {CargoRunner.SubcommandName(Command)} {outcome}: {ErrorCount} error(s), {WarningCount} warning(s), {Artifacts.Count} artifact(s) in {Duration.TotalSeconds:0.0} s";
    }
}
