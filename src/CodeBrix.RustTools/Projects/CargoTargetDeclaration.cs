using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// A build target of a package: either declared in Cargo.toml (<c>[lib]</c>, <c>[[bin]]</c>,
/// <c>[[example]]</c>, <c>[[test]]</c>, <c>[[bench]]</c>, <c>build</c>) or discovered from
/// cargo's folder conventions.
/// </summary>
public sealed class CargoTargetDeclaration
{
    /// <summary>The target name (<c>null</c> for a <c>[lib]</c> or <c>[[bin]]</c> table that
    /// does not name itself; <see cref="CargoProject.GetDeclaredTargets"/> fills defaults in).</summary>
    public string Name { get; init; }

    /// <summary>The target kind.</summary>
    public CargoTargetKind Kind { get; init; }

    /// <summary>The source path relative to the package folder, with '/' separators, or
    /// <c>null</c> when not declared and not yet resolved.</summary>
    public string Path { get; init; }

    /// <summary>True when the target appears in Cargo.toml; false when it was found by cargo's
    /// folder conventions (<c>src/main.rs</c>, <c>src/bin/*.rs</c>, <c>examples/*.rs</c>, ...).</summary>
    public bool IsDeclared { get; init; }

    /// <summary>The <c>required-features</c> of the target.</summary>
    public IReadOnlyList<string> RequiredFeatures { get; init; } = Array.Empty<string>();

    /// <summary>The <c>crate-type</c> list of a library (empty when not declared).</summary>
    public IReadOnlyList<string> CrateTypes { get; init; } = Array.Empty<string>();

    /// <summary>False when the target sets <c>harness = false</c> (custom test or benchmark
    /// main function).</summary>
    public bool Harness { get; init; } = true;

    /// <summary>The <c>test</c> flag when set explicitly, or <c>null</c>.</summary>
    public bool? Test { get; init; }

    /// <summary>The <c>bench</c> flag when set explicitly, or <c>null</c>.</summary>
    public bool? Bench { get; init; }

    /// <summary>True for <c>proc-macro = true</c> libraries.</summary>
    public bool IsProcMacro { get; init; }

    /// <inheritdoc />
    public override string ToString() => $"{CargoTargetKinds.ToCargoString(Kind)} {Name} ({Path})";
}
