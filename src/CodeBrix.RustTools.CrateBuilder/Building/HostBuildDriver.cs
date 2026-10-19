using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Building;
using CodeBrix.RustTools.CrateBuilder.Building.Verification;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.CrateBuilder.Internal.Readiness;
using CodeBrix.RustTools.Projects;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Building;

/// <summary>
/// Builds a staged crate with the Rust toolchain installed on this machine
/// (CodeBrix.RustTools' <see cref="RustWorkbench"/> from the context), one cargo run per
/// target, in request order:
/// <list type="number">
/// <item>the host's own triple: <c>cargo build --target &lt;host triple&gt;</c> (explicit, so the
/// output lands in <c>target/&lt;triple&gt;/</c> like every other target);</item>
/// <item>another Linux triple: <see cref="LinuxLinkProfile"/> for that one triple;</item>
/// <item>Android: <see cref="AndroidLinkProfile"/> on the detected NDK, at API level
/// <see cref="WebGpuAndroidApiLevel"/> when the request links CodeBrix WebGPU (the floor of the
/// Native package's Android binaries), else <see cref="AndroidLinkProfile.DefaultApiLevel"/>;</item>
/// <item>macOS: plain cargo on a Mac (distributable), else <see cref="MacOSLinkProfile"/> with
/// <see cref="CrateBuilderOptions.MacOSSysrootDirectory"/> (a development build);</item>
/// <item>Windows: <see cref="WindowsGnullvmLinkProfile"/> on the llvm-mingw the readiness
/// check found.</item>
/// </list>
/// With <see cref="CrateBuilderOptions.CargoTargetDirectory"/> every run uses its <c>host/</c>
/// sub-folder as cargo's target folder (<see cref="SharedCargoTarget"/>).
/// Targets the <see cref="TargetMatrix"/> rules out are returned as not buildable before any
/// cargo runs. Only THE crate's dynamic library is collected (not dependencies, not build
/// scripts or proc-macros), copied into the request's layout and verified.
/// </summary>
internal sealed class HostBuildDriver : IBuildDriver
{
    /// <summary>The Android API level used when the crate links CodeBrix WebGPU: the level the
    /// CodeBrix.WebGpu.Native Android binaries are built for.</summary>
    public const int WebGpuAndroidApiLevel = 33;

    private readonly IReadOnlyList<IBuildContribution> _contributions;
    private readonly IReadOnlyList<IArtifactVerifier> _verifiers;

    /// <summary>A driver with no contributions and the <see cref="DefaultArtifactVerifier"/>.</summary>
    public HostBuildDriver()
        : this(Array.Empty<IBuildContribution>(), new IArtifactVerifier[] { new DefaultArtifactVerifier() })
    {
    }

    /// <summary>A driver with the given contributions and verifiers (either may be
    /// empty).</summary>
    public HostBuildDriver(IReadOnlyList<IBuildContribution> contributions, IReadOnlyList<IArtifactVerifier> verifiers)
    {
        _contributions = contributions ?? Array.Empty<IBuildContribution>();
        _verifiers = verifiers ?? Array.Empty<IArtifactVerifier>();
    }

    /// <summary>What <see cref="BuildContribution.WorkspaceToken"/> stands for in host mode: the
    /// staging root on this machine (the built manifest's folder when there is none).</summary>
    public static string WorkspaceTokenValue(StagedWorkspace workspace, BuiltCrate crate)
        => Path.GetFullPath(workspace.Directory ?? Path.GetDirectoryName(crate.ManifestPath));

    /// <summary>The Android API level of a request.</summary>
    public static int AndroidApiLevelFor(CrateBuildRequest request)
        => request.LinkToCodeBrixWebGpu ? WebGpuAndroidApiLevel : AndroidLinkProfile.DefaultApiLevel;

    /// <summary>"rustc X, cargo Y, host T, toolchain N".</summary>
    public static string DescribeToolchain(RustToolchainInfo toolchain)
        => toolchain == null
            ? null
            : $"rustc {toolchain.Rustc?.Version?.ToString() ?? "unknown"}, cargo {toolchain.Cargo?.Version?.ToString() ?? "unknown"}, host {toolchain.HostTriple ?? "unknown"}, toolchain {toolchain.ActiveToolchain ?? "unknown"}";

    public async Task<IReadOnlyList<CrateTargetResult>> BuildAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var crate = BuiltCrate.From(workspace);
        var workbench = context.RustWorkbench;
        var settings = HostEvaluationSettings.FromOptions(context.Options);
        var toolchain = context.Readiness?.Mode == BuildMode.Host && context.Readiness.Toolchain != null
            ? context.Readiness.Toolchain
            : await workbench.DetectToolchainAsync(Path.GetDirectoryName(crate.ManifestPath), cancellationToken).ConfigureAwait(false);
        var readiness = context.Readiness?.Mode == BuildMode.Host
            ? context.Readiness
            : ReadinessReportBuilder.ForHost(request.Targets.ToList(), toolchain, settings);
        var matrix = TargetMatrix.Create(request, readiness);
        context.ToolchainDescription = DescribeToolchain(toolchain);

        var project = workbench.OpenProject(crate.ManifestPath);
        var hostOs = settings.HostOperatingSystem ?? toolchain.OperatingSystem;
        var results = new List<CrateTargetResult>();
        foreach (var target in request.Targets)
        {
            var ruledOut = matrix.NotBuildable.FirstOrDefault(r => r.Target == target);
            if (ruledOut != null)
            {
                results.Add(ruledOut);
                continue;
            }

            results.Add(await BuildTargetAsync(workbench, project, crate, workspace, request, context, toolchain, settings, hostOs, target, progress, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>The crate's dynamic library files among a host build's artifacts: artifacts of
    /// the crate's package (not build scripts, tests or proc-macros) whose kinds include
    /// cdylib or dylib, and only files in the target's own folder (<c>target/&lt;triple&gt;/</c>,
    /// never host-compiled helpers), each once.</summary>
    public static IReadOnlyList<string> SelectArtifacts(IEnumerable<BuildArtifact> artifacts, BuiltCrate crate, string triple)
    {
        var selected = new List<string>();
        var segment = "/" + triple + "/";
        foreach (var artifact in artifacts ?? Array.Empty<BuildArtifact>())
        {
            if (artifact == null || artifact.IsBuildScript || artifact.Profile?.Test == true || artifact.Kinds.Contains("proc-macro", StringComparer.Ordinal))
            {
                continue;
            }

            var samePackage = string.Equals(artifact.PackageName, crate.PackageName, StringComparison.Ordinal)
                || (!string.IsNullOrEmpty(artifact.ManifestPath) && string.Equals(Path.GetFullPath(artifact.ManifestPath), crate.ManifestPath, StringComparison.Ordinal));
            if (!samePackage || !artifact.Kinds.Any(k => k == "cdylib" || k == "dylib"))
            {
                continue;
            }

            foreach (var file in artifact.Filenames.Where(f => !string.IsNullOrWhiteSpace(f)))
            {
                var normalized = file.Replace('\\', '/');
                if (IsSharedLibrary(normalized) && normalized.Contains(segment, StringComparison.Ordinal) && !selected.Contains(file))
                {
                    selected.Add(file);
                }
            }
        }

        return selected;
    }

    /// <summary>Whether a file name is a dynamic library (.so, .dylib, .dll).</summary>
    public static bool IsSharedLibrary(string path)
    {
        var name = Path.GetFileName(path ?? string.Empty).ToLowerInvariant();
        return name.EndsWith(".so", StringComparison.Ordinal) || name.EndsWith(".dylib", StringComparison.Ordinal) || name.EndsWith(".dll", StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies the link settings of a target to the options (see the class remarks).
    /// </summary>
    /// <exception cref="CrateBuilderException">A tool the target needs cannot be used.</exception>
    public static void ApplyLinkSettings(CargoBuildOptions options, CrateTarget target, CrateBuildRequest request, BuiltCrate crate, RustToolchainInfo toolchain, HostEvaluationSettings settings, OperatingSystemKind hostOs)
    {
        var triple = CrateTargets.Triple(target);
        if (string.Equals(triple, toolchain.HostTriple, StringComparison.Ordinal) || (CrateTargets.IsMacOS(target) && hostOs == OperatingSystemKind.MacOS))
        {
            options.TargetTriple = triple;
            return;
        }

        if (CrateTargets.IsLinux(target))
        {
            LinuxLinkProfile.ForTriple(triple, settings.LinuxCrossToolDirectory).ApplyTo(options, triple);
        }
        else if (CrateTargets.IsAndroid(target))
        {
            var ndk = toolchain.AndroidNdk ?? throw new CrateBuilderException($"No Android NDK was found for {CrateTargets.Rid(target)}.", CrateBuildStage.Building);
            AndroidLinkProfile.ForNdk(ndk, AndroidApiLevelFor(request)).ApplyTo(options, triple);
        }
        else if (CrateTargets.IsMacOS(target))
        {
            MacOSLinkProfile.ForToolchain(toolchain, settings.MacOSSysrootDirectory).ApplyTo(options, triple, CargoOptionsFactory.MacOSInstallName(crate));
        }
        else if (CrateTargets.IsWindows(target))
        {
            var bin = LlvmMingwLocator.FindBinDirectory(triple, settings.LlvmMingwDirectory, null, null, hostOs == OperatingSystemKind.Windows)
                ?? throw new CrateBuilderException($"llvm-mingw was not found for {triple}.", CrateBuildStage.Building);
            new WindowsGnullvmLinkProfile(bin, hostOs == OperatingSystemKind.Windows).ApplyTo(options, triple);
        }
    }

    private async Task<CrateTargetResult> BuildTargetAsync(
        RustWorkbench workbench,
        CargoProject project,
        BuiltCrate crate,
        StagedWorkspace workspace,
        CrateBuildRequest request,
        CrateBuilderContext context,
        RustToolchainInfo toolchain,
        HostEvaluationSettings settings,
        OperatingSystemKind hostOs,
        CrateTarget target,
        IProgress<CrateBuildProgress> progress,
        CancellationToken cancellationToken)
    {
        var triple = CrateTargets.Triple(target);
        var result = new CrateTargetResult
        {
            Target = target,
            Rid = CrateTargets.Rid(target),
            Triple = triple,
            Outcome = CrateTargetOutcome.Failed,
            Provenance = CrateBuildPipeline.DefaultProvenance(target, BuildMode.Host, hostOs),
        };
        Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = $"Building {result.Rid} ({triple}) with the installed toolchain." });

        var options = CargoOptionsFactory.Create(request, crate, workspace.IsFacade);
        CargoOptionsFactory.AddUpstreamConfig(options, workspace);
        try
        {
            ApplyLinkSettings(options, target, request, crate, toolchain, settings, hostOs);
        }
        catch (Exception ex) when (ex is CrateBuilderException or FileNotFoundException or DirectoryNotFoundException or ArgumentException)
        {
            result.Outcome = CrateTargetOutcome.NotBuildableInThisMode;
            result.Message = "The link settings for " + triple + " cannot be applied: " + ex.Message;
            return result;
        }

        var contribution = await ContributionMerger.PrepareAsync(_contributions, workspace, target, BuildMode.Host, context, WorkspaceTokenValue(workspace, crate), cancellationToken).ConfigureAwait(false);
        foreach (var pair in contribution.Environment)
        {
            options.Environment[pair.Key] = pair.Value;
        }

        if (contribution.RustFlags.Count > 0)
        {
            var variable = CargoOptionsFactory.RustFlagsVariable(triple);
            options.Environment.TryGetValue(variable, out var existing);
            options.Environment[variable] = ContributionMerger.AppendFlags(existing, contribution.RustFlags);
        }

        SharedCargoTarget.ApplyHost(options, SharedCargoTarget.For(context.Options, SharedCargoTarget.HostFolderName));
        var cargoProgress = new SynchronousProgress<CargoProgress>(p => Report(context, progress, new CrateBuildProgress { Stage = CrateBuildStage.Building, Target = target, Message = p?.Message, CargoProgress = p }));
        CargoCommandResult cargo;
        try
        {
            cargo = await workbench.BuildAsync(project, options, cargoProgress, cancellationToken).ConfigureAwait(false);
        }
        catch (ToolchainNotFoundException ex)
        {
            result.Outcome = CrateTargetOutcome.NotBuildableInThisMode;
            result.Message = ex.Message;
            result.Guidance = ex.Guidance;
            return result;
        }
        catch (CargoCommandException ex)
        {
            result.Message = "cargo could not run: " + ex.Message;
            return result;
        }

        result.CargoResult = cargo;
        if (!cargo.Success)
        {
            result.Message = CargoResultSummary.Describe(cargo);
            return result;
        }

        var files = SelectArtifacts(cargo.Artifacts, crate, triple);
        if (files.Count == 0)
        {
            result.Message = $"cargo built {crate.PackageName} for {triple} but produced no dynamic library; the crate needs crate-type = [\"cdylib\"] in its [lib] section.";
            return result;
        }

        var destination = OutputLayout.DirectoryFor(request.OutputDirectory, request.Layout, target);
        Directory.CreateDirectory(destination);
        var copied = new List<string>();
        foreach (var file in files)
        {
            var path = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, path, overwrite: true);
            copied.Add(path);
        }

        result.ArtifactPaths = copied;
        result.Outcome = CrateTargetOutcome.Compiled;
        result.VerificationNotes = contribution.Notes.ToList();
        ArtifactVerification.Apply(result, _verifiers, workspace);
        return result;
    }

    private static void Report(CrateBuilderContext context, IProgress<CrateBuildProgress> progress, CrateBuildProgress value)
    {
        if (progress != null)
        {
            progress.Report(value);
        }
        else
        {
            context.Report(value);
        }
    }
}
