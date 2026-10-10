using System;
using System.Collections.Generic;

namespace CodeBrix.RustTools.Projects;

/// <summary>
/// One <c>[profile.NAME]</c> table of a Cargo.toml (the common settings only).
/// </summary>
public sealed class CargoProfileDeclaration
{
    /// <summary>The profile name (<c>dev</c>, <c>release</c>, <c>test</c>, <c>bench</c>, or a
    /// custom name).</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The <c>opt-level</c> as text (<c>0</c>-<c>3</c>, <c>s</c>, <c>z</c>), or
    /// <c>null</c>.</summary>
    public string OptLevel { get; init; }

    /// <summary>The <c>inherits</c> value of a custom profile, or <c>null</c>.</summary>
    public string Inherits { get; init; }

    /// <summary>The <c>lto</c> setting as text (<c>true</c>, <c>thin</c>, <c>fat</c>,
    /// <c>off</c>, ...), or <c>null</c>.</summary>
    public string Lto { get; init; }

    /// <summary>The <c>codegen-units</c> value, or <c>null</c>.</summary>
    public long? CodegenUnits { get; init; }

    /// <summary>The <c>debug</c> setting as text, or <c>null</c>.</summary>
    public string Debug { get; init; }

    /// <summary>The package selectors of <c>[profile.NAME.package.X]</c> overrides
    /// (for example <c>*</c>).</summary>
    public IReadOnlyList<string> PackageOverrides { get; init; } = Array.Empty<string>();

    /// <inheritdoc />
    public override string ToString() => OptLevel == null ? Name : $"{Name} (opt-level {OptLevel})";
}
