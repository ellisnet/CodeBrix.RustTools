using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Readiness;

/// <summary>
/// The real readiness check. Host mode: CodeBrix.RustTools' detection
/// (<see cref="RustWorkbench.DetectToolchainAsync"/>, from the local crate's folder when there
/// is one so its rust-toolchain.toml applies) and readiness evaluation, then
/// <see cref="HostTargetEvaluator"/> per target. Docker mode: the Docker workbench's engine
/// check, then <see cref="DockerTargetEvaluator"/> per target.
/// </summary>
internal sealed class ReadinessChecker : IReadinessChecker
{
    public async Task<CrateReadinessReport> CheckAsync(CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        var targets = request.Targets.ToList();
        if (request.Mode == BuildMode.Docker)
        {
            var engine = await context.DockerWorkbench.CheckEngineAsync(cancellationToken).ConfigureAwait(false);
            return ReadinessReportBuilder.ForDocker(targets, engine, request.LinuxBaseline);
        }

        var toolchain = await context.RustWorkbench.DetectToolchainAsync(ProjectDirectoryOf(request.Source), cancellationToken).ConfigureAwait(false);
        return ReadinessReportBuilder.ForHost(targets, toolchain, HostEvaluationSettings.FromOptions(context.Options));
    }

    /// <summary>The folder of a local source's Cargo.toml when it exists, else <c>null</c>
    /// (a clone does not exist yet when readiness runs).</summary>
    internal static string ProjectDirectoryOf(CrateSource source)
    {
        if (source is not LocalManifestSource local || string.IsNullOrWhiteSpace(local.ManifestPath))
        {
            return null;
        }

        var path = Path.GetFullPath(local.ManifestPath.Trim());
        if (Directory.Exists(path))
        {
            return path;
        }

        return File.Exists(path) ? Path.GetDirectoryName(path) : null;
    }
}
