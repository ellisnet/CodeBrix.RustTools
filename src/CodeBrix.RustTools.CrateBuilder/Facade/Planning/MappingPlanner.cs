using System;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;
using CodeBrix.RustTools.CrateBuilder.Facade.Planning.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// The tier-1 mapping planner (plan D10): turns a crate's extracted public API
/// (<see cref="CrateApiModel"/>) and an optional consumer <see cref="WrapPlan"/> into a
/// <see cref="MappingPlan"/> - mirrors, enums, handles, the C exports and how each calls
/// upstream, and a plain-language reason plus an override stub for everything it skips.
/// Without a WrapPlan everything mappable is wrapped. The result is deterministic: the same
/// input gives the same plan, list for list.
/// </summary>
internal sealed class MappingPlanner
{
    /// <summary>The name under which the facade crate depends on the upstream crate; every
    /// <see cref="ExportedFunction.RustCallExpression"/> starts with it.</summary>
    public const string UpstreamCrateAlias = "upstream";

    /// <summary>The local the facade binds the receiver to in
    /// <see cref="ExportedFunction.RustCallExpression"/>.</summary>
    public const string ReceiverLocal = "this";

    /// <summary>The C parameter name of a handle receiver.</summary>
    public const string HandleParameterName = "handle";

    /// <summary>The C parameter name of a mirror / enum receiver.</summary>
    public const string ValueReceiverParameterName = "self_value";

    /// <summary>The suffix of the generated last-error accessor (rendered by the facade's
    /// support code; reserved here so no export takes its name).</summary>
    public const string LastErrorFunctionSuffix = "last_error_message";

    /// <summary>The suffix of the buffer release function.</summary>
    public const string FreeBufferSuffix = "free_buffer";

    /// <summary>The suffix of the string release function.</summary>
    public const string FreeStringSuffix = "free_string";

    /// <summary>Plans the mapping.</summary>
    /// <param name="api">The extracted public API.</param>
    /// <param name="plan">The consumer's WrapPlan, or <c>null</c> for "wrap everything
    /// mappable".</param>
    /// <param name="options">Planner settings, or <c>null</c> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="api"/> is null.</exception>
    /// <exception cref="FacadeGenerationException">The crate has no library to wrap (stage
    /// <see cref="CrateBuildStage.Planning"/>).</exception>
    public MappingPlan Plan(CrateApiModel api, WrapPlan plan, PlannerOptions options)
    {
        if (api == null)
        {
            throw new ArgumentNullException(nameof(api));
        }

        return new PlannerRun(api, plan, options ?? new PlannerOptions()).Execute();
    }
}
