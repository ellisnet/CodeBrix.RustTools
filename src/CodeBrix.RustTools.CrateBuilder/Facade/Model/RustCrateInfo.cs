using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Model;

/// <summary>
/// Crate metadata from Cargo.toml and the crate root.
/// </summary>
internal sealed class RustCrateInfo
{
    /// <summary><c>[package] name</c> as written.</summary>
    public string Name { get; set; }

    /// <summary><c>[package] version</c>; <c>workspace</c> when inherited.</summary>
    public string Version { get; set; }

    /// <summary><c>[package] edition</c>; <c>workspace</c> when inherited; <c>2015</c> when
    /// absent.</summary>
    public string Edition { get; set; }

    /// <summary><c>[lib] name</c>, else the package name with <c>-</c> -&gt; <c>_</c>.</summary>
    public string LibName { get; set; }

    /// <summary><c>[lib] crate-type</c>; <c>["lib"]</c> when absent.</summary>
    public List<string> CrateTypes { get; set; } = new List<string>();

    /// <summary>Which root was read.</summary>
    public RustRootKind RootKind { get; set; }

    /// <summary>The root file, relative (<c>src/lib.rs</c>).</summary>
    public string RootFile { get; set; }

    /// <summary>The manifest that was read, as the tool saw it (a container path in the
    /// container route).</summary>
    public string ManifestPath { get; set; }

    /// <summary>The package folder, as the tool saw it.</summary>
    public string PackageDirectory { get; set; }

    /// <summary>The keys of <c>[features]</c>.</summary>
    public List<string> DeclaredFeatures { get; set; } = new List<string>();

    /// <summary>The features passed with <c>--features</c>.</summary>
    public List<string> RequestedFeatures { get; set; } = new List<string>();

    /// <summary>The crate-level <c>//!</c> docs.</summary>
    public string Docs { get; set; } = string.Empty;

    /// <summary>Whether the library is already a C dynamic library (<c>cdylib</c>).</summary>
    [JsonIgnore]
    public bool IsCdylib => CrateTypes.Contains("cdylib", StringComparer.Ordinal);
}
