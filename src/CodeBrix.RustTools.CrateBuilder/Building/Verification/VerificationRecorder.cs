using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// The pipeline's Verifying stage when the build drivers have already verified every artifact
/// (see <see cref="ModeBuildDriver"/>): it verifies nothing again. It reports one progress line
/// per compiled target and copies every verification note that starts with
/// <see cref="WarningPrefix"/> into the build's warnings (and so into the result and the
/// record).
/// </summary>
internal sealed class VerificationRecorder : Internal.Pipeline.IArtifactVerifier
{
    /// <summary>A verification note that starts with this text is a warning for the
    /// build.</summary>
    public const string WarningPrefix = WebGpu.WebGpuLinkVerifier.VerificationWarningPrefix;

    public Task VerifyAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        foreach (var target in compiledTargets ?? Array.Empty<CrateTargetResult>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var notes = target.VerificationNotes ?? Array.Empty<string>();
            var rid = target.Rid ?? CrateTargets.Rid(target.Target);
            foreach (var warning in notes.Where(n => n != null && n.StartsWith(WarningPrefix, StringComparison.Ordinal)))
            {
                var text = rid + ": " + warning.Substring(WarningPrefix.Length);
                if (!context.Warnings.Contains(text))
                {
                    context.Warnings.Add(text);
                }
            }

            context.Report(CrateBuildStage.Verifying, $"{rid}: {target.Outcome}, {notes.Count} verification note(s).", target.Target);
        }

        return Task.CompletedTask;
    }
}
