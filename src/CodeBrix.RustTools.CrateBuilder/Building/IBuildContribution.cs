using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Something that adds to the build of one target before cargo runs - files staged into the
/// workspace, environment variables, rustflags - for example the CodeBrix WebGPU link. The
/// build drivers call <see cref="PrepareAsync"/> once per target and merge what it returns.
/// </summary>
internal interface IBuildContribution
{
    /// <summary>
    /// Prepares the build of <paramref name="target"/> in <paramref name="mode"/>. Values in the
    /// returned contribution may hold the token <see cref="BuildContribution.WorkspaceToken"/>,
    /// which the driver replaces with the staged workspace root as cargo sees it (the host
    /// folder in host mode, the container folder in Docker mode).
    /// </summary>
    Task<BuildContribution> PrepareAsync(StagedWorkspace workspace, CrateTarget target, BuildMode mode, CrateBuilderContext context, CancellationToken cancellationToken);
}
