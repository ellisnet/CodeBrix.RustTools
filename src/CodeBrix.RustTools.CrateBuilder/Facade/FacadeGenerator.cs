using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.RustTools.CrateBuilder.Facade.Extraction;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Rendering;
using CodeBrix.RustTools.CrateBuilder.Internal.Pipeline;
using CodeBrix.RustTools.Projects;

namespace CodeBrix.RustTools.CrateBuilder.Facade;

/// <summary>
/// The pipeline's facade stages behind <see cref="IFacadeGenerator"/>:
/// <list type="number">
/// <item><see cref="CrateBuildStage.ExtractingApi"/>: <see cref="ApiExtractor"/> reads the staged
/// crate's public API;</item>
/// <item><see cref="CrateBuildStage.Planning"/>: <see cref="MappingPlanner"/> plans the C mapping
/// from it, the request's <see cref="WrapPlan"/> and <see cref="PlannerOptions.FromRequest"/>, and
/// <see cref="WrapReportWriter"/> writes <c>WRAP-REPORT.txt</c> (and its JSON twin) into the run
/// folder;</item>
/// <item><see cref="CrateBuildStage.RenderingFacade"/>: <see cref="FacadeRenderer"/> writes the
/// facade crate, or - for a crate that is already a cdylib with C exports (pass-through) -
/// nothing.</item>
/// </list>
/// The result carries the plan (the wrapper writer renders the managed side from it), the
/// report path, the library name of the binary that will be built and its exports.
/// </summary>
internal sealed class FacadeGenerator : IFacadeGenerator
{
    /// <summary>The run sub-folder the wrap report is written to.</summary>
    public const string ReportFolderName = "report";

    private readonly ApiExtractor _extractor;
    private readonly MappingPlanner _planner;
    private readonly FacadeRenderer _renderer;

    /// <summary>A generator with the default extractor, planner and renderer.</summary>
    public FacadeGenerator()
        : this(new ApiExtractor(), new MappingPlanner(), new FacadeRenderer())
    {
    }

    /// <summary>A generator with the given parts.</summary>
    public FacadeGenerator(ApiExtractor extractor, MappingPlanner planner, FacadeRenderer renderer)
    {
        _extractor = extractor ?? throw new ArgumentNullException(nameof(extractor));
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
    }

    /// <inheritdoc/>
    public async Task<FacadeGenerationResult> GenerateAsync(StagedWorkspace workspace, CrateBuildRequest request, CrateBuilderContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var api = await _extractor.ExtractAsync(workspace, request, context, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        context.Report(CrateBuildStage.Planning, "Planning the C mapping of " + (api.Crate?.Name ?? "the crate") + ".");
        var plan = Plan(api, request, workspace);
        foreach (var warning in plan.Warnings)
        {
            context.Warnings.Add("Planning: " + warning);
        }

        var reportPath = Path.Combine(context.RunDirectory ?? context.ScratchRootDirectory, ReportFolderName, WrapReportWriter.DefaultFileName);
        try
        {
            WrapReportWriter.Write(plan, reportPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FacadeGenerationException("The wrap report could not be written: " + ex.Message, CrateBuildStage.Planning, ex);
        }

        context.Report(CrateBuildStage.Planning, $"Planned {plan.Functions.Count} exports, {plan.Handles.Count} object types, {plan.Mirrors.Count} structs and {plan.Enums.Count} enums; {plan.Skipped.Count} items are not wrapped (see {WrapReportWriter.DefaultFileName}).");
        cancellationToken.ThrowIfCancellationRequested();

        context.Report(CrateBuildStage.RenderingFacade, plan.UpstreamIsCdylib
            ? "The crate is already a cdylib with C exports: no facade is needed."
            : "Writing the facade crate " + plan.FacadeCrateName + ".");
        FacadeRenderResult rendered;
        try
        {
            rendered = _renderer.Render(plan, workspace, FacadeRenderOptions.FromRequest(request, api));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FacadeGenerationException("The facade crate could not be written: " + ex.Message, CrateBuildStage.RenderingFacade, ex);
        }

        var result = rendered.ToGenerationResult();
        result.Plan = plan;
        result.LibName = plan.LibName;
        result.WrapReportPath = reportPath;
        if (rendered.Generated)
        {
            context.Report(CrateBuildStage.RenderingFacade, $"Wrote the facade crate {rendered.FacadeCrateName} (library {rendered.LibName}, {rendered.Exports.Count} exports) in {rendered.Directory}.");
        }

        return result;
    }

    private MappingPlan Plan(CrateApiModel api, CrateBuildRequest request, StagedWorkspace workspace)
    {
        try
        {
            var options = PlannerOptions.FromRequest(request);
            ApplyFeatures(options, workspace.ManifestPath, request);
            return _planner.Plan(api, request.Wrapper?.Plan, options);
        }
        catch (FacadeGenerationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidDataException or NotSupportedException)
        {
            throw new FacadeGenerationException("The C mapping could not be planned: " + ex.Message, CrateBuildStage.Planning, ex);
        }
    }

    /// <summary>Fills <see cref="PlannerOptions.EnabledFeatures"/> and
    /// <see cref="PlannerOptions.KnownFeatures"/> from the staged crate's manifest, so an item
    /// behind a feature that is OFF for this build is not wrapped (the facade would not
    /// compile against it). A manifest that cannot be read leaves both unknown.</summary>
    internal static void ApplyFeatures(PlannerOptions options, string manifestPath, CrateBuildRequest request)
    {
        if (options == null || string.IsNullOrEmpty(manifestPath) || !File.Exists(manifestPath))
        {
            return;
        }

        CargoManifest manifest;
        try
        {
            manifest = CargoManifest.Load(manifestPath);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or FormatException or UnauthorizedAccessException or ArgumentException or RustToolsException)
        {
            return;
        }

        if (manifest?.Package == null)
        {
            return;
        }

        var resolution = FeatureResolver.Resolve(manifest, request?.Features, request != null && request.NoDefaultFeatures, request != null && request.AllFeatures);
        options.EnabledFeatures = resolution.Enabled;
        options.KnownFeatures = resolution.Known;
    }

    /// <summary>The exported function names of a generation result, in order.</summary>
    public static string[] ExportNames(FacadeGenerationResult result)
        => (result?.ExportedFunctions ?? Array.Empty<FacadeExport>()).Where(e => e != null).Select(e => e.Name).ToArray();
}
