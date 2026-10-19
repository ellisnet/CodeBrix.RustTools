using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Settings of one <see cref="MappingPlanner.Plan"/> call.
/// </summary>
internal sealed class PlannerOptions
{
    /// <summary>The wrapper's root namespace; <c>null</c> = the crate's library name in
    /// PascalCase.</summary>
    public string ManagedNamespace { get; set; }

    /// <summary>The wrapper's assembly name; <c>null</c> = <see cref="ManagedNamespace"/>.</summary>
    public string ManagedAssemblyName { get; set; }

    /// <summary>The build links the CodeBrix WebGPU engine (copied into the plan).</summary>
    public bool LinkToCodeBrixWebGpu { get; set; }

    /// <summary>The targets the build is for; items whose <c>cfg</c> rules them out on EVERY
    /// target are skipped. Empty = all nine.</summary>
    public IList<CrateTarget> Targets { get; set; } = new List<CrateTarget>();

    /// <summary>Also wrap <c>unsafe fn</c> items (the caller then owns their safety contract).
    /// Default false.</summary>
    public bool IncludeUnsafeFunctions { get; set; }

    /// <summary>Generate a C facade over the Rust API even when the crate is a cdylib that
    /// already has <c>extern "C"</c> exports (<see cref="FacadeMode.Always"/>). Default
    /// false: such a cdylib is passed through.</summary>
    public bool ForceFacade { get; set; }

    /// <summary>The crate's features that are ON for this build (see
    /// <see cref="FeatureResolver"/>); <c>null</c> = not known, and then a <c>feature = ".."</c>
    /// cfg is decided only by the extractor's requested features.</summary>
    public ICollection<string> EnabledFeatures { get; set; }

    /// <summary>Every feature name the crate has; a <c>feature = ".."</c> cfg naming one of
    /// them that is not in <see cref="EnabledFeatures"/> is OFF. <c>null</c> = not
    /// known.</summary>
    public ICollection<string> KnownFeatures { get; set; }

    /// <summary>The planner settings for a build request (wrapper names, WebGPU link,
    /// targets, <see cref="FacadeMode.Always"/>).</summary>
    public static PlannerOptions FromRequest(CrateBuildRequest request) => new PlannerOptions
    {
        ManagedNamespace = request?.Wrapper?.RootNamespace,
        ManagedAssemblyName = request?.Wrapper?.AssemblyName,
        LinkToCodeBrixWebGpu = request != null && request.LinkToCodeBrixWebGpu,
        ForceFacade = request != null && request.FacadeMode == FacadeMode.Always,
        Targets = (request?.Targets ?? Enumerable.Empty<CrateTarget>()).ToList(),
    };
}
