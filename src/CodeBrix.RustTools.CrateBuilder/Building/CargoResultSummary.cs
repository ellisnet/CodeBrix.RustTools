using System;
using System.Linq;
using CodeBrix.RustTools.Building;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>A one-paragraph explanation of a failed cargo run, for a target's
/// message.</summary>
internal static class CargoResultSummary
{
    /// <summary>How many compiler errors the summary quotes.</summary>
    public const int QuotedErrorCount = 3;

    /// <summary>How many trailing standard-error lines are quoted when there is no compiler
    /// error.</summary>
    public const int QuotedStandardErrorLines = 8;

    public static string Describe(CargoCommandResult result)
    {
        if (result == null)
        {
            return "cargo did not run.";
        }

        if (result.LicenseGateFailed)
        {
            return "The licence gate failed, so cargo did not run: " + (result.LicenseAudit?.ToSummaryText() ?? "no audit report.");
        }

        if (result.TimedOut)
        {
            return $"cargo timed out after {result.Duration}.";
        }

        var head = $"cargo failed (exit code {result.ExitCode})";
        var errors = result.Errors.Take(QuotedErrorCount).Select(e => e.Message).Where(m => !string.IsNullOrWhiteSpace(m)).ToList();
        if (errors.Count > 0)
        {
            return head + ": " + string.Join(" | ", errors) + (result.ErrorCount > errors.Count ? $" (and {result.ErrorCount - errors.Count} more)" : string.Empty);
        }

        var lines = (result.StandardError ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? head + "." : head + ": " + string.Join(" | ", lines.Skip(Math.Max(0, lines.Length - QuotedStandardErrorLines)));
    }
}
