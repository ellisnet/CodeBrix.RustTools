using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stages <see cref="CrateBuildStage.ExtractingApi"/>, <see cref="CrateBuildStage.Planning"/>
/// and <see cref="CrateBuildStage.RenderingFacade"/> behind one door: reads the staged crate's
/// public API, plans the C mapping and, for a crate that is not already a cdylib with C
/// exports, writes the facade crate. Implemented by WP5 (extraction), WP6 (planning) and WP7
/// (rendering). The pipeline calls it only when a wrapper is requested or the staged crate
/// does not declare a cdylib; it reports <see cref="CrateBuildStage.ExtractingApi"/> first and
/// the generator reports the later two itself through the context.
/// </summary>
internal interface IFacadeGenerator
{
    /// <summary>Generates the facade (or only reads the exports). Throws
    /// <see cref="FacadeGenerationException"/> with the stage that failed.</summary>
    Task<FacadeGenerationResult> GenerateAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken);
}
