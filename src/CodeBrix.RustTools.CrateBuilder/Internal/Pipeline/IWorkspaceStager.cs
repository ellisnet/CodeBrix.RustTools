using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.Staging"/>: copies the resolved crate (with its workspace
/// and path-dependency closure) into a scratch folder under
/// <see cref="CrateBuilderContext.RunDirectory"/> that the build may write to, and fills in the
/// source identity (crate name, version, crate types, source hash). Implemented by WP2.
/// </summary>
internal interface IWorkspaceStager
{
    /// <summary>Stages the crate. Throws <see cref="StagingException"/> when it
    /// cannot.</summary>
    Task<StagedWorkspace> StageAsync(ResolvedSource source, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken);
}
