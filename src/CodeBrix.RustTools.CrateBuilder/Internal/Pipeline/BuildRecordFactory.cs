using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using CodeBrix.WebGpu.Native;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Builds the <see cref="BuildRecord"/> of a run from the request, the result so far and the
/// context.
/// </summary>
internal static class BuildRecordFactory
{
    /// <summary>The informational version of this assembly (without a source-revision
    /// suffix).</summary>
    public static string CrateBuilderVersion => VersionOf(typeof(BuildRecordFactory).Assembly);

    /// <summary>The version of the referenced CodeBrix.WebGpu.Native assembly.</summary>
    public static string ReferencedWebGpuNativeVersion => VersionOf(typeof(CodeBrixWebGpuNative).Assembly);

    public static BuildRecord Create(CrateBuildRequest request, CrateBuildResult result, CrateBuilderContext context, DateTimeOffset started, DateTimeOffset finished, FacadeGenerationResult facade = null)
    {
        var record = new BuildRecord
        {
            CrateBuilderVersion = CrateBuilderVersion,
            StartedUtc = started,
            FinishedUtc = finished,
            Source = result.Source,
            Mode = request.Mode,
            Profile = request.Profile,
            Features = new List<string>(request.Features ?? Enumerable.Empty<string>()),
            NoDefaultFeatures = request.NoDefaultFeatures,
            Offline = request.Offline,
            Locked = request.Locked,
            ToolchainDescription = context.ToolchainDescription ?? DescribeToolchain(result.Readiness),
            WebGpuNativeVersion = request.LinkToCodeBrixWebGpu ? context.WebGpuNativeVersion ?? ReferencedWebGpuNativeVersion : null,
        };

        foreach (var target in result.Targets ?? Enumerable.Empty<CrateTargetResult>())
        {
            record.Targets.Add(new TargetRecord
            {
                Rid = target.Rid,
                Triple = target.Triple,
                Outcome = target.Outcome,
                Provenance = target.Provenance,
                Message = target.Message,
                Artifacts = (target.ArtifactPaths ?? Array.Empty<string>()).Where(File.Exists).Select(p => Describe(p, result.OutputDirectory)).ToList(),
            });
        }

        if (facade != null)
        {
            var exports = (facade.ExportedFunctions ?? new List<FacadeExport>()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.Name)).Select(e => e.Name).ToList();
            record.Facade = new FacadeRecord
            {
                Generated = facade.Generated,
                LibName = facade.LibName ?? facade.Plan?.LibName,
                PackageName = facade.Generated ? facade.CrateName : null,
                ExportCount = exports.Count,
                Exports = exports,
            };
        }

        if (!string.IsNullOrWhiteSpace(result.WrapReportPath))
        {
            record.WrapReportFile = Path.GetFileName(result.WrapReportPath);
        }

        if (result.Wrapper != null && request.Wrapper != null)
        {
            record.Wrapper = new WrapperRecord
            {
                AssemblyName = request.Wrapper.AssemblyName,
                RootNamespace = request.Wrapper.RootNamespace,
                PackageId = request.Wrapper.Packaging?.PackageId,
                ProjectPath = result.Wrapper.ProjectPath,
                EntryClassName = result.Wrapper.EntryClassName,
            };
        }

        foreach (var warning in context.Warnings)
        {
            record.Warnings.Add(warning);
        }

        if (result.FailedStage.HasValue)
        {
            record.Warnings.Add($"Failed at {result.FailedStage.Value}: {result.FailureMessage}");
        }

        return record;
    }

    /// <summary>"rustc X, cargo Y, host T" (host mode) or "Docker engine V (arch) at endpoint"
    /// (Docker mode); <c>null</c> when nothing is known.</summary>
    public static string DescribeToolchain(CrateReadinessReport readiness)
    {
        if (readiness?.Toolchain != null)
        {
            var toolchain = readiness.Toolchain;
            return $"rustc {toolchain.Rustc.Version?.ToString() ?? "unknown"}, cargo {toolchain.Cargo.Version?.ToString() ?? "unknown"}, host {toolchain.HostTriple ?? "unknown"}";
        }

        if (readiness?.Engine != null)
        {
            var engine = readiness.Engine;
            return $"Docker engine {engine.ServerVersion ?? "unknown"} ({engine.Architecture ?? "unknown"}) at {engine.Endpoint ?? "unknown"}";
        }

        return null;
    }

    /// <summary>The record of one file: its path relative to the output folder (forward
    /// slashes), SHA-256 and length.</summary>
    public static ArtifactRecord Describe(string path, string outputDirectory)
    {
        var full = Path.GetFullPath(path);
        var relative = string.IsNullOrEmpty(outputDirectory) ? full : Path.GetRelativePath(outputDirectory, full);
        using var stream = File.OpenRead(full);
        var hash = Convert.ToHexStringLower(SHA256.HashData(stream));
        return new ArtifactRecord { Path = relative.Replace('\\', '/'), Sha256 = hash, Length = new FileInfo(full).Length };
    }

    private static string VersionOf(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+');
            return plus > 0 ? informational.Substring(0, plus) : informational;
        }

        return assembly.GetName().Version?.ToString();
    }
}
