using System;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// One published version of a crate, as listed by crates.io.
/// </summary>
public sealed class CrateVersionInfo
{
    /// <summary>The version text exactly as published (for example <c>1.0.18</c> or
    /// <c>0.4.0-alpha.2</c>).</summary>
    public string Number { get; init; } = string.Empty;

    /// <summary>The parsed version, or <c>null</c> when <see cref="Number"/> is not a valid
    /// version.</summary>
    public ToolVersion Version { get; init; }

    /// <summary>True when the version has been yanked (new lockfiles will not select it).</summary>
    public bool IsYanked { get; init; }

    /// <summary>The SPDX license expression declared by this version (for example
    /// <c>MIT OR Apache-2.0</c>), or <c>null</c> when the crate uses a license file only.</summary>
    public string License { get; init; }

    /// <summary>When the version was published, or <c>null</c>.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>The minimum supported Rust version declared by this version
    /// (<c>rust-version</c>), or <c>null</c>.</summary>
    public string RustVersion { get; init; }

    /// <summary>True when the version has a pre-release label (<c>-alpha</c>, <c>-rc.1</c>, ...).</summary>
    public bool IsPreRelease => Version?.PreRelease != null;

    /// <summary>
    /// The version number, with <c>(yanked)</c> appended when yanked.
    /// </summary>
    public override string ToString() => IsYanked ? Number + " (yanked)" : Number;
}
