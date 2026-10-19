using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// The outcome of <see cref="CrateBuilder.BuildAsync"/>. A stage that fails is reported here
/// (<see cref="FailedStage"/>, <see cref="FailureMessage"/>), never thrown.
/// </summary>
public sealed class CrateBuildResult
{
    /// <summary>True when no stage failed and every requested target was
    /// <see cref="CrateTargetOutcome.Compiled"/>.</summary>
    public bool Success { get; set; }

    /// <summary>Which source was built; <c>null</c> when the source could not be resolved.</summary>
    public CrateSourceIdentity Source { get; set; }

    /// <summary>The mode of the request.</summary>
    public BuildMode Mode { get; set; }

    /// <summary>One result per requested target, in request order.</summary>
    public IReadOnlyList<CrateTargetResult> Targets { get; set; } = Array.Empty<CrateTargetResult>();

    /// <summary>The request's output folder (absolute).</summary>
    public string OutputDirectory { get; set; }

    /// <summary>Where the build record was written; <c>null</c> when none was (the readiness
    /// check failed, or writing it failed).</summary>
    public string BuildRecordPath { get; set; }

    /// <summary>The build record; <c>null</c> when the readiness check failed.</summary>
    public BuildRecord Record { get; set; }

    /// <summary>The wrapper project; <c>null</c> when none was asked for or written.</summary>
    public WrapperResult Wrapper { get; set; }

    /// <summary>The wrap report (<c>WRAP-REPORT.txt</c>, with its JSON twin
    /// <c>WRAP-REPORT.json</c>) beside the build record: what was wrapped, what was skipped and
    /// why, with a stub for each skipped item. <c>null</c> when the facade stages did not
    /// run.</summary>
    public string WrapReportPath { get; set; }

    /// <summary>The readiness check that ran first.</summary>
    public CrateReadinessReport Readiness { get; set; }

    /// <summary>Things that did not fail the build but deserve attention.</summary>
    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    /// <summary>The stage that failed, or <c>null</c>.</summary>
    public CrateBuildStage? FailedStage { get; set; }

    /// <summary>Why it failed, or <c>null</c>.</summary>
    public string FailureMessage { get; set; }

    /// <summary>How long the whole call took.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>A multi-line summary: the outcome, the failure, one line per target.</summary>
    public override string ToString()
    {
        var builder = new StringBuilder(Success ? "Succeeded" : "Failed");
        builder.Append(" (").Append(Mode).Append(" mode, ").Append(Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)).Append(" s)");
        if (FailedStage.HasValue)
        {
            builder.AppendLine().Append("  Failed at ").Append(FailedStage.Value).Append(": ").Append(FailureMessage);
        }

        foreach (var target in Targets ?? Enumerable.Empty<CrateTargetResult>())
        {
            builder.AppendLine().Append("  ").Append(target);
        }

        return builder.ToString();
    }
}
