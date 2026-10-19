using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Stage <see cref="CrateBuildStage.Readiness"/> (implemented in WP1 by
/// <see cref="Readiness.ReadinessChecker"/>); a seam so pipeline tests can replace the real
/// toolchain / engine detection.
/// </summary>
internal interface IReadinessChecker
{
    /// <summary>Checks whether this machine can build the request, per target.</summary>
    Task<CrateReadinessReport> CheckAsync(CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken);
}
