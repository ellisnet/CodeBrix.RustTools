using System.Collections.Generic;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Checks one native file a build produced. The build drivers run every verifier on every
/// artifact; a rejected artifact makes its target <see cref="CrateTargetOutcome.Failed"/> (the
/// file stays in the output folder for inspection).
/// </summary>
internal interface IArtifactVerifier
{
    /// <summary>
    /// Verifies <paramref name="artifactPath"/>, built for <paramref name="target"/>, and
    /// returns what it found (one line each). Sets <paramref name="ok"/> to false when the
    /// artifact must be rejected; the returned lines then hold the reason(s).
    /// </summary>
    IReadOnlyList<string> Verify(string artifactPath, CrateTarget target, StagedWorkspace workspace, out bool ok);
}
