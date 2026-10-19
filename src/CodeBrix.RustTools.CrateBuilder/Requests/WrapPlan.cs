using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.CrateBuilder;

/// <summary>
/// Which parts of a crate's public API the wrapper covers and how. Without a plan everything
/// that can be mapped is wrapped. Patterns are globs over Rust paths inside the crate
/// (<c>*</c> = any run of characters, for example <c>geom::*</c> or <c>*::to_string</c>).
/// </summary>
public sealed class WrapPlan
{
    /// <summary>Types to wrap; empty = every type.</summary>
    public IList<string> IncludeTypes { get; set; } = new List<string>();

    /// <summary>Types never to wrap (applied after <see cref="IncludeTypes"/>).</summary>
    public IList<string> ExcludeTypes { get; set; } = new List<string>();

    /// <summary>Functions and methods to wrap; empty = every one.</summary>
    public IList<string> IncludeFunctions { get; set; } = new List<string>();

    /// <summary>Functions and methods never to wrap (applied after
    /// <see cref="IncludeFunctions"/>).</summary>
    public IList<string> ExcludeFunctions { get; set; } = new List<string>();

    /// <summary>C# names to use instead of the generated ones, keyed by Rust path.</summary>
    public IDictionary<string, string> Renames { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Hand-written exports that replace generated ones.</summary>
    public IList<FacadeOverride> Overrides { get; set; } = new List<FacadeOverride>();

    /// <summary>Paths of Rust files of your own to copy into the facade crate (for helpers
    /// your overrides call).</summary>
    public IList<string> AdditionalFacadeFiles { get; set; } = new List<string>();

    /// <summary>Free text kept with the plan (copied into the wrap report).</summary>
    public string Notes { get; set; }

    internal WrapPlan Clone() => new WrapPlan
    {
        IncludeTypes = new List<string>(IncludeTypes ?? Enumerable.Empty<string>()),
        ExcludeTypes = new List<string>(ExcludeTypes ?? Enumerable.Empty<string>()),
        IncludeFunctions = new List<string>(IncludeFunctions ?? Enumerable.Empty<string>()),
        ExcludeFunctions = new List<string>(ExcludeFunctions ?? Enumerable.Empty<string>()),
        Renames = new Dictionary<string, string>(Renames ?? new Dictionary<string, string>(), StringComparer.Ordinal),
        Overrides = (Overrides ?? Enumerable.Empty<FacadeOverride>()).Select(o => o?.Clone()).ToList(),
        AdditionalFacadeFiles = new List<string>(AdditionalFacadeFiles ?? Enumerable.Empty<string>()),
        Notes = Notes,
    };
}
