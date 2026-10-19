using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.Wrapping"/>: writes the .NET wrapper project around the
/// compiled targets (called only when <see cref="CrateBuildRequest.Wrapper"/> is set).
/// Implemented by WP8. Throws <see cref="WrapperGenerationException"/> when it cannot.
/// </summary>
internal interface IWrapperWriter
{
    /// <summary>Writes the wrapper project. <paramref name="facade"/> is <c>null</c> when no
    /// facade stage ran.</summary>
    Task<WrapperResult> WriteAsync(StagedWorkspace workspace, IReadOnlyList<CrateTargetResult> compiledTargets, FacadeGenerationResult facade, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken);
}
