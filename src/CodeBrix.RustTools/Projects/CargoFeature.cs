using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One entry of a Cargo.toml <c>[features]</c> table.
/// </summary>
public sealed class CargoFeature
{
    /// <summary>The feature name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>What the feature enables, as written: other features, <c>dep:name</c> optional
    /// dependencies, or <c>crate/feature</c> entries.</summary>
    public IReadOnlyList<string> Enables { get; init; } = Array.Empty<string>();

    /// <summary>True for the special <c>default</c> feature.</summary>
    public bool IsDefault => string.Equals(Name, "default", StringComparison.Ordinal);

    /// <inheritdoc />
    public override string ToString() => $"{Name} = [{string.Join(", ", Enables)}]";
}
