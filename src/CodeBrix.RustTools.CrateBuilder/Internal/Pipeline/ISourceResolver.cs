using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.ResolvingSource"/>: turns a <see cref="CrateSource"/> into
/// a folder on this machine that holds the crate (a local folder as it is, or a clone at the
/// requested commit, branch or tag). Implemented by WP2.
/// </summary>
internal interface ISourceResolver
{
    /// <summary>Finds or fetches the source. Throws <see cref="SourceResolutionException"/>
    /// when it cannot.</summary>
    Task<ResolvedSource> ResolveAsync(CrateSource source, CrateBuilderContext context, CancellationToken cancellationToken);
}
