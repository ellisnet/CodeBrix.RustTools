using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.RustTools.CrateBuilder.Facade.Model;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Rendering;

/// <summary>
/// The settings of one <see cref="FacadeRenderer.Render"/> call that the mapping plan does not
/// carry: how the facade's <c>[dependencies] upstream</c> entry selects the upstream crate's
/// features, and (optionally) the upstream package name and version when the staged workspace's
/// identity does not hold them.
/// </summary>
internal sealed class FacadeRenderOptions
{
    /// <summary>Features of the upstream crate to switch on (written to the dependency's
    /// <c>features</c> list, ordinal order, duplicates removed).</summary>
    public IList<string> Features { get; set; } = new List<string>();

    /// <summary>Writes <c>default-features = false</c> on the upstream dependency.</summary>
    public bool NoDefaultFeatures { get; set; }

    /// <summary>Switches on every feature in <see cref="DeclaredFeatures"/> (a dependency entry
    /// cannot say "all features", so the declared ones are listed).</summary>
    public bool AllFeatures { get; set; }

    /// <summary>The features the upstream crate declares (from the API model); used only with
    /// <see cref="AllFeatures"/>.</summary>
    public IList<string> DeclaredFeatures { get; set; } = new List<string>();

    /// <summary>The upstream package name for the dependency's <c>package</c> key; <c>null</c> =
    /// the staged workspace identity's crate name, else the plan's <c>CrateName</c>.</summary>
    public string UpstreamPackageName { get; set; }

    /// <summary>The facade package version; <c>null</c> = the staged workspace identity's crate
    /// version, else "0.0.0".</summary>
    public string UpstreamVersion { get; set; }

    /// <summary>The options a build request asks for (features, default features, all features),
    /// with the declared features, package name and version taken from <paramref name="api"/> when
    /// given (an inherited version, reported as "workspace", is left to the staged identity).</summary>
    public static FacadeRenderOptions FromRequest(CrateBuildRequest request, CrateApiModel api = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new FacadeRenderOptions
        {
            Features = request.Features?.ToList() ?? new List<string>(),
            NoDefaultFeatures = request.NoDefaultFeatures,
            AllFeatures = request.AllFeatures,
            DeclaredFeatures = api?.Crate?.DeclaredFeatures?.ToList() ?? new List<string>(),
            UpstreamPackageName = api?.Crate?.Name,
            UpstreamVersion = api?.Crate?.Version == "workspace" ? null : api?.Crate?.Version,
        };
    }

    /// <summary>The feature list written to the dependency: <see cref="Features"/>, plus
    /// <see cref="DeclaredFeatures"/> (except "default") with <see cref="AllFeatures"/>; trimmed,
    /// blank entries dropped, distinct, ordinal order.</summary>
    public IReadOnlyList<string> EffectiveFeatures()
    {
        var all = (Features ?? new List<string>()).AsEnumerable();
        if (AllFeatures)
        {
            all = all.Concat((DeclaredFeatures ?? new List<string>()).Where(f => f != "default"));
        }

        return all.Where(f => !string.IsNullOrWhiteSpace(f)).Select(f => f.Trim()).Distinct(StringComparer.Ordinal).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }
}
