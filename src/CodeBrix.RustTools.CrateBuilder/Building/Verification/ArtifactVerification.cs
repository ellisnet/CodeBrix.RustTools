using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Runs verifiers over a target's files and records the outcome on its
/// <see cref="CrateTargetResult"/>: every verifier's notes go to
/// <see cref="CrateTargetResult.VerificationNotes"/>; a rejection (or a verifier that throws)
/// makes the target <see cref="CrateTargetOutcome.Failed"/> with the reasons in its message.
/// The files stay where they are.
/// </summary>
internal static class ArtifactVerification
{
    /// <summary>Verifies every file of a compiled target. Targets that did not compile are
    /// left alone.</summary>
    public static void Apply(CrateTargetResult result, IReadOnlyList<IArtifactVerifier> verifiers, StagedWorkspace workspace)
    {
        if (result == null || result.Outcome != CrateTargetOutcome.Compiled || verifiers == null || verifiers.Count == 0)
        {
            return;
        }

        var notes = new List<string>(result.VerificationNotes ?? Array.Empty<string>());
        var failures = new List<string>();
        foreach (var path in result.ArtifactPaths ?? Array.Empty<string>())
        {
            foreach (var verifier in verifiers.Where(v => v != null))
            {
                try
                {
                    var found = verifier.Verify(path, result.Target, workspace, out var ok) ?? Array.Empty<string>();
                    notes.AddRange(found);
                    if (!ok)
                    {
                        failures.Add(found.Count == 0 ? $"{Path.GetFileName(path)} was rejected by {verifier.GetType().Name}." : string.Join(" ", found.Where(n => !string.IsNullOrWhiteSpace(n))));
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    var message = $"{Path.GetFileName(path)}: {verifier.GetType().Name} failed: {ex.Message}";
                    notes.Add(message);
                    failures.Add(message);
                }
            }
        }

        result.VerificationNotes = notes;
        if (failures.Count > 0)
        {
            result.Outcome = CrateTargetOutcome.Failed;
            result.Message = "Artifact verification failed: " + string.Join(" | ", failures);
        }
    }
}
