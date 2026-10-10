using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One build target of a package as reported by <c>cargo metadata</c>.
/// </summary>
public sealed class CargoMetadataTarget
{
    /// <summary>The target name.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The target kinds (usually one: <c>bin</c>, <c>lib</c>, <c>example</c>, ...).</summary>
    public IReadOnlyList<CargoTargetKind> Kinds { get; init; } = Array.Empty<CargoTargetKind>();

    /// <summary>The crate types (<c>bin</c>, <c>lib</c>, <c>rlib</c>, <c>cdylib</c>, ...).</summary>
    public IReadOnlyList<CargoTargetKind> CrateTypes { get; init; } = Array.Empty<CargoTargetKind>();

    /// <summary>The full path of the target's root source file.</summary>
    public string SrcPath { get; init; } = string.Empty;

    /// <summary>The Rust edition of the target.</summary>
    public string Edition { get; init; }

    /// <summary>The <c>required-features</c> of the target.</summary>
    public IReadOnlyList<string> RequiredFeatures { get; init; } = Array.Empty<string>();

    /// <summary>The first (primary) kind, or <see cref="CargoTargetKind.Unknown"/>.</summary>
    public CargoTargetKind Kind => Kinds.Count == 0 ? CargoTargetKind.Unknown : Kinds[0];

    /// <summary>True when the target has the given kind.</summary>
    /// <param name="kind">The kind to test.</param>
    public bool HasKind(CargoTargetKind kind) => Kinds.Contains(kind);

    /// <inheritdoc />
    public override string ToString() => $"{string.Join(",", Kinds.Select(CargoTargetKinds.ToCargoString))} {Name}";
}
