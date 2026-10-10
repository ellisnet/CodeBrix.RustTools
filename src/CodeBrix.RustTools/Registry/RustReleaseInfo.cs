using System;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Registry;

/// <summary>
/// The tool versions in one Rust release channel manifest
/// (<c>https://static.rust-lang.org/dist/channel-rust-stable.toml</c> for stable).
/// </summary>
public sealed class RustReleaseInfo
{
    /// <summary>The channel the manifest describes (<c>stable</c>, <c>beta</c> or
    /// <c>nightly</c>).</summary>
    public string Channel { get; init; } = "stable";

    /// <summary>The manifest's publication date, or <c>null</c> when absent.</summary>
    public DateOnly? Date { get; init; }

    /// <summary>The rustc version of the release; this is also the version that
    /// <c>cargo --version</c> reports for the matching cargo.</summary>
    public ToolVersion RustcVersion { get; init; }

    /// <summary>The version cargo reports on the command line for this release. Cargo's own
    /// package number in the manifest differs (see <see cref="CargoPackageVersion"/>), so this
    /// equals <see cref="RustcVersion"/>.</summary>
    public ToolVersion CargoVersion => RustcVersion;

    /// <summary>Cargo's internal package version from the manifest (for example
    /// <c>0.100.0</c>); it does not match the version cargo prints.</summary>
    public ToolVersion CargoPackageVersion { get; init; }

    /// <summary>The clippy version (comparable with <c>cargo clippy --version</c>), or
    /// <c>null</c>.</summary>
    public ToolVersion ClippyVersion { get; init; }

    /// <summary>The rustfmt version (comparable with <c>rustfmt --version</c>), or
    /// <c>null</c>.</summary>
    public ToolVersion RustfmtVersion { get; init; }
}
