using System.Collections.Generic;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// One dependency entry of a Cargo.toml that names a <c>path</c> or a <c>git</c> source,
/// found by <see cref="ManifestDependencyScanner"/>.
/// </summary>
internal sealed class ManifestDependencyEntry
{
    /// <summary>The TOML keys that lead to the entry's table, for example
    /// <c>["target", "cfg(unix)", "dependencies", "gamma"]</c>.</summary>
    public IReadOnlyList<string> TableKeys { get; set; }

    /// <summary>The dependency key (the last of <see cref="TableKeys"/>).</summary>
    public string Name { get; set; }

    /// <summary>The <c>path</c> value, or <c>null</c>.</summary>
    public string Path { get; set; }

    /// <summary>The <c>git</c> URL, or <c>null</c>.</summary>
    public string Git { get; set; }

    /// <summary>The <c>rev</c>, <c>tag</c> or <c>branch</c> of a git entry (the first that is
    /// set, in that order), or <c>null</c>.</summary>
    public string GitReference { get; set; }
}
