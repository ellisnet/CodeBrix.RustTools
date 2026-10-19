using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;

/// <summary>
/// Runs one build through the stages in order: Readiness, ResolvingSource, Staging, the facade
/// stages (ExtractingApi / Planning / RenderingFacade, when needed - see
/// <see cref="NeedsFacade"/>; with <see cref="FacadeMode.Auto"/> a cdylib built as it is that
/// exports nothing is routed through them and built again, <see cref="NeedsAutoFallback"/>), Building, Verifying,
/// Wrapping (when asked for), Recording and CleaningUp. A stage that throws ends the build
/// with <see cref="CrateBuildResult.FailedStage"/> and <see cref="CrateBuildResult.FailureMessage"/>
/// set; the record is still written (when the build got past Readiness) and cleanup still
/// runs. Only the caller's cancellation propagates (after cleanup).
/// </summary>
internal sealed class CrateBuildPipeline
{
    private readonly CrateBuilderStages _stages;

    public CrateBuildPipeline(CrateBuilderStages stages)
    {
        _stages = stages ?? throw new ArgumentNullException(nameof(stages));
        _stages.EnsureComplete();
    }

    /// <summary>Whether the facade stages run for a staged crate before it is built:
    /// <see cref="FacadeMode.Always"/> = yes, <see cref="FacadeMode.Never"/> = no;
    /// <see cref="FacadeMode.Auto"/> = when a wrapper is asked for (its declarations come from
    /// the API), or when the crate does not declare a cdylib.</summary>
    public static bool NeedsFacade(CrateBuildRequest request, StagedWorkspace workspace) => request.FacadeMode switch
    {
        FacadeMode.Always => true,
        FacadeMode.Never => false,
        _ => request.Wrapper != null
             || workspace?.Identity?.CrateTypes == null
             || !workspace.Identity.CrateTypes.Contains("cdylib", StringComparer.Ordinal),
    };

    /// <summary>Why the facade stages run before the build (the record's
    /// <see cref="FacadeRecord.Reason"/>).</summary>
    public static string FacadeReason(CrateBuildRequest request, StagedWorkspace workspace)
        => request.FacadeMode == FacadeMode.Always ? "FacadeMode.Always was requested."
            : request.Wrapper != null ? "A wrapper was requested; its declarations come from the crate's API."
            : workspace?.Identity?.CrateTypes == null ? "The crate's crate types are unknown, so a facade gives it a C interface."
            : "The crate is not a cdylib, so a facade gives it a C interface.";

    /// <summary>The reason recorded when <see cref="FacadeMode.Auto"/> routes a cdylib that
    /// exports nothing through the facade stages.</summary>
    public const string AutoFallbackReason = "FacadeMode.Auto fallback: the cdylib was built as it is but exports no symbols, so the same build ran the facade stages (extract, plan, render) and built the generated facade.";

    /// <summary>Whether <see cref="FacadeMode.Auto"/> must route the build through the facade
    /// stages after building the crate as it is: no facade ran yet and a target failed
    /// verification because its library exports nothing.</summary>
    public static bool NeedsAutoFallback(CrateBuildRequest request, FacadeGenerationResult facade, IEnumerable<CrateTargetResult> targets)
        => request.FacadeMode == FacadeMode.Auto
           && facade == null
           && (targets ?? Enumerable.Empty<CrateTargetResult>()).Any(t => t.Outcome == CrateTargetOutcome.Failed
                && t.Message != null && t.Message.Contains(Building.Verification.DefaultArtifactVerifier.NoExportsNote, StringComparison.Ordinal));

    /// <summary>The provenance a target's files get unless a stage decides otherwise: macOS
    /// files are distributable only when built in host mode on a Mac.</summary>
    public static ArtifactProvenance DefaultProvenance(CrateTarget target, BuildMode mode, OperatingSystemKind hostOperatingSystem)
        => !CrateTargets.IsMacOS(target) || (mode == BuildMode.Host && hostOperatingSystem == OperatingSystemKind.MacOS)
            ? ArtifactProvenance.Distributable
            : ArtifactProvenance.DevelopmentBuild;

    /// <summary>Runs the build. <paramref name="request"/> has been validated (and copied) by
    /// the caller.</summary>
    public async Task<CrateBuildResult> RunAsync(CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var started = DateTimeOffset.UtcNow;
        var outputDirectory = Path.GetFullPath(request.OutputDirectory);
        var hostOs = HostOperatingSystem.Detect();
        var targets = request.Targets.Select(t => new CrateTargetResult
        {
            Target = t,
            Rid = CrateTargets.Rid(t),
            Triple = CrateTargets.Triple(t),
            Outcome = CrateTargetOutcome.Skipped,
            Provenance = DefaultProvenance(t, request.Mode, hostOs),
        }).ToList();
        var result = new CrateBuildResult { Mode = request.Mode, OutputDirectory = outputDirectory, Targets = targets };

        // ---- Readiness: a failure here ends the build without a record. ----
        context.Report(CrateBuildStage.Readiness, $"Checking that this machine can build in {request.Mode} mode.");
        CrateReadinessReport readiness;
        try
        {
            readiness = await _stages.Readiness.CheckAsync(request, context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.FailedStage = CrateBuildStage.Readiness;
            result.FailureMessage = "The readiness check failed: " + ex.Message;
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        readiness ??= new CrateReadinessReport { Mode = request.Mode, UnbuildableTargets = request.Targets.ToList() };
        context.Readiness = readiness;
        result.Readiness = readiness;
        foreach (var target in targets.Where(t => readiness.UnbuildableTargets.Contains(t.Target)))
        {
            target.Outcome = CrateTargetOutcome.NotBuildableInThisMode;
            target.Guidance = readiness.GetGuidance(target.Target);
            var reasons = readiness.Issues.Where(i => i.Target == target.Target).Select(i => i.Message).ToList();
            target.Message = reasons.Count > 0 ? string.Join(" ", reasons) : $"Not buildable in {request.Mode} mode on this machine.";
        }

        if (!readiness.IsReady)
        {
            var blocking = readiness.Issues.Where(i => i.IsBlocking).Select(i => i.Message).ToList();
            result.FailedStage = CrateBuildStage.Readiness;
            result.FailureMessage = blocking.Count > 0
                ? "This machine cannot build the request: " + string.Join(" ", blocking)
                : "This machine cannot build any of the requested targets.";
            result.Warnings = context.Warnings.ToList();
            result.Elapsed = stopwatch.Elapsed;
            return result;
        }

        // ---- The stages after Readiness. ----
        var stage = CrateBuildStage.ResolvingSource;
        ResolvedSource resolved = null;
        FacadeGenerationResult facade = null;
        string facadeReason = null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (request.Timeout.HasValue)
        {
            timeout.CancelAfter(request.Timeout.Value);
        }

        var token = timeout.Token;
        try
        {
            try
            {
                Directory.CreateDirectory(context.RunDirectory);

                context.Report(stage, "Resolving the source: " + request.Source);
                resolved = await _stages.SourceResolver.ResolveAsync(request.Source, context, token).ConfigureAwait(false);
                result.Source = resolved?.Identity;

                stage = CrateBuildStage.Staging;
                context.Report(stage, "Staging the crate in the scratch workspace.");
                var staged = await _stages.WorkspaceStager.StageAsync(resolved, request, context, token).ConfigureAwait(false)
                    ?? throw new CrateBuilderException("The stager returned no workspace.", CrateBuildStage.Staging);
                result.Source = staged.Identity ?? result.Source;

                async Task GenerateFacadeAsync(string reason)
                {
                    stage = CrateBuildStage.ExtractingApi;
                    context.Report(stage, "Reading the crate's public API.");
                    facadeReason = reason;
                    facade = await _stages.FacadeGenerator.GenerateAsync(staged, request, context, token).ConfigureAwait(false);
                    if (facade != null && facade.Generated)
                    {
                        staged.ManifestPath = facade.ManifestPath ?? Path.Combine(facade.FacadeDirectory ?? string.Empty, "Cargo.toml");
                        staged.IsFacade = true;
                    }

                    // The base of the native file name: the facade's [lib] name (the wrapper
                    // imports exactly that name), never the facade's package name.
                    staged.CrateName = facade?.LibName ?? staged.CrateName;
                }

                async Task BuildTargetsAsync()
                {
                    stage = CrateBuildStage.Building;
                    var buildable = targets.Where(t => t.Outcome != CrateTargetOutcome.NotBuildableInThisMode).Select(t => t.Target).ToList();
                    var buildRequest = request.Clone();
                    buildRequest.Targets = buildable;
                    context.Report(stage, "Building " + string.Join(", ", buildable.Select(CrateTargets.Rid)) + ".");
                    var built = await _stages.BuildDriver.BuildAsync(staged, buildRequest, context, context.Progress, token).ConfigureAwait(false)
                        ?? Array.Empty<CrateTargetResult>();
                    for (var i = 0; i < targets.Count; i++)
                    {
                        var replacement = built.FirstOrDefault(b => b != null && b.Target == targets[i].Target);
                        if (replacement != null)
                        {
                            replacement.Rid ??= targets[i].Rid;
                            replacement.Triple ??= targets[i].Triple;
                            targets[i] = replacement;
                        }
                    }
                }

                if (NeedsFacade(request, staged))
                {
                    await GenerateFacadeAsync(FacadeReason(request, staged)).ConfigureAwait(false);
                }

                await BuildTargetsAsync().ConfigureAwait(false);
                if (NeedsAutoFallback(request, facade, targets))
                {
                    context.Warnings.Add("The cdylib built as it is exports no symbols, so the build was routed through the facade stages and a generated C facade was built instead (FacadeMode.Auto; set FacadeMode.Never to keep the crate as it is, or Always to skip the first attempt).");
                    await GenerateFacadeAsync(AutoFallbackReason).ConfigureAwait(false);
                    await BuildTargetsAsync().ConfigureAwait(false);
                }

                var compiled = targets.Where(t => t.Outcome == CrateTargetOutcome.Compiled).ToList();
                if (compiled.Count == 0)
                {
                    throw new CrateBuilderException("No target compiled: " + string.Join("; ", targets.Select(t => t.ToString())), CrateBuildStage.Building);
                }

                stage = CrateBuildStage.Verifying;
                context.Report(stage, "Verifying the built files.");
                await _stages.ArtifactVerifier.VerifyAsync(staged, compiled, request, context, token).ConfigureAwait(false);

                if (request.Wrapper != null)
                {
                    stage = CrateBuildStage.Wrapping;
                    context.Report(stage, "Writing the wrapper project " + request.Wrapper.AssemblyName + ".");
                    var stillCompiled = targets.Where(t => t.Outcome == CrateTargetOutcome.Compiled).ToList();
                    result.Wrapper = await _stages.WrapperWriter.WriteAsync(staged, stillCompiled, facade, request, context, token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                result.FailedStage = stage;
                result.FailureMessage = $"The build timed out after {request.Timeout} during the {stage} stage.";
            }
            catch (Exception ex)
            {
                var failedStage = stage;
                if (ex is CrateBuilderException crateBuilderException)
                {
                    crateBuilderException.AssignStageIfUnknown(stage);
                    failedStage = crateBuilderException.Stage;
                }

                result.FailedStage = failedStage;
                result.FailureMessage = ex.Message;
            }

            result.Targets = targets;

            // ---- Recording: always, once past Readiness. ----
            context.Report(CrateBuildStage.Recording, "Writing the build record.");
            try
            {
                result.WrapReportPath = CopyWrapReport(facade, outputDirectory, context);
                var recordPath = Path.Combine(outputDirectory, BuildRecord.DefaultFileName);
                var record = BuildRecordFactory.Create(request, result, context, started, DateTimeOffset.UtcNow, facade);
                if (record.Facade != null)
                {
                    record.Facade.Reason = facadeReason;
                }
                record.Save(recordPath);
                result.Record = record;
                result.BuildRecordPath = recordPath;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!result.FailedStage.HasValue)
                {
                    result.FailedStage = CrateBuildStage.Recording;
                    result.FailureMessage = "The build record could not be written: " + ex.Message;
                }
                else
                {
                    context.Warnings.Add("The build record could not be written: " + ex.Message);
                }
            }
        }
        finally
        {
            // ---- CleaningUp: always, also after a cancellation. ----
            context.Report(CrateBuildStage.CleaningUp, "Cleaning up.");
            CleanUp(resolved, context);
        }

        result.Targets = targets;
        result.Warnings = context.Warnings.ToList();
        result.Success = !result.FailedStage.HasValue && targets.All(t => t.Outcome == CrateTargetOutcome.Compiled);
        result.Elapsed = stopwatch.Elapsed;
        return result;
    }

    /// <summary>Copies the wrap report (and its JSON twin) next to the build record, so it
    /// survives the scratch clean-up; returns the copy's path, or <c>null</c> when there is no
    /// report.</summary>
    public static string CopyWrapReport(FacadeGenerationResult facade, string outputDirectory, CrateBuilderContext context)
    {
        var source = facade?.WrapReportPath;
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
        {
            return null;
        }

        try
        {
            Directory.CreateDirectory(outputDirectory);
            var destination = Path.Combine(outputDirectory, Path.GetFileName(source));
            if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.Ordinal))
            {
                File.Copy(source, destination, overwrite: true);
            }

            var json = Path.ChangeExtension(source, ".json");
            if (File.Exists(json))
            {
                var jsonDestination = Path.ChangeExtension(destination, ".json");
                if (!string.Equals(Path.GetFullPath(json), Path.GetFullPath(jsonDestination), StringComparison.Ordinal))
                {
                    File.Copy(json, jsonDestination, overwrite: true);
                }
            }

            return destination;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Warnings.Add("The wrap report could not be copied next to the build record: " + ex.Message);
            return null;
        }
    }

    private static void CleanUp(ResolvedSource resolved, CrateBuilderContext context)
    {
        try
        {
            resolved?.CleanUp?.Invoke();
        }
        catch (Exception ex)
        {
            context.Warnings.Add("Cleaning up the source failed: " + ex.Message);
        }

        if (context.Options.KeepScratch || string.IsNullOrEmpty(context.RunDirectory) || !Directory.Exists(context.RunDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(context.RunDirectory, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.Warnings.Add($"The scratch folder '{context.RunDirectory}' could not be deleted: {ex.Message}");
        }
    }
}
