namespace CodeBrix.RustTools.Building;

/// <summary>
/// Where a <see cref="CompilerDiagnostic"/> came from.
/// </summary>
public enum DiagnosticOrigin
{
    /// <summary>The Rust compiler (or clippy) compiling a crate's source code.</summary>
    Rustc,

    /// <summary>A package's build script (<c>build.rs</c>): either a <c>cargo::warning=</c>
    /// line it printed, or a compiler message about the build script's own source.</summary>
    BuildScript,

    /// <summary>Cargo itself (resolution, manifest and command-level messages), or rustfmt
    /// for <see cref="CargoCommand.Fmt"/>.</summary>
    Cargo,
}
